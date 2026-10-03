using System.Globalization;
using NETRA.Application.Dashboard;
using NETRA.Web.Models.Ui;
using NETRA.Web.Models.Navigation;
using NETRA.Web.Services.Charts;

namespace NETRA.Web.Models.ViewModels.Dashboard;

/// <summary>
/// Presentation model for the Intelligence Dashboard. All figures originate from
/// <see cref="DashboardDto"/> database aggregates; nothing is hard-coded here.
/// </summary>
public sealed class DashboardViewModel
{
    public required DashboardDto Data { get; init; }

    public IReadOnlyList<StatCardModel> StatCards { get; init; } = Array.Empty<StatCardModel>();
    public SvgChartModel? TrendChart { get; init; }
    public SvgChartModel? DistributionChart { get; init; }
    public SvgChartModel? DistrictChart { get; init; }
    public IReadOnlyList<HighRiskAreaItem> HighRiskAreas { get; init; } = Array.Empty<HighRiskAreaItem>();
    public IReadOnlyList<RecentAlertItem> RecentAlerts { get; init; } = Array.Empty<RecentAlertItem>();

    public string PeriodLabel =>
        $"{Data.TrendFrom:MMM yyyy} – {Data.TrendTo:MMM yyyy} (last 12 months)";

    public static DashboardViewModel Create(DashboardDto dto)
    {
        var trend = dto.CrimeTrend;
        var distribution = TopCategories(dto.CrimeDistribution, 6);
        var districts = dto.DistrictComparison;

        return new DashboardViewModel
        {
            Data = dto,
            StatCards =
            [
                new StatCardModel
                {
                    Label = "Total Cases",
                    Value = dto.TotalCases.ToString("N0", CultureInfo.InvariantCulture),
                    Meta = "All recorded crime cases",
                    Tone = NetraTone.Primary,
                    Icon = NetraIcon.Cases
                },
                new StatCardModel
                {
                    Label = "Active Cases",
                    Value = dto.ActiveCases.ToString("N0", CultureInfo.InvariantCulture),
                    Meta = "Open, investigating or awaiting review",
                    Tone = NetraTone.Info,
                    Icon = NetraIcon.Investigation
                },
                new StatCardModel
                {
                    Label = "High-Risk Areas",
                    Value = dto.HighRiskAreaCount.ToString("N0", CultureInfo.InvariantCulture),
                    Meta = "Districts with recorded high-risk assessments",
                    Tone = NetraTone.Warning,
                    Icon = NetraIcon.Map
                },
                new StatCardModel
                {
                    Label = "Active Alerts",
                    Value = dto.ActiveAlerts.ToString("N0", CultureInfo.InvariantCulture),
                    Meta = "Unresolved alerts awaiting review",
                    Tone = NetraTone.Danger,
                    Icon = NetraIcon.Bell
                }
            ],
            TrendChart = trend.Count == 0 || trend.All(t => t.Count == 0)
                ? null
                : new SvgChartModel
                {
                    Title = "Crime trend over the last 12 months",
                    Description = $"Line chart of monthly case counts between " +
                        $"{trend[0].Label} and {trend[^1].Label}, " +
                        $"totalling {trend.Sum(t => t.Count)} cases.",
                    Categories = trend.Select(t => t.Label).ToList(),
                    Series = [new SvgChartSeries { Name = "Cases", Values = trend.Select(t => (decimal)t.Count).ToList() }]
                },
            DistributionChart = distribution.Count == 0
                ? null
                : new SvgChartModel
                {
                    Title = "Crime distribution by type",
                    Description = $"Donut chart of case counts by crime type: " +
                        string.Join(", ", distribution.Select(d => $"{d.Label} {d.Count}")).TrimEnd('.'),
                    Categories = distribution.Select(d => d.Label).ToList(),
                    Series = [new SvgChartSeries { Name = "Cases", Values = distribution.Select(d => (decimal)d.Count).ToList() }]
                },
            DistrictChart = districts.Count == 0
                ? null
                : new SvgChartModel
                {
                    Title = "District comparison by case severity",
                    Description = $"Horizontal stacked bar chart of case counts per district split by severity. " +
                        string.Join("; ", districts.Select(d => $"{d.DistrictName}: {d.Total} cases")).TrimEnd('.') + '.',
                    Categories = districts.Select(d => d.DistrictName).ToList(),
                    Series =
                    [
                        new SvgChartSeries { Name = "Low", Values = districts.Select(d => (decimal)d.Low).ToList() },
                        new SvgChartSeries { Name = "Medium", Values = districts.Select(d => (decimal)d.Medium).ToList() },
                        new SvgChartSeries { Name = "High", Values = districts.Select(d => (decimal)d.High).ToList() },
                        new SvgChartSeries { Name = "Critical", Values = districts.Select(d => (decimal)d.Critical).ToList() }
                    ]
                },
            HighRiskAreas = dto.HighRiskAreas
                .Select(a => new HighRiskAreaItem(
                    a.DistrictName,
                    a.MaximumRecordedScore.ToString("0.0", CultureInfo.InvariantCulture),
                    RiskLevelLabel(a.MaximumRiskLevel),
                    a.AssessedCaseCount))
                .ToList(),
            RecentAlerts = dto.RecentAlerts
                .Select(a => new RecentAlertItem(
                    a.Title,
                    NetraDisplay.Words(a.Severity),
                    AlertSeverityTone(a.Severity),
                    a.CreatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture),
                    a.CaseId,
                    a.CaseNumber))
                .ToList()
        };
    }

    private static List<Application.Analytics.CategoryCountDto> TopCategories(
        IReadOnlyList<Application.Analytics.CategoryCountDto> categories,
        int limit)
    {
        if (categories.Count <= limit)
        {
            return categories.ToList();
        }

        var rest = categories.Skip(limit - 1).ToList();
        return categories.Take(limit - 1)
            .Append(new Application.Analytics.CategoryCountDto("Other", rest.Sum(c => c.Count)))
            .ToList();
    }

    private static string RiskLevelLabel(int level) => level switch
    {
        >= 5 => "Very High",
        4 => "High",
        3 => "Elevated",
        _ => "Moderate"
    };

    private static NetraTone AlertSeverityTone(string severity) => severity switch
    {
        "Critical" => NetraTone.Danger,
        "High" => NetraTone.Warning,
        "Medium" => NetraTone.Info,
        "Low" => NetraTone.Success,
        _ => NetraTone.Neutral
    };
}

public sealed record HighRiskAreaItem(
    string DistrictName,
    string MaximumScore,
    string RiskLevel,
    int AssessedCaseCount);

public sealed record RecentAlertItem(
    string Title,
    string Severity,
    NetraTone SeverityTone,
    string CreatedAt,
    Guid? CaseId,
    string? CaseNumber);
