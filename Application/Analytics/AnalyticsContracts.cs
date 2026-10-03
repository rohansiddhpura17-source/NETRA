namespace NETRA.Application.Analytics;

public sealed class AnalyticsFilterCriteria
{
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
    public Guid? DistrictId { get; init; }
    public Guid? CrimeTypeId { get; init; }
    public string? Severity { get; init; }
}

public sealed class AnalyticsReportDto
{
    public DateTime DateFrom { get; init; }
    public DateTime DateTo { get; init; }
    public int TotalCrimeVolume { get; init; }
    public int HighSeverityCount { get; init; }
    public decimal ClearanceRate { get; init; }
    public decimal AverageDailyVolume { get; init; }
    public IReadOnlyList<TrendPointDto> MonthlyTrend { get; init; } = Array.Empty<TrendPointDto>();
    public IReadOnlyList<CategoryCountDto> CrimeByType { get; init; } = Array.Empty<CategoryCountDto>();
    public IReadOnlyList<DistrictSeverityDto> CrimeByDistrict { get; init; } = Array.Empty<DistrictSeverityDto>();
    public IReadOnlyList<CategoryCountDto> SeverityDistribution { get; init; } = Array.Empty<CategoryCountDto>();
    public IReadOnlyList<CategoryCountDto> DayOfWeekPattern { get; init; } = Array.Empty<CategoryCountDto>();
    public IReadOnlyList<CategoryCountDto> TimeOfDayPattern { get; init; } = Array.Empty<CategoryCountDto>();
    public AnalyticsHighlightsDto Highlights { get; init; } = new();
}

public sealed class AnalyticsLookupDto
{
    public IReadOnlyList<AnalyticsLookupItemDto> Districts { get; init; } = Array.Empty<AnalyticsLookupItemDto>();
    public IReadOnlyList<AnalyticsLookupItemDto> CrimeTypes { get; init; } = Array.Empty<AnalyticsLookupItemDto>();
}

public sealed record AnalyticsLookupItemDto(Guid Id, string Name);

public sealed record TrendPointDto(DateTime PeriodStart, string Label, int Count);

public sealed record CategoryCountDto(string Label, int Count);

public sealed class DistrictSeverityDto
{
    public Guid DistrictId { get; init; }
    public string DistrictName { get; init; } = string.Empty;
    public int Low { get; init; }
    public int Medium { get; init; }
    public int High { get; init; }
    public int Critical { get; init; }
    public int Total => Low + Medium + High + Critical;
}

public sealed class AnalyticsHighlightsDto
{
    public string? LeadingCrimeType { get; init; }
    public int LeadingCrimeTypeCount { get; init; }
    public string? HighestVolumeDistrict { get; init; }
    public int HighestVolumeDistrictCount { get; init; }
    public string? PeakMonth { get; init; }
    public int PeakMonthCount { get; init; }
    public string? PeakDayOfWeek { get; init; }
    public string? PeakTimeOfDay { get; init; }
}
