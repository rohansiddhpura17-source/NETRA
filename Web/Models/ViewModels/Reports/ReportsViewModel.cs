using System.ComponentModel.DataAnnotations;
using System.Globalization;
using NETRA.Application.CaseOperations;
using NETRA.Application.Reports;
using NETRA.Domain.Enums;
using NETRA.Web.Models.Navigation;
using NETRA.Web.Models.Ui;
using NETRA.Web.Services.Charts;

namespace NETRA.Web.Models.ViewModels.Reports;

/// <summary>
/// Server-side GET filters for the report list. Every value arrives through the query
/// string; there is no client-side state. Unknown enum names simply fail to bind and the
/// filter is dropped, so a hand-edited URL can never produce an error page.
/// </summary>
public sealed class ReportsFilterViewModel
{
    public string? Search { get; set; }

    public ReportGroup Group { get; set; } = ReportGroup.All;

    public ReportType? Type { get; set; }

    public ReportSource? Source { get; set; }

    public ReportStatus? Status { get; set; }

    private int _page = 1;

    /// <summary>Never below page 1, whatever the query string claims.</summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public ReportFilterCriteria ToCriteria() => new()
    {
        Search = Search,
        Group = Group,
        Type = Type,
        Source = Source,
        Status = Status,
        Page = Page < 1 ? 1 : Page,
        PageSize = ReportLimits.DefaultPageSize
    };

    /// <summary>Route values that keep the active filters and tab when paging.</summary>
    public Dictionary<string, string?> RouteValues() => new()
    {
        ["search"] = Search,
        ["group"] = Group.ToString(),
        ["type"] = Type?.ToString(),
        ["source"] = Source?.ToString(),
        ["status"] = Status?.ToString(),
        ["page"] = Page.ToString(CultureInfo.InvariantCulture)
    };

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Search) || Type != null || Source != null ||
        Status != null || Group != ReportGroup.All;

    public static IReadOnlyList<(ReportGroup Value, string Label)> GroupOptions { get; } =
        new List<(ReportGroup, string)>
        {
            (ReportGroup.All, "All reports"),
            (ReportGroup.TrendAnalysis, "Trend analysis"),
            (ReportGroup.RiskAssessments, "Risk assessments"),
            (ReportGroup.Investigations, "Investigations")
        };

    /// <summary>Type dropdown. Analytical entries are removed for readers without analytical access.</summary>
    public static IReadOnlyList<(ReportType Value, string Label)> TypeOptions(bool allowAnalytical) =>
        Enum.GetValues<ReportType>()
            .Where(t => allowAnalytical || !ReportTypes.IsAnalytical(t))
            .Select(t => (t, ReportTypes.Label(t)))
            .ToList();

    public static IReadOnlyList<(ReportSource Value, string Label)> SourceOptions { get; } =
        new List<(ReportSource, string)>
        {
            (ReportSource.SystemGenerated, "NETRA System Generated"),
            (ReportSource.AnalystAuthored, "Analyst Authored")
        };

    public static IReadOnlyList<(ReportStatus Value, string Label)> StatusOptions { get; } =
        Enum.GetValues<ReportStatus>().Select(s => (s, ReportLabels.Status(s))).ToList();
}

/// <summary>Tone mapping for report source and status, shared by list and document.</summary>
public static class ReportTones
{
    public static NetraTone Source(ReportSource source) => source switch
    {
        ReportSource.SystemGenerated => NetraTone.Primary,
        _ => NetraTone.Info
    };

    public static NetraTone Status(ReportStatus status) => status switch
    {
        ReportStatus.Draft => NetraTone.Warning,
        ReportStatus.Ready => NetraTone.Success,
        _ => NetraTone.Neutral
    };

    /// <summary>Card accent: the report subject decides the colour, as in the page 10 design.</summary>
    public static NetraTone Type(ReportType type) => type switch
    {
        ReportType.RiskAssessment => NetraTone.Danger,
        ReportType.StatisticalAnalysis => NetraTone.Primary,
        ReportType.IntelligenceBrief => NetraTone.Info,
        ReportType.InvestigationProgress => NetraTone.Warning,
        ReportType.CaseSummary => NetraTone.Success,
        _ => NetraTone.Neutral
    };
}

