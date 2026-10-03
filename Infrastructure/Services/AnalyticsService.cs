using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Analytics;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

public sealed class AnalyticsService : IAnalyticsService
{
    private readonly NetraDbContext _context;
    private readonly TimeProvider _timeProvider;

    public AnalyticsService(NetraDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<AnalyticsReportDto> GetReportAsync(
        AnalyticsFilterCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var today = _timeProvider.GetUtcNow().UtcDateTime.Date;
        var dateTo = (criteria.DateTo ?? today).Date;
        var dateFrom = (criteria.DateFrom ?? new DateTime(dateTo.Year, dateTo.Month, 1).AddMonths(-11)).Date;

        if (dateFrom > dateTo)
        {
            throw new ArgumentException("Date From must be on or before Date To.", nameof(criteria));
        }

        var query = CaseQueryFilters.Apply(_context.CrimeCases.AsNoTracking(), criteria, dateFrom, dateTo);

        var total = await query.CountAsync(cancellationToken);
        var highSeverity = await query.CountAsync(
            c => c.Severity == CaseSeverity.High || c.Severity == CaseSeverity.Critical,
            cancellationToken);
        var cleared = await query.CountAsync(
            c => c.Status == CaseStatus.Closed || c.Status == CaseStatus.Archived,
            cancellationToken);

        var monthlyRows = await query
            .GroupBy(c => new { c.IncidentDate.Year, c.IncidentDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .OrderBy(x => x.Year)
            .ThenBy(x => x.Month)
            .ToListAsync(cancellationToken);

        var crimeTypeRows = await query
            .GroupBy(c => new { c.CrimeTypeId, c.CrimeType.Name })
            .Select(g => new { g.Key.Name, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var severityRows = await query
            .GroupBy(c => c.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var districtRows = (await query
            .SelectMany(c => c.CaseLocations
                .Where(cl => cl.LocationRole == CaseService.IncidentLocationRole
                             && cl.Location != null
                             && cl.Location.DistrictId != null)
                .Select(cl => new
                {
                    c.Id,
                    DistrictId = cl.Location!.DistrictId!.Value,
                    DistrictName = cl.Location.District!.Name,
                    c.Severity
                }))
            .Distinct()
            .GroupBy(x => new { x.DistrictId, x.DistrictName })
            .Select(g => new
            {
                g.Key.DistrictId,
                g.Key.DistrictName,
                Low = g.Count(x => x.Severity == CaseSeverity.Low),
                Medium = g.Count(x => x.Severity == CaseSeverity.Medium),
                High = g.Count(x => x.Severity == CaseSeverity.High),
                Critical = g.Count(x => x.Severity == CaseSeverity.Critical)
            })
            .OrderByDescending(x => x.Low + x.Medium + x.High + x.Critical)
            .ThenBy(x => x.DistrictName)
            .ToListAsync(cancellationToken))
            .Select(x => new DistrictSeverityDto
            {
                DistrictId = x.DistrictId,
                DistrictName = x.DistrictName,
                Low = x.Low,
                Medium = x.Medium,
                High = x.High,
                Critical = x.Critical
            })
            .ToList();

        var dailyRows = await query
            .GroupBy(c => c.IncidentDate.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var hourlyRows = await query
            .GroupBy(c => c.IncidentDate.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var trend = FillMonthlyTrend(dateFrom, dateTo, monthlyRows
            .ToDictionary(x => (x.Year, x.Month), x => x.Count));
        var crimeByType = crimeTypeRows.Select(x => new CategoryCountDto(x.Name, x.Count)).ToList();
        var severityDistribution = Enum.GetValues<CaseSeverity>()
            .Select(severity => new CategoryCountDto(
                severity.ToString(),
                severityRows.FirstOrDefault(x => x.Severity == severity)?.Count ?? 0))
            .ToList();
        var dayOfWeek = BuildDayOfWeekPattern(dailyRows.Select(x => (x.Date, x.Count)));
        var timeOfDay = BuildTimeOfDayPattern(hourlyRows.Select(x => (x.Hour, x.Count)));
        var topDistrict = districtRows.FirstOrDefault();
        var peakMonth = trend.Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.PeriodStart)
            .FirstOrDefault();
        var peakDay = dayOfWeek.Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();
        var peakTime = timeOfDay.Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();
        var leadingType = crimeByType.FirstOrDefault();
        var dayCount = Math.Max(1, (dateTo - dateFrom).Days + 1);

        return new AnalyticsReportDto
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            TotalCrimeVolume = total,
            HighSeverityCount = highSeverity,
            ClearanceRate = total == 0 ? 0 : Math.Round(cleared * 100m / total, 1),
            AverageDailyVolume = Math.Round(total / (decimal)dayCount, 1),
            MonthlyTrend = trend,
            CrimeByType = crimeByType,
            CrimeByDistrict = districtRows,
            SeverityDistribution = severityDistribution,
            DayOfWeekPattern = dayOfWeek,
            TimeOfDayPattern = timeOfDay,
            Highlights = new AnalyticsHighlightsDto
            {
                LeadingCrimeType = leadingType?.Label,
                LeadingCrimeTypeCount = leadingType?.Count ?? 0,
                HighestVolumeDistrict = topDistrict?.DistrictName,
                HighestVolumeDistrictCount = topDistrict?.Total ?? 0,
                PeakMonth = peakMonth?.Label,
                PeakMonthCount = peakMonth?.Count ?? 0,
                PeakDayOfWeek = peakDay?.Label,
                PeakTimeOfDay = peakTime?.Label
            }
        };
    }

    public async Task<AnalyticsLookupDto> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        var districts = await _context.Districts
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new AnalyticsLookupItemDto(d.Id, d.Name))
            .ToListAsync(cancellationToken);

        var crimeTypes = await _context.CrimeTypes
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Select(c => new AnalyticsLookupItemDto(c.Id, c.Name))
            .ToListAsync(cancellationToken);

        return new AnalyticsLookupDto { Districts = districts, CrimeTypes = crimeTypes };
    }

    private static IReadOnlyList<TrendPointDto> FillMonthlyTrend(
        DateTime dateFrom,
        DateTime dateTo,
        IReadOnlyDictionary<(int Year, int Month), int> counts)
    {
        var result = new List<TrendPointDto>();
        var month = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var lastMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (month <= lastMonth)
        {
            counts.TryGetValue((month.Year, month.Month), out var count);
            result.Add(new TrendPointDto(month, month.ToString("MMM yy", CultureInfo.InvariantCulture), count));
            month = month.AddMonths(1);
        }

        return result;
    }

    private static IReadOnlyList<CategoryCountDto> BuildDayOfWeekPattern(
        IEnumerable<(DateTime Date, int Count)> rows)
    {
        var counts = rows
            .GroupBy(x => x.Date.DayOfWeek)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
        var order = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };

        return order.Select(day => new CategoryCountDto(
            day.ToString()[..3], counts.GetValueOrDefault(day))).ToList();
    }

    private static IReadOnlyList<CategoryCountDto> BuildTimeOfDayPattern(
        IEnumerable<(int Hour, int Count)> rows)
    {
        var bands = new[]
        {
            (Label: "00–05", From: 0, To: 6),
            (Label: "06–11", From: 6, To: 12),
            (Label: "12–17", From: 12, To: 18),
            (Label: "18–23", From: 18, To: 24)
        };

        return bands.Select(band => new CategoryCountDto(
            band.Label,
            rows.Where(x => x.Hour >= band.From && x.Hour < band.To).Sum(x => x.Count))).ToList();
    }
}
