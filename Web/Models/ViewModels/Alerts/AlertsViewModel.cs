using System.Globalization;
using NETRA.Application.Alerts;
using NETRA.Domain.Enums;
using NETRA.Web.Models.Navigation;
using NETRA.Web.Models.Ui;

namespace NETRA.Web.Models.ViewModels.Alerts;

/// <summary>
/// Server-side GET filter for the alert list. Enum names bind case-insensitively
/// ("severity=High"); an invalid value fails model binding and the filter is
/// simply dropped — the request still renders.
/// </summary>
public sealed class AlertsFilterViewModel
{
    public AlertSeverity? Severity { get; set; }

    public AlertStatus? Status { get; set; }

    public AlertCategory? Category { get; set; }

    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    public Guid? CaseId { get; set; }

    private int _page = 1;

    /// <summary>Never below page 1, whatever the query string claims.</summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public AlertFilterCriteria ToCriteria() => new()
    {
        Severity = Severity,
        Status = Status,
        Category = Category,
        DateFrom = DateFrom,
        DateTo = DateTo,
        CaseId = CaseId,
        Page = Page < 1 ? 1 : Page,
        PageSize = AlertLimits.DefaultPageSize
    };

    /// <summary>Route values that keep the active filters when paging.</summary>
    public Dictionary<string, string?> RouteValues() => new()
    {
        ["severity"] = Severity?.ToString(),
        ["status"] = Status?.ToString(),
        ["category"] = Category?.ToString(),
        ["dateFrom"] = DateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["dateTo"] = DateTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["caseId"] = CaseId?.ToString(),
        ["page"] = Page.ToString(CultureInfo.InvariantCulture)
    };

    public static IReadOnlyList<string> SeverityOptions { get; } =
        new List<string> { "Info", "Low", "Medium", "High", "Critical" };

    public static IReadOnlyList<string> StatusOptions { get; } =
        new List<string> { "New", "Acknowledged", "InProgress", "Resolved", "Dismissed", "Escalated" };

    public static IReadOnlyList<(AlertCategory Value, string Label)> CategoryOptions { get; } =
        new List<(AlertCategory, string)>
        {
            (AlertCategory.CrimeSpike, "Crime Spike"),
            (AlertCategory.UnusualPattern, "Unusual Pattern"),
            (AlertCategory.HighRiskArea, "High-Risk Area"),
            (AlertCategory.RelatedCases, "Related Cases"),
            (AlertCategory.SecuritySystem, "Security / System")
        };
}

/// <summary>One rendered alert card. Display strings are prepared in the view model.</summary>
public sealed class AlertDisplayItem
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public required string SeverityLabel { get; init; }

    public required NetraTone SeverityTone { get; init; }

    public required string StatusLabel { get; init; }

    public required NetraTone StatusTone { get; init; }

    public required string CategoryLabel { get; init; }

    public required string CreatedAtText { get; init; }

    public string? CaseNumber { get; init; }

    public Guid? CrimeCaseId { get; init; }

    public string? RelatedEntityLabel { get; init; }

    public bool IsGenerated { get; init; }

    /// <summary>"Case CR-2026-001 · Central District" style meta line.</summary>
    public string MetaLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(CaseNumber))
            {
                parts.Add($"Case {CaseNumber}");
            }

            if (!string.IsNullOrWhiteSpace(RelatedEntityLabel))
            {
                parts.Add(RelatedEntityLabel);
            }

            if (parts.Count == 0)
            {
                parts.Add(IsGenerated ? "Analytically generated" : "Manually raised");
            }

            return string.Join(" · ", parts);
        }
    }

    public static AlertDisplayItem From(AlertListItemDto dto) => new()
    {
        Id = dto.Id,
        Title = dto.Title,
        Description = dto.Description,
        SeverityLabel = NetraDisplay.Words(dto.Severity.ToString()),
        SeverityTone = AlertTones.Severity(dto.Severity),
        StatusLabel = NetraDisplay.Words(dto.Status.ToString()),
        StatusTone = AlertTones.Status(dto.Status),
        CategoryLabel = AlertCategories.Label(dto.Category),
        CreatedAtText = dto.CreatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture),
        CaseNumber = dto.CaseNumber,
        CrimeCaseId = dto.CrimeCaseId,
        RelatedEntityLabel = dto.RelatedEntityLabel,
        IsGenerated = dto.IsGenerated
    };
}

/// <summary>Tone mapping for alert severity and status, shared by list and details.</summary>
public static class AlertTones
{
    public static NetraTone Severity(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => NetraTone.Danger,
        AlertSeverity.High => NetraTone.Warning,
        AlertSeverity.Medium => NetraTone.Info,
        AlertSeverity.Low => NetraTone.Success,
        _ => NetraTone.Neutral
    };