/// <summary>One rendered report card.</summary>
public sealed class ReportCardItem
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required string TypeLabel { get; init; }

    public required string SourceLabel { get; init; }

    public required string StatusLabel { get; init; }

    public required NetraTone Accent { get; init; }

    public required NetraTone SourceTone { get; init; }

    public required NetraTone StatusTone { get; init; }

    public string? Summary { get; init; }

    public required string PeriodBadge { get; init; }

    public required string CreatedAtText { get; init; }

    public required string AuthorLabel { get; init; }

    public string? CaseNumber { get; init; }

    public required string SectionLabel { get; init; }

    public required bool CanEdit { get; init; }

    public static ReportCardItem From(ReportListItemDto dto) => new()
    {
        Id = dto.Id,
        Title = dto.Title,
        TypeLabel = ReportTypes.Label(dto.Type),
        SourceLabel = ReportLabels.Source(dto.Source),
        StatusLabel = ReportLabels.Status(dto.Status),
        Accent = ReportTones.Type(dto.Type),
        SourceTone = ReportTones.Source(dto.Source),
        StatusTone = ReportTones.Status(dto.Status),
        Summary = dto.Summary,
        PeriodBadge = dto.PeriodBadge,
        CreatedAtText = dto.CreatedAt.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
        AuthorLabel = string.IsNullOrWhiteSpace(dto.AuthorName) ? "Not recorded" : dto.AuthorName,
        CaseNumber = dto.CaseNumber,
        SectionLabel = $"{dto.SectionCount} section{(dto.SectionCount == 1 ? "" : "s")}",
        CanEdit = dto.CanEdit
    };
}

public sealed class ReportsIndexViewModel
{
    public required IReadOnlyList<ReportCardItem> Reports { get; init; }

    public required ReportSummaryDto Summary { get; init; }

    public required ReportsFilterViewModel Filter { get; init; }

    public required IReadOnlyList<StatCardModel> SummaryCards { get; init; }

    public required PaginationModel Pagination { get; init; }

    public required IReadOnlyList<ReportCaseOptionDto> CaseOptions { get; init; }

    /// <summary>May generate, author and archive reports.</summary>
    public required bool CanManage { get; init; }

    /// <summary>May open analytical reports; drives tabs, badges and the type dropdown.</summary>
    public required bool CanReadAnalytical { get; init; }

    public int TotalCount { get; init; }

    public bool HasActiveFilters => Filter.HasActiveFilters;

    public IReadOnlyList<(ReportGroup Value, string Label)> Tabs =>
        ReportsFilterViewModel.GroupOptions
            .Where(tab => tab.Value != ReportGroup.RiskAssessments || CanReadAnalytical)
            .ToList();

    public IReadOnlyList<(ReportType Value, string Label)> TypeOptions =>
        ReportsFilterViewModel.TypeOptions(CanReadAnalytical);

