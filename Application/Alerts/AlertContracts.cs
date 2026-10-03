using NETRA.Domain.Enums;

namespace NETRA.Application.Alerts;

/// <summary>
/// Application-layer contracts for the Alerts &amp; Notifications module.
/// DTOs are read-only projections; controllers and views never touch entities.
/// </summary>
public static class AlertLimits
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int DefaultGenerationWindowDays = 30;
    public const int MinGenerationWindowDays = 7;
    public const int MaxGenerationWindowDays = 365;
    public const int MaxGeneratedPerRun = 25;
    public const int MaxNotificationPageSize = 50;
}

/// <summary>Server-side GET filters for the alert list. All values are optional.</summary>
public sealed class AlertFilterCriteria
{
    public AlertSeverity? Severity { get; init; }
    public AlertStatus? Status { get; init; }
    public AlertCategory? Category { get; init; }
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
    public Guid? CaseId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = AlertLimits.DefaultPageSize;
}

public sealed class AlertListItemDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required AlertSeverity Severity { get; init; }
    public required AlertStatus Status { get; init; }
    public required AlertCategory Category { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public Guid? CrimeCaseId { get; init; }
    public string? CaseNumber { get; init; }
    public string? RelatedEntityLabel { get; init; }
    public bool IsGenerated { get; init; }
}

public sealed class AlertDetailDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required AlertSeverity Severity { get; init; }
    public required AlertStatus Status { get; init; }
    public required AlertCategory Category { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public string? ResolvedByName { get; init; }
    public string? ResolutionNotes { get; init; }
    public Guid? CrimeCaseId { get; init; }
    public string? CaseNumber { get; init; }
    public string? CaseTitle { get; init; }
    public string? RelatedEntityLabel { get; init; }
    public DateTime? WindowStart { get; init; }
    public DateTime? WindowEnd { get; init; }
    public bool IsGenerated { get; init; }
}

/// <summary>Case id + number for the related-case filter dropdown.</summary>
public sealed record CaseOptionDto(Guid Id, string CaseNumber);

/// <summary>Database-side counts for the summary cards. Never faked.</summary>
public sealed class AlertSummaryDto
{
    public required int CriticalCount { get; init; }
    public required int HighCount { get; init; }
    public required int OpenCount { get; init; }
    public required int TotalCount { get; init; }
}

public sealed class AlertGenerationResultDto
{
    public required int CreatedCount { get; init; }
    public required int SkippedDuplicateCount { get; init; }
    public required int WindowDays { get; init; }
    public required DateTime WindowStart { get; init; }
    public required DateTime WindowEnd { get; init; }
}

public sealed class PagedAlertsDto
{
    public required IReadOnlyList<AlertListItemDto> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed class NotificationDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Message { get; init; }
    public required NotificationCategory Category { get; init; }
    public required bool IsRead { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? ReadAt { get; init; }
    public Guid? AlertId { get; init; }
    public string? AlertTitle { get; init; }
    public AlertSeverity? AlertSeverity { get; init; }
}

public sealed class PagedNotificationsDto
{
    public required IReadOnlyList<NotificationDto> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public required int UnreadCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Stable deduplication keys for analytically generated alerts.</summary>
public static class AlertKeys
{
    /// <summary>
    /// Category + source type + source id + analysis window. The same analytical
    /// event processed twice for the same window always produces the same key.
    /// </summary>
    public static string Build(AlertCategory category, string sourceType, Guid sourceId,
        DateTime windowStart, DateTime windowEnd) =>
        $"{(int)category}:{sourceType}:{sourceId:N}:{windowStart:yyyyMMdd}-{windowEnd:yyyyMMdd}";

    public static string Build(AlertCategory category, string sourceType, string sourceKey,
        DateTime windowStart, DateTime windowEnd) =>
        $"{(int)category}:{sourceType}:{sourceKey}:{windowStart:yyyyMMdd}-{windowEnd:yyyyMMdd}";
}

/// <summary>Display labels for the controlled category model.</summary>
public static class AlertCategories
{
    public static string Label(AlertCategory category) => category switch
    {
        AlertCategory.CrimeSpike => "Crime Spike",
        AlertCategory.UnusualPattern => "Unusual Pattern",
        AlertCategory.HighRiskArea => "High-Risk Area",
        AlertCategory.RelatedCases => "Related Cases",
        AlertCategory.SecuritySystem => "Security / System",
        _ => "Alert"
    };

    public static bool TryParse(string? value, out AlertCategory category) =>
        Enum.TryParse(value, ignoreCase: true, out category) && Enum.IsDefined(category);
}

public static class NotificationCategories
{
    public static string Label(NotificationCategory category) => category switch
    {
        NotificationCategory.CrimeIntelligence => "Crime Intelligence Alert",
        NotificationCategory.Investigation => "Investigation Alert",
        NotificationCategory.System => "System Notification",
        _ => "Notification"
    };
}