    public static NetraTone Status(AlertStatus status) => status switch
    {
        AlertStatus.New => NetraTone.Warning,
        AlertStatus.Acknowledged => NetraTone.Info,
        AlertStatus.InProgress => NetraTone.Primary,
        AlertStatus.Resolved => NetraTone.Success,
        AlertStatus.Dismissed => NetraTone.Neutral,
        AlertStatus.Escalated => NetraTone.Danger,
        _ => NetraTone.Neutral
    };
}

public sealed class AlertsIndexViewModel
{
    public required IReadOnlyList<AlertDisplayItem> Alerts { get; init; }

    public required AlertSummaryDto Summary { get; init; }

    public required AlertsFilterViewModel Filter { get; init; }

    public required IReadOnlyList<StatCardModel> SummaryCards { get; init; }

    public required PaginationModel Pagination { get; init; }

    public required IReadOnlyList<CaseOptionDto> CaseOptions { get; init; }

    /// <summary>Whether the signed-in user may run generation and triage.</summary>
    public required bool CanManage { get; init; }

    public int TotalCount { get; init; }

    public bool HasActiveFilters =>
        Filter.Severity != null || Filter.Status != null || Filter.Category != null ||
        Filter.DateFrom != null || Filter.DateTo != null || Filter.CaseId != null;

    public static AlertsIndexViewModel Create(
        PagedAlertsDto paged,
        AlertSummaryDto summary,
        AlertsFilterViewModel filter,
        IReadOnlyList<CaseOptionDto> caseOptions,
        bool canManage) => new()
        {
            Alerts = paged.Items.Select(AlertDisplayItem.From).ToList(),
            Summary = summary,
            Filter = filter,
            CaseOptions = caseOptions,
            CanManage = canManage,
            TotalCount = paged.TotalCount,
            SummaryCards = new List<StatCardModel>
            {
                new()
                {
                    Label = "Critical",
                    Value = summary.CriticalCount.ToString(CultureInfo.InvariantCulture),
                    Meta = "Active critical alerts",
                    Tone = NetraTone.Danger,
                    Icon = NetraIcon.Alert,
                    ShowIcon = true
                },
                new()
                {
                    Label = "High",
                    Value = summary.HighCount.ToString(CultureInfo.InvariantCulture),
                    Meta = "Active high alerts",
                    Tone = NetraTone.Warning,
                    Icon = NetraIcon.Alert,
                    ShowIcon = true
                },
                new()
                {
                    Label = "Resolved",
                    Value = Math.Max(0, summary.TotalCount - summary.OpenCount)
                        .ToString(CultureInfo.InvariantCulture),
                    Meta = "Closed or dismissed",
                    Tone = NetraTone.Success,
                    Icon = NetraIcon.Shield,
                    ShowIcon = true
                },
                new()
                {
                    Label = "Total",
                    Value = summary.TotalCount.ToString(CultureInfo.InvariantCulture),
                    Meta = $"Open: {summary.OpenCount}",
                    Tone = NetraTone.Primary,
                    Icon = NetraIcon.Bell,
                    ShowIcon = true
                }
            },
            Pagination = new PaginationModel
            {
                Page = paged.Page,
                TotalPages = Math.Max(1, paged.TotalPages),
                TotalItems = paged.TotalCount,
                PageSize = paged.PageSize,
                Controller = "Alerts",
                Action = "Index",
                RouteValues = filter.RouteValues()
            }
        };
}

public sealed class AlertDetailsViewModel
{
    public required AlertDetailDto Alert { get; init; }

    public required bool CanManage { get; init; }

    public string SeverityLabel => NetraDisplay.Words(Alert.Severity.ToString());

    public NetraTone SeverityTone => AlertTones.Severity(Alert.Severity);

    public string StatusLabel => NetraDisplay.Words(Alert.Status.ToString());

    public NetraTone StatusTone => AlertTones.Status(Alert.Status);

    public string CategoryLabel => AlertCategories.Label(Alert.Category);

    public string CreatedAtText => Alert.CreatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);

    public string? AcknowledgedAtText => Format(Alert.AcknowledgedAt);

    public string? ResolvedAtText => Format(Alert.ResolvedAt);

    public bool IsOpen => Alert.Status != AlertStatus.Resolved && Alert.Status != AlertStatus.Dismissed;

    public string? WindowLabel => Alert.WindowStart is { } start && Alert.WindowEnd is { } end
        ? $"{start:dd MMM yyyy} – {end:dd MMM yyyy}"
        : null;

    public static AlertDetailsViewModel Create(AlertDetailDto alert, bool canManage) =>
        new() { Alert = alert, CanManage = canManage };

    private static string? Format(DateTime? value) => value is null
        ? null
        : value.Value.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);
}