    public static ReportsIndexViewModel Create(
        PagedReportsDto paged,
        ReportSummaryDto summary,
        ReportsFilterViewModel filter,
        IReadOnlyList<ReportCaseOptionDto> caseOptions,
        bool canManage,
        bool canReadAnalytical) => new()
        {
            Reports = paged.Items.Select(ReportCardItem.From).ToList(),
            Summary = summary,
            Filter = filter,
            CaseOptions = caseOptions,
            CanManage = canManage,
            CanReadAnalytical = canReadAnalytical,
            TotalCount = paged.TotalCount,
            SummaryCards = new List<StatCardModel>
            {
                new()
                {
                    Label = "Total reports",
                    Value = summary.TotalCount.ToString(CultureInfo.InvariantCulture),
                    Meta = "Stored in NETRA",
                    Tone = NetraTone.Primary,
                    Icon = NetraIcon.Report,
                    ShowIcon = true
                },
                new()
                {
                    Label = "System generated",
                    Value = summary.SystemGeneratedCount.ToString(CultureInfo.InvariantCulture),
                    Meta = "Immutable factual sections",
                    Tone = NetraTone.Info,
                    Icon = NetraIcon.Shield,
                    ShowIcon = true
                },
                new()
                {
                    Label = "Analyst authored",
                    Value = summary.AnalystAuthoredCount.ToString(CultureInfo.InvariantCulture),
                    Meta = "Editable briefings",
                    Tone = NetraTone.Success,
                    Icon = NetraIcon.Assistant,
                    ShowIcon = true
                },
                new()
                {
                    Label = canReadAnalytical ? "Analytical" : "Archived",
                    Value = (canReadAnalytical ? summary.AnalyticalCount : summary.ArchivedCount)
                        .ToString(CultureInfo.InvariantCulture),
                    Meta = canReadAnalytical ? "Risk, trend and intelligence" : "Out of the working set",
                    Tone = canReadAnalytical ? NetraTone.Warning : NetraTone.Neutral,
                    Icon = NetraIcon.Analytics,
                    ShowIcon = true
                }
            },
            Pagination = new PaginationModel
            {
                Page = paged.Page,
                TotalPages = Math.Max(1, paged.TotalPages),
                TotalItems = paged.TotalCount,
                PageSize = paged.PageSize,
                Controller = "Reports",
                Action = "Index",
                RouteValues = filter.RouteValues()
            }
        };
}

/// <summary>A report rendered as a document. Charts are built here, never in the view.</summary>
public sealed class ReportDocumentViewModel
{
    public required ReportDocumentDto Document { get; init; }

    public required bool CanManage { get; init; }

    public required IReadOnlyList<StatCardModel> MetricCards { get; init; }

    public SvgChartModel? TrendChart { get; init; }

    public SvgChartModel? TypeChart { get; init; }

    public SvgChartModel? SeverityChart { get; init; }

    public SvgChartModel? DistrictChart { get; init; }

    public string PeriodLabel =>
        $"{Document.PeriodFrom:d MMM yyyy} – {Document.PeriodTo:d MMM yyyy}";

    public string TypeLabel => ReportTypes.Label(Document.Type);

    public string SourceLabel => ReportLabels.Source(Document.Source);

    public string StatusLabel => ReportLabels.Status(Document.Status);

    public NetraTone SourceTone => ReportTones.Source(Document.Source);

    public NetraTone StatusTone => ReportTones.Status(Document.Status);

    public NetraTone Accent => ReportTones.Type(Document.Type);

