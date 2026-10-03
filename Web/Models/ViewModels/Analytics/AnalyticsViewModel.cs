using System.Globalization;
using NETRA.Application.Analytics;
using NETRA.Web.Models.Ui;
using NETRA.Web.Models.Navigation;
using NETRA.Web.Services.Charts;

namespace NETRA.Web.Models.ViewModels.Analytics;

/// <summary>Query-string state for the Crime Analytics filter band.</summary>
public sealed class AnalyticsFilterViewModel
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public Guid? DistrictId { get; set; }
    public Guid? CrimeTypeId { get; set; }
    public string? Severity { get; set; }

    public bool IsFiltered =>
        DateFrom is not null || DateTo is not null ||
        DistrictId is not null || CrimeTypeId is not null ||
        !string.IsNullOrWhiteSpace(Severity);

    public AnalyticsFilterCriteria ToCriteria() => new()
    {
        DateFrom = DateFrom,
        DateTo = DateTo,
        DistrictId = DistrictId,
        CrimeTypeId = CrimeTypeId,
        Severity = string.IsNullOrWhiteSpace(Severity) ? null : Severity
    };

    public static readonly string[] SeverityOptions = ["Low", "Medium", "High", "Critical"];
}

/// <summary>
/// Presentation model for Crime Analytics. Every figure comes from
/// <see cref="AnalyticsReportDto"/> database aggregates.
/// </summary>
public sealed class AnalyticsViewModel
{
    public required AnalyticsReportDto Report { get; init; }
    public required AnalyticsFilterViewModel Filter { get; init; }
    public required AnalyticsLookupDto Lookups { get; init; }

    public IReadOnlyList<StatCardModel> MetricCards { get; init; } = Array.Empty<StatCardModel>();
    public SvgChartModel? TrendChart { get; init; }
    public SvgChartModel? TypeChart { get; init; }
    public SvgChartModel? SeverityDistrictChart { get; init; }
    public SvgChartModel? DayOfWeekChart { get; init; }
    public SvgChartModel? TimeOfDayChart { get; init; }
    public IReadOnlyList<string> Insights { get; init; } = Array.Empty<string>();

    public string PeriodLabel =>
        $"{Report.DateFrom:dd MMM yyyy} – {Report.DateTo:dd MMM yyyy}";

    public static AnalyticsViewModel Create(
        AnalyticsReportDto report,
        AnalyticsFilterViewModel filter,
        AnalyticsLookupDto lookups)
    {
        return new AnalyticsViewModel
        {
            Report = report,
            Filter = filter,
            Lookups = lookups,
            MetricCards =
            [
                new StatCardModel
                {
                    Label = "Total Crimes",
                    Value = report.TotalCrimeVolume.ToString("N0", CultureInfo.InvariantCulture),
                    Meta = $"{report.HighSeverityCount} high or critical severity",
                    Tone = NetraTone.Primary,
                    Icon = NetraIcon.Cases
                },
                new StatCardModel
                {
                    Label = "Avg Cases Per Day",
                    Value = report.AverageDailyVolume.ToString("0.0", CultureInfo.InvariantCulture),
                    Meta = $"Across {(report.DateTo - report.DateFrom).Days + 1} days",
                    Tone = NetraTone.Info,
                    Icon = NetraIcon.Analytics
                },
                new StatCardModel
                {
                    Label = "Most Common Crime",
                    Value = report.Highlights.LeadingCrimeType ?? "—",
                    Meta = LeadingCrimeTypeCountLabel(report),
                    Tone = NetraTone.Warning,
                    Icon = NetraIcon.Alert
                },
                new StatCardModel
                {
                    Label = "Hotspot District",
                    Value = report.Highlights.HighestVolumeDistrict ?? "—",
                    Meta = HighestVolumeDistrictCountLabel(report),
                    Tone = NetraTone.Danger,
                    Icon = NetraIcon.Map
                }
            ],
            TrendChart = HasValues(report.MonthlyTrend)
                ? new SvgChartModel
                {
                    Title = "Crime trend over time",
                    Description = $"Line chart of monthly case counts from {report.MonthlyTrend[0].Label} " +
                        $"to {report.MonthlyTrend[^1].Label}, totalling {report.TotalCrimeVolume} cases.",
                    Categories = report.MonthlyTrend.Select(t => t.Label).ToList(),
                    Series = [new SvgChartSeries { Name = "Cases", Values = report.MonthlyTrend.Select(t => (decimal)t.Count).ToList() }]
                }
                : null,
            TypeChart = report.CrimeByType.Count > 0
                ? new SvgChartModel
                {
                    Title = "Crime type distribution",
                    Description = "Donut chart of cases by crime type: " +
                        string.Join(", ", report.CrimeByType.Select(c => $"{c.Label} {c.Count}")) + ".",
                    Categories = Top(report.CrimeByType, 6).Select(c => c.Label).ToList(),
                    Series = [new SvgChartSeries
                    {
                        Name = "Cases",
                        Values = Top(report.CrimeByType, 6).Select(c => (decimal)c.Count).ToList()
                    }]
                }
                : null,
            SeverityDistrictChart = report.CrimeByDistrict.Count > 0
                ? new SvgChartModel
                {
                    Title = "Severity by district",
                    Description = "Horizontal stacked bar chart of cases per district split by severity: " +
                        string.Join("; ", report.CrimeByDistrict.Select(d => $"{d.DistrictName} {d.Total}")) + ".",
                    Categories = report.CrimeByDistrict.Select(d => d.DistrictName).ToList(),
                    Series =
                    [
                        new SvgChartSeries { Name = "Low", Values = report.CrimeByDistrict.Select(d => (decimal)d.Low).ToList() },
                        new SvgChartSeries { Name = "Medium", Values = report.CrimeByDistrict.Select(d => (decimal)d.Medium).ToList() },
                        new SvgChartSeries { Name = "High", Values = report.CrimeByDistrict.Select(d => (decimal)d.High).ToList() },
                        new SvgChartSeries { Name = "Critical", Values = report.CrimeByDistrict.Select(d => (decimal)d.Critical).ToList() }
                    ]
                }
                : null,
            DayOfWeekChart = HasValues(report.DayOfWeekPattern)
                ? new SvgChartModel
                {
                    Title = "Cases by day of week",
                    Description = "Bar chart of case counts by day of week: " +
                        string.Join(", ", report.DayOfWeekPattern.Select(d => $"{d.Label} {d.Count}")) + ".",
                    Categories = report.DayOfWeekPattern.Select(d => d.Label).ToList(),
                    Series = [new SvgChartSeries { Name = "Cases", Values = report.DayOfWeekPattern.Select(d => (decimal)d.Count).ToList() }]
                }
                : null,
            TimeOfDayChart = HasValues(report.TimeOfDayPattern)
                ? new SvgChartModel
                {
                    Title = "Cases by time of day",
                    Description = "Bar chart of case counts by hour band: " +
                        string.Join(", ", report.TimeOfDayPattern.Select(t => $"{t.Label} {t.Count}")) + ".",
                    Categories = report.TimeOfDayPattern.Select(t => t.Label).ToList(),
                    Series = [new SvgChartSeries { Name = "Cases", Values = report.TimeOfDayPattern.Select(t => (decimal)t.Count).ToList() }]
                }
                : null,
            Insights = BuildInsights(report)
        };
    }

