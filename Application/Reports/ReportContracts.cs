using System.Globalization;
using NETRA.Domain.Enums;

namespace NETRA.Application.Reports;

/// <summary>Bounded, controlled values for the report module.</summary>
public static class ReportLimits
{
    public const int DefaultPageSize = 12;
    public const int MaxPageSize = 50;
    public const int MinTitleLength = 6;
    public const int MaxTitleLength = 200;
    public const int MaxSummaryLength = 2000;
    public const int MaxContentLength = 8000;
    public const int DefaultPeriodDays = 30;
    public const int MaxPeriodDays = 3650;

    /// <summary>Rows shown inside a report table before the report says "and more".</summary>
    public const int MaxTableRows = 12;

    /// <summary>Linked cases listed in a case or investigation report.</summary>
    public const int MaxLinkedCases = 10;

    /// <summary>Upper bound on the case picker so a dropdown never loads the case table.</summary>
    public const int MaxCaseOptions = 200;
}

/// <summary>A case offered by the report filters and the generation form.</summary>
public sealed record ReportCaseOptionDto(Guid Id, string CaseNumber);

/// <summary>
/// Quick groupings offered as tabs above the report list. Each maps onto existing
/// <see cref="ReportType"/> values so no second taxonomy is introduced.
/// </summary>
public enum ReportGroup
{
    All = 0,
    TrendAnalysis = 1,
    RiskAssessments = 2,
    Investigations = 3
}

/// <summary>Server-side GET filters for the report list. All values are optional.</summary>
public sealed class ReportFilterCriteria
{
    public string? Search { get; init; }

    public ReportType? Type { get; init; }

    public ReportGroup Group { get; init; } = ReportGroup.All;

    public ReportSource? Source { get; init; }

    public ReportStatus? Status { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = ReportLimits.DefaultPageSize;
}

/// <summary>
/// One report row. <see cref="PeriodLabel"/> and the source/author labels are derived
/// server-side so the list never renders raw enum numbers.
/// </summary>
public sealed class ReportListItemDto
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required ReportType Type { get; init; }

    public required ReportSource Source { get; init; }

    public required ReportStatus Status { get; init; }

    public string? Summary { get; init; }

    public DateTime? PeriodFrom { get; init; }

    public DateTime? PeriodTo { get; init; }

    public required DateTime CreatedAt { get; init; }

    public DateTime? GeneratedAt { get; init; }

    public string? AuthorName { get; init; }

    public string? GeneratedByName { get; init; }

    public string? CaseNumber { get; init; }

    public int SectionCount { get; init; }

    public bool CanEdit { get; init; }

    public string PeriodLabel => PeriodFrom is { } from && PeriodTo is { } to
        ? $"{from:d MMM yyyy} – {to:d MMM yyyy}"
        : "Not period-based";

    /// <summary>Compact card badge: a single month when the period sits in one month.</summary>
    public string PeriodBadge
    {
        get
        {
            if (PeriodFrom is not { } from || PeriodTo is not { } to)
            {
                return "Undated";
            }

            return from.Year == to.Year && from.Month == to.Month
                ? from.ToString("MMM yyyy", CultureInfo.InvariantCulture)
                : $"{from:d MMM} – {to:d MMM yyyy}";
        }
    }
}

public sealed class PagedReportsDto
{
    public IReadOnlyList<ReportListItemDto> Items { get; init; } = Array.Empty<ReportListItemDto>();

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = ReportLimits.DefaultPageSize;

    public int TotalCount { get; init; }

    public int TotalPages => PageSize <= 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Counters for the report list header. Every value is a real database count.</summary>
public sealed class ReportSummaryDto
{
    public int TotalCount { get; init; }

    public int SystemGeneratedCount { get; init; }

    public int AnalystAuthoredCount { get; init; }

    public int AnalyticalCount { get; init; }

    public int ArchivedCount { get; init; }
}

/// <summary>Request to produce a system-generated report from existing NETRA data.</summary>
public sealed class GenerateReportRequest
{
    public required ReportType Type { get; init; }