    public string CreatedAtText => Document.CreatedAt.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture);

    public string? GeneratedAtText => Text(Document.GeneratedAt);

    public string? ArchivedAtText => Text(Document.ArchivedAt);

    /// <summary>Attribution line: system reports are attributed to NETRA, authored ones to the person.</summary>
    public string AuthorLine => Document.Source == ReportSource.SystemGenerated
        ? "NETRA System Generated"
        : string.IsNullOrWhiteSpace(Document.AuthorName)
            ? "Analyst authored (author not recorded)"
            : $"Analyst authored by {Document.AuthorName}";

    public string? RequestedByText => Document.GeneratedByName;

    public IReadOnlyList<ReportSectionDto> Sections =>
        Document.Sections.Where(section => !section.IsEmpty).ToList();

    public IReadOnlyList<string> AnalystParagraphs =>
        string.IsNullOrWhiteSpace(Document.AnalystContent)
            ? Array.Empty<string>()
            : Document.AnalystContent
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

    public bool HasCharts => TrendChart is not null || TypeChart is not null ||
        SeverityChart is not null || DistrictChart is not null;

    public static ReportDocumentViewModel Create(
        ReportDocumentDto document, bool canManage) => new()
        {
            Document = document,
            CanManage = canManage,
            MetricCards = document.Metrics.Select(metric => new StatCardModel
            {
                Label = metric.Label,
                Value = metric.Value,
                Meta = metric.Meta,
                Tone = NetraTone.Primary,
                Icon = NetraIcon.Report,
                ShowIcon = false
            }).ToList(),
            TrendChart = document.Trend.Count > 1 && document.Trend.Any(t => t.Count > 0)
                ? new SvgChartModel
                {
                    Title = "Recorded case volume by month",
                    Description = "Line chart of monthly case counts in the reporting period: " +
                        string.Join(", ", document.Trend.Select(t => $"{t.Label} {t.Count}")) + ".",
                    Categories = document.Trend.Select(t => t.Label).ToList(),
                    Series =
                    [
                        new SvgChartSeries
                        {
                            Name = "Cases",
                            Values = document.Trend.Select(t => (decimal)t.Count).ToList()
                        }
                    ]
                }
                : null,
            TypeChart = document.CrimeByType.Count > 0 && document.CrimeByType.Any(c => c.Count > 0)
                ? new SvgChartModel
                {
                    Title = "Cases by crime type",
                    Description = "Donut chart of cases by crime type: " +
                        string.Join(", ", document.CrimeByType.Select(c => $"{c.Label} {c.Count}")) + ".",
                    Categories = document.CrimeByType.Select(c => c.Label).ToList(),
                    Series =
                    [
                        new SvgChartSeries
                        {
                            Name = "Cases",
                            Values = document.CrimeByType.Select(c => (decimal)c.Count).ToList()
                        }
                    ]
                }
                : null,
            SeverityChart = document.SeverityDistribution.Count > 0 &&
                document.SeverityDistribution.Any(s => s.Count > 0)
                ? new SvgChartModel
                {
                    Title = "Severity distribution",
                    Description = "Bar chart of cases by recorded severity: " +
                        string.Join(", ", document.SeverityDistribution.Select(s => $"{s.Label} {s.Count}")) + ".",
                    Categories = document.SeverityDistribution.Select(s => s.Label).ToList(),
                    Series =
                    [
                        new SvgChartSeries
                        {
                            Name = "Cases",
                            Values = document.SeverityDistribution.Select(s => (decimal)s.Count).ToList()
                        }
                    ]
                }
                : null,
            DistrictChart = document.CrimeByDistrict.Count > 0 && document.CrimeByDistrict.Any(d => d.Total > 0)
                ? new SvgChartModel
                {
                    Title = "Severity by district",
                    Description = "Horizontal stacked bar chart of cases per district split by severity: " +
                        string.Join("; ", document.CrimeByDistrict.Select(d => $"{d.DistrictName} {d.Total}")) + ".",
                    Categories = document.CrimeByDistrict.Select(d => d.DistrictName).ToList(),
                    Series =
                    [
                        new SvgChartSeries { Name = "Low", Values = document.CrimeByDistrict.Select(d => (decimal)d.Low).ToList() },
                        new SvgChartSeries { Name = "Medium", Values = document.CrimeByDistrict.Select(d => (decimal)d.Medium).ToList() },
                        new SvgChartSeries { Name = "High", Values = document.CrimeByDistrict.Select(d => (decimal)d.High).ToList() },
                        new SvgChartSeries { Name = "Critical", Values = document.CrimeByDistrict.Select(d => (decimal)d.Critical).ToList() }
                    ]
                }
                : null
        };

    private static string? Text(DateTime? value) => value is null
        ? null
        : value.Value.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>
/// The generation form. Analytical types are offered only to readers authorised for
/// analytical content, so an unauthorised user cannot submit what they cannot open.
/// </summary>
public sealed class GenerateReportViewModel
{
    [Required(ErrorMessage = "Select the report type to generate.")]
    public ReportType Type { get; set; } = ReportType.StatisticalAnalysis;

    public Guid? CaseId { get; set; }

    public Guid? DistrictId { get; set; }

    public Guid? CrimeTypeId { get; set; }

