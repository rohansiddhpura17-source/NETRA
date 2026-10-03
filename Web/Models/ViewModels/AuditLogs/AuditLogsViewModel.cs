using System.Globalization;
using NETRA.Application.Abstractions;
using NETRA.Application.Audit;
using NETRA.Web.Models.Ui;

namespace NETRA.Web.Models.ViewModels.AuditLogs;

/// <summary>
/// Server-side GET filter for the audit trail. Every field maps to a real
/// <c>AuditLog</c> column; the trail has no severity or workflow state, so neither is
/// offered as a filter.
/// </summary>
public sealed class AuditLogFilterViewModel
{
    public Guid? ActorUserId { get; set; }

    public string? EntityType { get; set; }

    public string? Action { get; set; }

    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    private int _page = 1;

    /// <summary>Never below page 1, whatever the query string claims.</summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public AuditLogFilterCriteria ToCriteria() => new()
    {
        ActorUserId = ActorUserId,
        EntityType = string.IsNullOrWhiteSpace(EntityType) ? null : EntityType,
        Action = string.IsNullOrWhiteSpace(Action) ? null : Action,
        DateFrom = DateFrom,
        DateTo = DateTo,
        Page = Page,
        PageSize = AuditLogLimits.DefaultPageSize
    };

    /// <summary>Route values that keep the active filters when paging.</summary>
    public Dictionary<string, string?> RouteValues() => new()
    {
        ["actorUserId"] = ActorUserId?.ToString(),
        ["entityType"] = EntityType,
        ["action"] = Action,
        ["dateFrom"] = DateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["dateTo"] = DateTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["page"] = Page.ToString(CultureInfo.InvariantCulture)
    };

    public bool HasActiveFilters =>
        ActorUserId is not null
        || !string.IsNullOrWhiteSpace(EntityType)
        || !string.IsNullOrWhiteSpace(Action)
        || DateFrom is not null
        || DateTo is not null;
}

/// <summary>One audit row as listed. Payload text is shown as a bounded excerpt.</summary>
public sealed class AuditLogRowViewModel
{
    /// <summary>List view never prints a whole payload: the detail page carries it.</summary>
    public const int SummaryLength = 140;

    public required AuditLogListItemDto Entry { get; init; }

    public string TimestampText => Entry.Timestamp.ToString(
        "dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    public string ActorLabel => string.IsNullOrWhiteSpace(Entry.ActorName)
        ? Entry.ActorUserName ?? "Deleted account"
        : Entry.ActorName!;

    public string ActionLabel => NetraDisplay.Words(Entry.Action);

    public string EntityLabel => NetraDisplay.Words(Entry.EntityType);

    public string EntityIdText => Entry.EntityId.ToString("N")[..12];

    public string Summary => Summarize(Entry);

    private static string Summarize(AuditLogListItemDto entry)
    {
        var raw = entry.PropertyName is { Length: > 0 } property
            ? ComposeChange(property, entry.OldValues, entry.NewValues)
            : entry.NewValues ?? entry.OldValues ?? string.Empty;

        return Excerpt(raw, SummaryLength);
    }

    internal static string ComposeChange(string property, string? oldValue, string? newValue)
    {
        if (string.IsNullOrEmpty(newValue) && string.IsNullOrEmpty(oldValue))
        {
            return property;
        }

        if (string.IsNullOrEmpty(oldValue))
        {
            return $"{property} set to {newValue}";
        }

        return string.IsNullOrEmpty(newValue)
            ? $"{property} cleared (was {oldValue})"
            : $"{property}: {oldValue} to {newValue}";
    }

    internal static string Excerpt(string value, int length)
    {
        var single = value.ReplaceLineEndings(" ").Trim();
        return single.Length <= length ? single : single[..length].TrimEnd() + "…";
    }
}

public sealed class AuditLogsIndexViewModel
{
    public required IReadOnlyList<AuditLogRowViewModel> Entries { get; init; }

    public required AuditLogFilterViewModel Filter { get; init; }

    public required AuditLogFilterOptionsDto Options { get; init; }

    public required int TotalCount { get; init; }

    public required PaginationModel Pagination { get; init; }

    public bool HasActiveFilters => Filter.HasActiveFilters;

    public static AuditLogsIndexViewModel Create(
        PagedResult<AuditLogListItemDto> paged,
        AuditLogFilterViewModel filter,
        AuditLogFilterOptionsDto options) => new()
    {
        Entries = paged.Items.Select(item => new AuditLogRowViewModel { Entry = item }).ToList(),
        Filter = filter,
        Options = options,
        TotalCount = paged.TotalCount,
        Pagination = new PaginationModel
        {
            Page = paged.Page,
            TotalPages = Math.Max(1, paged.TotalPages),
            TotalItems = paged.TotalCount,
            PageSize = paged.PageSize,
            Controller = "AuditLogs",
            Action = "Index",
            RouteValues = filter.RouteValues()
        }
    };
}

/// <summary>
/// The complete audit record. Payloads stay plain strings so Razor escapes them: an
/// audit entry that contains markup is displayed as text, never interpreted.
/// </summary>
public sealed class AuditLogDetailsViewModel
{
    public const int PayloadLength = 2000;

    public required AuditLogDetailDto Entry { get; init; }

    public string TimestampText => Entry.Timestamp.ToString(
        "dd MMM yyyy HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    public string ActorLabel => string.IsNullOrWhiteSpace(Entry.ActorName)
        ? Entry.ActorUserName ?? "Deleted account"
        : Entry.ActorName!;

    public string ActorUserName => Entry.ActorUserName ?? "—";

    public string ActionLabel => NetraDisplay.Words(Entry.Action);

    public string EntityLabel => NetraDisplay.Words(Entry.EntityType);

    public string PropertyOrDash => OrDash(Entry.PropertyName);

    public string Summary => AuditLogRowViewModel.Excerpt(
        Entry.PropertyName is { Length: > 0 } property
            ? AuditLogRowViewModel.ComposeChange(property, Entry.OldValues, Entry.NewValues)
            : Entry.NewValues ?? Entry.OldValues ?? string.Empty,
        AuditLogRowViewModel.SummaryLength);

    public string OldValuesText => Payload(Entry.OldValues);

    public string NewValuesText => Payload(Entry.NewValues);

    public string IpOrDash => OrDash(Entry.IpAddress);

    public string CorrelationOrDash => OrDash(Entry.CorrelationId);

    public string SessionOrDash => OrDash(Entry.SessionId);

    public string UserAgentText => Payload(Entry.UserAgent);

    private static string Payload(string? value) => string.IsNullOrWhiteSpace(value)
        ? "(nothing recorded)"
        : AuditLogRowViewModel.Excerpt(value!, PayloadLength);

    private static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value!;
}