    public Guid? CaseId { get; init; }

    public Guid? DistrictId { get; init; }

    public Guid? CrimeTypeId { get; init; }

    public string? Severity { get; init; }

    public DateTime? DateFrom { get; init; }

    public DateTime? DateTo { get; init; }

    /// <summary>Optional analyst framing written alongside the immutable system facts.</summary>
    public string? AnalystObservations { get; init; }
}

/// <summary>Request to author a report by hand.</summary>
public sealed class CreateAnalystReportRequest
{
    public required string Title { get; init; }

    public required ReportType Type { get; init; }

    public string? Summary { get; init; }

    public string? Content { get; init; }

    public Guid? CaseId { get; init; }

    public DateTime? PeriodFrom { get; init; }

    public DateTime? PeriodTo { get; init; }
}

/// <summary>Edits to an analyst-authored report. System-generated reports are not editable.</summary>
public sealed class EditAnalystReportRequest
{
    public required string Title { get; init; }

    public string? Summary { get; init; }

    public string? Content { get; init; }

    public DateTime? PeriodFrom { get; init; }

    public DateTime? PeriodTo { get; init; }
}

/// <summary>
/// A report rendered as a document. Facts are recomputed from the stored parameters so a
/// report never shows numbers that no longer match the underlying records.
/// </summary>
public sealed class ReportDocumentDto
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required ReportType Type { get; init; }

    public required ReportSource Source { get; init; }

    public required ReportStatus Status { get; init; }

    public required DateTime CreatedAt { get; init; }

    public DateTime? GeneratedAt { get; init; }

    public DateTime? ArchivedAt { get; init; }

    public DateTime PeriodFrom { get; init; }

    public DateTime PeriodTo { get; init; }

    public string? AuthorName { get; init; }

    public string? GeneratedByName { get; init; }

    /// <summary>Human-readable filter set the report was produced from.</summary>
    public IReadOnlyList<string> AppliedFilters { get; init; } = Array.Empty<string>();

    public string? Summary { get; init; }

    public IReadOnlyList<ReportSectionDto> Sections { get; init; } = Array.Empty<ReportSectionDto>();

    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ReportMetricDto> Metrics { get; init; } = Array.Empty<ReportMetricDto>();

    /// <summary>Analyst-authored narrative; separate from immutable system facts.</summary>
    public string? AnalystContent { get; init; }

    public Guid? CaseId { get; init; }

    public string? CaseNumber { get; init; }

    public IReadOnlyList<LinkedCaseDto> LinkedCases { get; init; } = Array.Empty<LinkedCaseDto>();

    /// <summary>True when the underlying sources held no records for this report.</summary>
    public bool HasData { get; init; }

    /// <summary>Monthly case counts for the reporting period (trend chart input).</summary>
    public IReadOnlyList<ReportTrendPointDto> Trend { get; init; } = Array.Empty<ReportTrendPointDto>();

    public IReadOnlyList<ReportCategoryCountDto> CrimeByType { get; init; } =
        Array.Empty<ReportCategoryCountDto>();

    public IReadOnlyList<ReportCategoryCountDto> SeverityDistribution { get; init; } =
        Array.Empty<ReportCategoryCountDto>();

    public IReadOnlyList<ReportDistrictSeverityDto> CrimeByDistrict { get; init; } =
        Array.Empty<ReportDistrictSeverityDto>();
}

public sealed class ReportSectionDto
{
    public required string Heading { get; init; }