    public string? Severity { get; set; }

    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    [StringLength(ReportLimits.MaxContentLength, ErrorMessage = "Observations are limited to 8000 characters.")]
    public string? AnalystObservations { get; set; }

    // Dropdown contents, prepared by the controller and never bound from the form.
    public IReadOnlyList<(ReportType Value, string Label, string Description)> TypeOptions { get; set; } =
        Array.Empty<(ReportType, string, string)>();

    public IReadOnlyList<ReportCaseOptionDto> CaseOptions { get; set; } =
        Array.Empty<ReportCaseOptionDto>();

    public IReadOnlyList<LookupItem> Districts { get; set; } = Array.Empty<LookupItem>();

    public IReadOnlyList<LookupItem> CrimeTypes { get; set; } = Array.Empty<LookupItem>();

    /// <summary>Period used when the form leaves the dates empty.</summary>
    public DateTime DefaultDateFrom { get; set; }

    public DateTime DefaultDateTo { get; set; }

    public static IReadOnlyList<string> SeverityOptions { get; } = ["Low", "Medium", "High", "Critical"];

    /// <summary>Case-scoped reports address one case, so the picker becomes mandatory.</summary>
    public bool RequiresCase => ReportTypes.CaseScoped.Contains(Type);

    public string PeriodLabel =>
        $"{(DateFrom ?? DefaultDateFrom):d MMM yyyy} – {(DateTo ?? DefaultDateTo):d MMM yyyy}";

    public string SelectedDescription =>
        TypeOptions.FirstOrDefault(option => option.Value == Type).Description
        ?? ReportTypes.Description(Type);
}

/// <summary>Create/edit form for analyst-authored reports.</summary>
public sealed class AnalystReportFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "A report title is required.")]
    [StringLength(ReportLimits.MaxTitleLength, MinimumLength = ReportLimits.MinTitleLength,
        ErrorMessage = "Title must be between 6 and 200 characters.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select the report type.")]
    public ReportType Type { get; set; } = ReportType.Custom;

    public Guid? CaseId { get; set; }

    public DateTime? PeriodFrom { get; set; }

    public DateTime? PeriodTo { get; set; }

    [StringLength(ReportLimits.MaxSummaryLength, ErrorMessage = "Summary is limited to 2000 characters.")]
    public string? Summary { get; set; }

    [StringLength(ReportLimits.MaxContentLength, ErrorMessage = "Observations are limited to 8000 characters.")]
    public string? Content { get; set; }

    public bool IsEdit => Id is not null;

    public string PageTitle => IsEdit ? "Edit analyst report" : "New analyst report";

    public static IReadOnlyList<(ReportType Value, string Label, string Description)> TypeOptions(
        bool allowAnalytical) =>
        ReportTypes.Authorable
            .Where(t => allowAnalytical || !ReportTypes.IsAnalytical(t))
            .Select(t => (t, ReportTypes.Label(t), ReportTypes.Description(t)))
            .ToList();

    public IReadOnlyList<ReportCaseOptionDto> CaseOptions { get; set; } = Array.Empty<ReportCaseOptionDto>();

    /// <summary>An authored report keeps its type after creation, so the selector is locked when editing.</summary>
    public bool TypeIsLocked => IsEdit;

    public static AnalystReportFormViewModel Create(
        IReadOnlyList<ReportCaseOptionDto> caseOptions, ReportType? type = null) => new()
        {
            CaseOptions = caseOptions,
            Type = type is { } value && ReportTypes.Authorable.Contains(value) ? value : ReportType.Custom
        };

    public static AnalystReportFormViewModel FromDocument(
        ReportDocumentDto document, IReadOnlyList<ReportCaseOptionDto> caseOptions) => new()
        {
            Id = document.Id,
            Title = document.Title,
            Type = document.Type,
            CaseId = document.CaseId,
            PeriodFrom = document.PeriodFrom,
            PeriodTo = document.PeriodTo,
            Summary = document.Summary,
            Content = document.AnalystContent,
            CaseOptions = caseOptions
        };
}