    private static bool HasValues(IReadOnlyList<TrendPointDto> trend) =>
        trend.Count > 0 && trend.Any(t => t.Count > 0);

    private static bool HasValues(IReadOnlyList<CategoryCountDto> categories) =>
        categories.Count > 0 && categories.Any(c => c.Count > 0);

    private static IReadOnlyList<CategoryCountDto> Top(
        IReadOnlyList<CategoryCountDto> categories,
        int limit)
    {
        if (categories.Count <= limit)
        {
            return categories;
        }

        var rest = categories.Skip(limit - 1).ToList();
        return [.. categories.Take(limit - 1),
            new CategoryCountDto("Other", rest.Sum(c => c.Count))];
    }

    private static IReadOnlyList<string> BuildInsights(AnalyticsReportDto report)
    {
        var insights = new List<string>();
        var h = report.Highlights;

        if (h.LeadingCrimeType is { } type)
        {
            insights.Add($"{type} is the most recorded crime type ({h.LeadingCrimeTypeCount} cases).");
        }

        if (h.HighestVolumeDistrict is { } district)
        {
            insights.Add($"{district} records the highest case volume ({h.HighestVolumeDistrictCount} cases).");
        }

        if (h.PeakMonth is { } month)
        {
            insights.Add($"Case volume peaks in {month} ({h.PeakMonthCount} cases).");
        }

        if (h.PeakDayOfWeek is { } day)
        {
            insights.Add($"Weekly case volumes peak on {day}.");
        }

        if (h.PeakTimeOfDay is { } band)
        {
            insights.Add($"Most incidents are recorded in the {band} hour band.");
        }

        if (report.TotalCrimeVolume > 0)
        {
            insights.Add($"High or critical severity cases: {report.HighSeverityCount} of {report.TotalCrimeVolume}.");
            insights.Add($"Clearance rate for the period: {report.ClearanceRate.ToString("0.#", CultureInfo.InvariantCulture)}%.");
        }

        return insights;
    }

    private static string LeadingCrimeTypeCountLabel(AnalyticsReportDto report) =>
        report.Highlights.LeadingCrimeType is null
            ? "No cases in period"
            : $"{report.Highlights.LeadingCrimeTypeCount} cases";

    private static string HighestVolumeDistrictCountLabel(AnalyticsReportDto report) =>
        report.Highlights.HighestVolumeDistrict is null
            ? "No cases in period"
            : $"{report.Highlights.HighestVolumeDistrictCount} cases";
}
