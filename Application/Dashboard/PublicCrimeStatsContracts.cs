using System.Text.Json.Serialization;

namespace NETRA.Application.Dashboard;

public sealed class PublicCrimeStatsDto
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("report")]
    public string Report { get; init; } = string.Empty;

    [JsonPropertyName("geography")]
    public string Geography { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; init; } = string.Empty;

    [JsonPropertyName("dataType")]
    public string DataType { get; init; } = string.Empty;

    [JsonPropertyName("years")]
    public IReadOnlyList<int> Years { get; init; } = Array.Empty<int>();

    [JsonPropertyName("annualCases")]
    public IReadOnlyList<CityAnnualCasesDto> AnnualCases { get; init; } = Array.Empty<CityAnnualCasesDto>();

    [JsonPropertyName("gujaratTotals")]
    public IReadOnlyList<YearlyTotalDto> GujaratTotals { get; init; } = Array.Empty<YearlyTotalDto>();

    [JsonPropertyName("cheatingCases")]
    public IReadOnlyList<YearlyTotalDto> CheatingCases { get; init; } = Array.Empty<YearlyTotalDto>();

    [JsonPropertyName("reportedMotives2023")]
    public IReadOnlyList<ReportedMotiveDto> ReportedMotives2023 { get; init; } = Array.Empty<ReportedMotiveDto>();

    [JsonPropertyName("chargeSheetingRatePercent")]
    public ChargeSheetingRateDto ChargeSheetingRatePercent { get; init; } = new();
}

public sealed class CityAnnualCasesDto
{
    [JsonPropertyName("year")]
    public int Year { get; init; }

    [JsonPropertyName("city")]
    public string City { get; init; } = string.Empty;

    [JsonPropertyName("cases")]
    public int Cases { get; init; }
}

public sealed class YearlyTotalDto
{
    [JsonPropertyName("year")]
    public int Year { get; init; }

    [JsonPropertyName("cases")]
    public int Cases { get; init; }
}

public sealed class ReportedMotiveDto
{
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("cases")]
    public int Cases { get; init; }
}

public sealed class ChargeSheetingRateDto
{
    [JsonPropertyName("gujarat")]
    public decimal Gujarat { get; init; }

    [JsonPropertyName("national")]
    public decimal National { get; init; }
}
