using Microsoft.EntityFrameworkCore;
using NETRA.Application.Alerts;
using NETRA.Application.Analytics;
using NETRA.Application.Dashboard;
using NETRA.Domain.Enums;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

public sealed class DashboardService : IDashboardService
{
    private readonly NetraDbContext _context;
    private readonly IAnalyticsService _analytics;
    private readonly TimeProvider _timeProvider;

    public DashboardService(
        NetraDbContext context,
        IAnalyticsService analytics,
        TimeProvider timeProvider)
    {
        _context = context;
        _analytics = analytics;
        _timeProvider = timeProvider;
    }

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var today = _timeProvider.GetUtcNow().UtcDateTime.Date;
        var trendFrom = new DateTime(today.Year, today.Month, 1).AddMonths(-11);

        var analytics = await _analytics.GetReportAsync(
            new AnalyticsFilterCriteria { DateFrom = trendFrom, DateTo = today },
            cancellationToken);

        var totalCases = await _context.CrimeCases
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var activeCases = await _context.CrimeCases
            .AsNoTracking()
            .CountAsync(c => c.Status != CaseStatus.Closed && c.Status != CaseStatus.Archived, cancellationToken);

        var activeAlerts = await _context.Alerts
            .AsNoTracking()
            .CountAsync(AlertQueryFilters.IsActive, cancellationToken);

        var alertRows = await _context.Alerts
            .AsNoTracking()
            .Where(AlertQueryFilters.IsActive)
            .OrderByDescending(a => a.CreatedAt)
            .Take(5)
            .Select(a => new
            {
                a.Id,
                a.Title,
                a.Severity,
                a.Status,
                a.CreatedAt,
                a.CrimeCaseId,
                CaseNumber = a.CrimeCase == null ? null : a.CrimeCase.CaseNumber
            })
            .ToListAsync(cancellationToken);

        // Only case-linked assessments identify an incident district; area-level
        // rows (DistrictId set, CrimeCaseId null) are reported on the Risk page.
        var riskAreas = (await _context.RiskAssessments
            .AsNoTracking()
            .Where(r => r.RiskLevel >= 3 && r.CrimeCaseId != null)
            .SelectMany(r => r.CrimeCase!.CaseLocations
                .Where(cl => cl.LocationRole == CaseService.IncidentLocationRole
                             && cl.Location != null
                             && cl.Location.DistrictId != null)
                .Select(cl => new
                {
                    r.CrimeCaseId,
                    DistrictId = cl.Location!.DistrictId!.Value,
                    DistrictName = cl.Location.District!.Name,
                    r.RiskScore,
                    r.RiskLevel
                }))
            .Distinct()
            .GroupBy(x => new { x.DistrictId, x.DistrictName })
            .Select(g => new
            {
                g.Key.DistrictId,
                g.Key.DistrictName,
                AssessedCaseCount = g.Select(x => x.CrimeCaseId).Distinct().Count(),
                MaximumRecordedScore = g.Max(x => x.RiskScore),
                MaximumRiskLevel = g.Max(x => x.RiskLevel)
            })
            .OrderByDescending(x => x.MaximumRecordedScore)
            .ThenByDescending(x => x.AssessedCaseCount)
            .ThenBy(x => x.DistrictName)
            .ToListAsync(cancellationToken))
            .Select(x => new HighRiskAreaDto
            {
                DistrictId = x.DistrictId,
                DistrictName = x.DistrictName,
                AssessedCaseCount = x.AssessedCaseCount,
                MaximumRecordedScore = x.MaximumRecordedScore,
                MaximumRiskLevel = x.MaximumRiskLevel
            })
            .ToList();

        return new DashboardDto
        {
            TotalCases = totalCases,
            ActiveCases = activeCases,
            HighRiskAreaCount = riskAreas.Count,
            ActiveAlerts = activeAlerts,
            TrendFrom = analytics.DateFrom,
            TrendTo = analytics.DateTo,
            CrimeTrend = analytics.MonthlyTrend,
            CrimeDistribution = analytics.CrimeByType,
            DistrictComparison = analytics.CrimeByDistrict,
            HighRiskAreas = riskAreas.Take(5).ToList(),
            RecentAlerts = alertRows.Select(a => new RecentAlertDto
            {
                Id = a.Id,
                Title = a.Title,
                Severity = a.Severity.ToString(),
                Status = a.Status.ToString(),
                CreatedAt = a.CreatedAt,
                CaseId = a.CrimeCaseId,
                CaseNumber = a.CaseNumber
            }).ToList()
        };
    }
}
