using System.Globalization;
using Microsoft.AspNetCore.Html;
using NETRA.Application.Analytics;
using NETRA.Application.Risk;
using NETRA.Web.Models.Navigation;
using NETRA.Web.Models.Ui;
using NETRA.Web.Models.ViewModels.Analytics;
using NETRA.Web.Services.Charts;

namespace NETRA.Web.Models.ViewModels.Risk;

/// <summary>
/// Presentation model for Risk &amp; Anomaly Analysis. Every figure comes from
/// <see cref="RiskAnalysisDto"/> database aggregates; nothing is invented here.
/// </summary>
public sealed class RiskViewModel
{
    public required RiskAnalysisDto Analysis { get; init; }
    public required AnalyticsFilterViewModel Filter { get; init; }
    public required AnalyticsLookupDto Lookups { get; init; }

    public IReadOnlyList<AreaRiskRowItem> Rows { get; init; } = Array.Empty<AreaRiskRowItem>();
    public IReadOnlyList<AnomalyCardItem> Anomalies { get; init; } = Array.Empty<AnomalyCardItem>();
    public IReadOnlyList<StatCardModel> SummaryCards { get; init; } = Array.Empty<StatCardModel>();

    public string WindowLabel =>
        $"{Analysis.DateFrom:dd MMM yyyy} – {Analysis.DateTo:dd MMM yyyy}";

    public string ComparisonLabel =>
        $"{Analysis.ComparisonFrom:dd MMM yyyy} – {Analysis.ComparisonTo:dd MMM yyyy}";

    public bool HasCases => Analysis.TotalCasesInWindow > 0;

    public static RiskViewModel Create(
        RiskAnalysisDto analysis,
        AnalyticsFilterViewModel filter,
        AnalyticsLookupDto lookups,
        SvgSparklineRenderer sparklines)
    {
        return new RiskViewModel
        {
            Analysis = analysis,
            Filter = filter,
            Lookups = lookups,
            SummaryCards =
            [
                new StatCardModel
                {
                    Label = "Cases In Window",
                    Value = analysis.TotalCasesInWindow.ToString("N0", CultureInfo.InvariantCulture),
                    Meta = analysis.WindowLabelShort(),
                    Tone = NetraTone.Primary,
                    Icon = NetraIcon.Cases
                },
                new StatCardModel
                {
                    Label = "Areas Scored",
                    Value = analysis.Indicators.Count(i => !i.InsufficientData)
                        .ToString(CultureInfo.InvariantCulture),
                    Meta = $"{analysis.Indicators.Count(i => i.InsufficientData)} awaiting sufficient data",
                    Tone = NetraTone.Info,
                    Icon = NetraIcon.Map
                },
                new StatCardModel
                {
                    Label = "Anomalies Detected",
                    Value = analysis.Anomalies.Count.ToString(CultureInfo.InvariantCulture),
                    Meta = "Compared with the previous period",
                    Tone = analysis.Anomalies.Count > 0 ? NetraTone.Warning : NetraTone.Success,
                    Icon = NetraIcon.Alert
                },
                new StatCardModel
                {
                    Label = "Elevated Or Above",
                    Value = analysis.Indicators.Count(i => i.Level >= 3).ToString(CultureInfo.InvariantCulture),
                    Meta = "Analytical indicators, not predictions",
                    Tone = NetraTone.Danger,
                    Icon = NetraIcon.Shield
                }
            ],
            Rows = analysis.Indicators.Select(i => new AreaRiskRowItem
            {
                DistrictName = i.DistrictName,
                DistrictCode = i.DistrictCode,
                CrimeCount = i.CurrentCount.ToString("N0", CultureInfo.InvariantCulture),
                Sparkline = sparklines.Render(i.WeeklyCounts, "Weekly case counts"),
                TrendText = TrendLabel(i),
                TrendTone = TrendTone(i),
                ScoreText = i.Score?.ToString("0.#", CultureInfo.InvariantCulture) ?? "—",
                Level = i.Level,
                LevelLabel = i.LevelLabel,
                LevelTone = LevelTone(i),
                Explanation = i.Explanation,
                InsufficientData = i.InsufficientData
            }).ToList(),
            Anomalies = analysis.Anomalies.Select(a => new AnomalyCardItem
            {
                Title = a.Title,
                SeverityLabel = a.Severity,
                SeverityTone = a.Severity switch
                {
                    "Critical" => NetraTone.Danger,
                    "High" => NetraTone.Danger,
                    "Medium" => NetraTone.Warning,
                    _ => NetraTone.Info
                },
                TypeLabel = AnomalyTypes.Label(a.AnomalyType),
                Description = a.Description,
                MetricLabel = a.DeviationPercent is not null ? "Change vs comparison period" : "Confidence",
                MetricValue = a.DeviationPercent is { } pct
                    ? $"+{pct.ToString("0.#", CultureInfo.InvariantCulture)}%"
                    : $"{Math.Round(a.Confidence * 100m, 1).ToString("0.#", CultureInfo.InvariantCulture)}%",
                DistrictName = a.DistrictName
            }).ToList()
        };
    }

    private static string TrendLabel(RiskIndicatorDto i)
    {
        if (i.ChangePercent is not { } pct)
        {
            return i.BaselineCount == 0 && i.CurrentCount > 0
                ? "↑ new activity"
                : "— no comparison";
        }

        var arrow = pct > 0 ? "↗" : pct < 0 ? "↘" : "→";
        return $"{arrow} {(pct >= 0 ? "+" : "")}{pct.ToString("0.#", CultureInfo.InvariantCulture)}%";
    }

    private static NetraTone TrendTone(RiskIndicatorDto i) => i.ChangePercent switch
    {
        null => NetraTone.Neutral,
        > 0 => NetraTone.Danger,
        < 0 => NetraTone.Success,
        _ => NetraTone.Neutral
    };

    private static NetraTone LevelTone(RiskIndicatorDto i) => i.Level switch
    {
        5 => NetraTone.Danger,
        4 => NetraTone.Danger,
        3 => NetraTone.Warning,
        2 => NetraTone.Neutral,
        1 => NetraTone.Success,
        _ => NetraTone.Neutral
    };
}

/// <summary>One Area Risk Analysis table row, fully formatted for display.</summary>
public sealed class AreaRiskRowItem
{
    public required string DistrictName { get; init; }
    public required string DistrictCode { get; init; }
    public required string CrimeCount { get; init; }
    public required HtmlString Sparkline { get; init; }
    public required string TrendText { get; init; }
    public required NetraTone TrendTone { get; init; }
    public required string ScoreText { get; init; }
    public required int Level { get; init; }
    public required string LevelLabel { get; init; }
    public required NetraTone LevelTone { get; init; }
    public required string Explanation { get; init; }
    public required bool InsufficientData { get; init; }
}

/// <summary>One Detected Anomalies card.</summary>
public sealed class AnomalyCardItem
{
    public required string Title { get; init; }
    public required string SeverityLabel { get; init; }
    public required NetraTone SeverityTone { get; init; }
    public required string TypeLabel { get; init; }
    public required string Description { get; init; }
    public required string MetricLabel { get; init; }
    public required string MetricValue { get; init; }
    public required string DistrictName { get; init; }
}

file static class RiskViewModelExtensions
{
    public static string WindowLabelShort(this RiskAnalysisDto analysis) =>
        $"Window {analysis.DateFrom:dd MMM} – {analysis.DateTo:dd MMM yyyy}";
}
