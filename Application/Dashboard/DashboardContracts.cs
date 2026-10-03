using NETRA.Application.Analytics;

namespace NETRA.Application.Dashboard;

public sealed class DashboardDto
{
    public int TotalCases { get; init; }
    public int ActiveCases { get; init; }
    public int HighRiskAreaCount { get; init; }
    public int ActiveAlerts { get; init; }
    public DateTime TrendFrom { get; init; }
    public DateTime TrendTo { get; init; }
    public IReadOnlyList<TrendPointDto> CrimeTrend { get; init; } = Array.Empty<TrendPointDto>();
    public IReadOnlyList<CategoryCountDto> CrimeDistribution { get; init; } = Array.Empty<CategoryCountDto>();
    public IReadOnlyList<DistrictSeverityDto> DistrictComparison { get; init; } = Array.Empty<DistrictSeverityDto>();
    public IReadOnlyList<HighRiskAreaDto> HighRiskAreas { get; init; } = Array.Empty<HighRiskAreaDto>();
    public IReadOnlyList<RecentAlertDto> RecentAlerts { get; init; } = Array.Empty<RecentAlertDto>();
}

public sealed class HighRiskAreaDto
{
    public Guid DistrictId { get; init; }
    public string DistrictName { get; init; } = string.Empty;
    public int AssessedCaseCount { get; init; }
    public decimal MaximumRecordedScore { get; init; }
    public int MaximumRiskLevel { get; init; }
}

public sealed class RecentAlertDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public Guid? CaseId { get; init; }
    public string? CaseNumber { get; init; }
}
