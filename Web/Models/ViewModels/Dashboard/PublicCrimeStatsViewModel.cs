using System.Globalization;
using NETRA.Application.Dashboard;
using NETRA.Web.Models.Navigation;
using NETRA.Web.Models.Ui;
using NETRA.Web.Services.Charts;

namespace NETRA.Web.Models.ViewModels.Dashboard;

public sealed class PublicCrimeStatsViewModel
{
    public required PublicCrimeStatsDto Raw { get; init; }

    public IReadOnlyList<StatCardModel> StatCards { get; init; } = Array.Empty<StatCardModel>();

    public SvgChartModel? AnnualTotalsChart { get; init; }
    public SvgChartModel? CheatingComparisonChart { get; init; }
    public SvgChartModel? CityComparisonChart { get; init; }
    public SvgChartModel? MotiveBreakdownChart { get; init; }

    public string Title => Raw.Title;
    public string Report => Raw.Report;
    public string Geography => Raw.Geography;
    public string Source => Raw.Source;
    public string SourceUrl => Raw.SourceUrl;
    public string DataType => Raw.DataType;
    public string ReportingPeriod => "2021 - 2023";

    public decimal GujaratChargeSheetingRate => Raw.ChargeSheetingRatePercent.Gujarat;
    public decimal NationalChargeSheetingRate => Raw.ChargeSheetingRatePercent.National;

    public static PublicCrimeStatsViewModel Create(PublicCrimeStatsDto dto)
    {
        var guj2022 = dto.GujaratTotals.FirstOrDefault(t => t.Year == 2022)?.Cases ?? 1417;
        var guj2023 = dto.GujaratTotals.FirstOrDefault(t => t.Year == 2023)?.Cases ?? 1995;
        var gujGrowth = guj2022 > 0 ? ((decimal)(guj2023 - guj2022) / guj2022 * 100m) : 0m;

        var cheat2022 = dto.CheatingCases.FirstOrDefault(t => t.Year == 2022)?.Cases ?? 575;
        var cheat2023 = dto.CheatingCases.FirstOrDefault(t => t.Year == 2023)?.Cases ?? 1034;
        var cheatGrowth = cheat2022 > 0 ? ((decimal)(cheat2023 - cheat2022) / cheat2022 * 100m) : 0m;

        var ahd2021 = dto.AnnualCases.FirstOrDefault(c => c.City == "Ahmedabad" && c.Year == 2021)?.Cases ?? 447;
        var ahd2023 = dto.AnnualCases.FirstOrDefault(c => c.City == "Ahmedabad" && c.Year == 2023)?.Cases ?? 246;

        var surat2021 = dto.AnnualCases.FirstOrDefault(c => c.City == "Surat" && c.Year == 2021)?.Cases ?? 296;
        var surat2023 = dto.AnnualCases.FirstOrDefault(c => c.City == "Surat" && c.Year == 2023)?.Cases ?? 397;

        var cards = new List<StatCardModel>
        {
            new()
            {
                Label = "Gujarat 2023 Cybercrime",
                Value = guj2023.ToString("N0", CultureInfo.InvariantCulture),
                Meta = $"+{gujGrowth.ToString("0.0", CultureInfo.InvariantCulture)}% YoY (from {guj2022:N0} in 2022)",
                Tone = NetraTone.Warning,
                Icon = NetraIcon.Cases
            },
            new()
            {
                Label = "Cheating / Financial Fraud",
                Value = cheat2023.ToString("N0", CultureInfo.InvariantCulture),
                Meta = $"51.8% of total (+{cheatGrowth.ToString("0.0", CultureInfo.InvariantCulture)}% YoY)",
                Tone = NetraTone.Danger,
                Icon = NetraIcon.Shield
            },
            new()
            {
                Label = "Ahmedabad (2023)",
                Value = ahd2023.ToString("N0", CultureInfo.InvariantCulture),
                Meta = $"Down 45% vs 2021 ({ahd2021:N0} cases)",
                Tone = NetraTone.Info,
                Icon = NetraIcon.Map
            },
            new()
            {
                Label = "Surat (2023)",
                Value = surat2023.ToString("N0", CultureInfo.InvariantCulture),
                Meta = $"Up 34% vs 2021 ({surat2021:N0} cases)",
                Tone = NetraTone.Warning,
                Icon = NetraIcon.Map
            },
            new()
            {
                Label = "Gujarat Charge-Sheet Rate",
                Value = $"{dto.ChargeSheetingRatePercent.Gujarat.ToString("0.0", CultureInfo.InvariantCulture)}%",
                Meta = $"vs National Avg {dto.ChargeSheetingRatePercent.National.ToString("0.0", CultureInfo.InvariantCulture)}% (+18.2%)",
                Tone = NetraTone.Success,
                Icon = NetraIcon.Analytics
            }
        };

        var annualTotalsChart = new SvgChartModel
        {
            Title = "Gujarat State Cybercrime Totals (2022 vs 2023)",
            Description = $"Statewide cybercrime incidents rose from {guj2022:N0} cases in 2022 to {guj2023:N0} cases in 2023 (+{gujGrowth.ToString("0.0", CultureInfo.InvariantCulture)}%).",
            Categories = ["Gujarat (2022)", "Gujarat (2023)"],
            Series = [new SvgChartSeries { Name = "Statewide Cases", Values = [guj2022, guj2023] }]
        };

        var cheatingChart = new SvgChartModel
        {
            Title = "Cyber Cheating / Online Fraud Growth",
            Description = $"Cyber cheating cases increased from {cheat2022:N0} cases in 2022 to {cheat2023:N0} cases in 2023 (+{cheatGrowth.ToString("0.0", CultureInfo.InvariantCulture)}%).",
            Categories = ["Cheating (2022)", "Cheating (2023)"],
            Series = [new SvgChartSeries { Name = "Cheating Cases", Values = [cheat2022, cheat2023] }]
        };

        var cityChart = new SvgChartModel
        {
            Title = "Metropolitan City Comparison (2021 vs 2023)",
            Description = $"Comparative cybercrime volume in Ahmedabad ({ahd2021:N0} in 2021 to {ahd2023:N0} in 2023) and Surat ({surat2021:N0} in 2021 to {surat2023:N0} in 2023).",
            Categories = ["Ahmedabad (2021)", "Ahmedabad (2023)", "Surat (2021)", "Surat (2023)"],
            Series = [new SvgChartSeries { Name = "Cases Reported", Values = [ahd2021, ahd2023, surat2021, surat2023] }]
        };

        var motiveChart = new SvgChartModel
        {
            Title = "Reported Cybercrime Motives (Gujarat 2023)",
            Description = $"Primary motives reported: Fraud ({dto.ReportedMotives2023.FirstOrDefault(m => m.Category == "Fraud")?.Cases:N0}), Causing Disrepute ({dto.ReportedMotives2023.FirstOrDefault(m => m.Category == "Causing disrepute")?.Cases:N0}), and Extortion ({dto.ReportedMotives2023.FirstOrDefault(m => m.Category == "Extortion")?.Cases:N0}).",
            Categories = dto.ReportedMotives2023.Select(m => m.Category).ToList(),
            Series = [new SvgChartSeries { Name = "Cases by Motive", Values = dto.ReportedMotives2023.Select(m => (decimal)m.Cases).ToList() }]
        };

        return new PublicCrimeStatsViewModel
        {
            Raw = dto,
            StatCards = cards,
            AnnualTotalsChart = annualTotalsChart,
            CheatingComparisonChart = cheatingChart,
            CityComparisonChart = cityChart,
            MotiveBreakdownChart = motiveChart
        };
    }
}