    public IReadOnlyList<string> Rows { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ReportFieldDto> Fields { get; init; } = Array.Empty<ReportFieldDto>();

    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();

    public IReadOnlyList<IReadOnlyList<string>> Table { get; init; } =
        Array.Empty<IReadOnlyList<string>>();

    public bool IsEmpty =>
        Rows.Count == 0 && Fields.Count == 0 && Table.Count == 0;
}

public sealed record ReportFieldDto(string Label, string Value);

public sealed record ReportMetricDto(string Label, string Value, string? Meta = null);

public sealed record ReportTrendPointDto(string Label, int Count);

public sealed record ReportCategoryCountDto(string Label, int Count);

public sealed record ReportDistrictSeverityDto(
    string DistrictName, int Low, int Medium, int High, int Critical)
{
    public int Total => Low + Medium + High + Critical;
}

public sealed record LinkedCaseDto(Guid Id, string CaseNumber, string Title, string Status);

/// <summary>
/// The five report concepts the project defines, mapped onto the pre-existing
/// <see cref="ReportType"/> enum values so no duplicate type is introduced.
/// </summary>
public static class ReportTypes
{
    /// <summary>Types whose content is analytical (risk, statistics, intelligence).</summary>
    public static readonly ReportType[] Analytical =
    [
        ReportType.RiskAssessment,
        ReportType.StatisticalAnalysis,
        ReportType.IntelligenceBrief
    ];

    /// <summary>Types that must be generated, never authored from scratch.</summary>
    public static readonly ReportType[] GeneratedOnly =
    [
        ReportType.CaseSummary,
        ReportType.InvestigationProgress,
        ReportType.RiskAssessment,
        ReportType.StatisticalAnalysis,
        ReportType.IntelligenceBrief
    ];

    /// <summary>Types an analyst may author by hand.</summary>
    public static readonly ReportType[] Authorable =
    [
        ReportType.IntelligenceBrief,
        ReportType.Custom
    ];

    /// <summary>Types that address a single case and therefore require one.</summary>
    public static readonly ReportType[] CaseScoped =
    [
        ReportType.CaseSummary,
        ReportType.InvestigationProgress
    ];

    public static bool IsAnalytical(ReportType type) => Analytical.Contains(type);

    /// <summary>Types covered by a list tab. An empty array means the tab does not restrict.</summary>
    public static ReportType[] TypesIn(ReportGroup group) => group switch
    {
        ReportGroup.TrendAnalysis => [ReportType.StatisticalAnalysis],
        ReportGroup.RiskAssessments => [ReportType.RiskAssessment],
        ReportGroup.Investigations => [ReportType.CaseSummary, ReportType.InvestigationProgress],
        _ => []
    };

    public static string Label(ReportType type) => type switch
    {
        ReportType.CaseSummary => "Case Summary",
        ReportType.InvestigationProgress => "Investigation Summary",
        ReportType.EvidenceInventory => "Evidence Inventory",
        ReportType.IntelligenceBrief => "Executive Intelligence Summary",
        ReportType.RiskAssessment => "Risk Assessment",
        ReportType.StatisticalAnalysis => "Crime Trend Analysis",
        ReportType.AuditTrail => "Audit Trail",
        _ => "Analyst Briefing"
    };

    public static string Description(ReportType type) => type switch
    {
        ReportType.CaseSummary =>
            "Recorded facts for a single case: classification, severity, locations, persons and evidence counts.",
        ReportType.InvestigationProgress =>
            "Investigation view of a case: status, assigned investigator, timeline and authorised notes.",
        ReportType.StatisticalAnalysis =>
            "Crime trend analysis over a period, produced by the existing analytics service.",
        ReportType.RiskAssessment =>
            "Area-level analytical indicators and anomalies, produced by the existing risk service.",
        ReportType.IntelligenceBrief =>
            "Executive summary combining dashboard, analytics, risk and alert readings for a period.",
        ReportType.EvidenceInventory => "Inventory of evidence recorded against a case.",
        ReportType.AuditTrail => "Audit trail extract for a case or period.",
        _ => "Analyst-authored briefing with no system-generated facts."
    };
}

/// <summary>Controlled labels for report source and status.</summary>
public static class ReportLabels
{
    public static string Source(ReportSource source) => source switch
    {
        ReportSource.SystemGenerated => "NETRA System Generated",
        _ => "Analyst Authored"
    };

    public static string Status(ReportStatus status) => status switch
    {
        ReportStatus.Draft => "Draft",
        ReportStatus.Ready => "Ready",
        _ => "Archived"
    };
}
