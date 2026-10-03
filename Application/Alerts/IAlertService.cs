using NETRA.Application.Abstractions;
using NETRA.Domain.Enums;

namespace NETRA.Application.Alerts;

/// <summary>
/// Alert reading, lifecycle transitions and idempotent generation from the
/// existing analytical subsystems. Alerts are NETRA-wide intelligence items;
/// every mutation is audited and validated server-side.
/// </summary>
public interface IAlertService
{
    Task<PagedAlertsDto> ListAsync(AlertFilterCriteria criteria, CancellationToken cancellationToken = default);

    Task<AlertSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Case ids for the related-case filter dropdown (most recently reported first).</summary>
    Task<IReadOnlyList<CaseOptionDto>> GetCaseOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Null when the alert does not exist. Viewed state is audited for the actor.</summary>
    Task<AlertDetailDto?> GetDetailAsync(Guid alertId, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<OperationResult> AcknowledgeAsync(Guid alertId, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<OperationResult> ResolveAsync(Guid alertId, string? resolutionNotes, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<OperationResult> DismissAsync(Guid alertId, string? reason, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the deterministic analytical generation pass over the given window.
    /// Re-running for the same window never duplicates alerts (stable AlertKey).
    /// </summary>
    Task<AlertGenerationResultDto> GenerateAsync(int windowDays, Guid actorUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-application notification center. Every operation is ownership-scoped in
/// the database query; a caller can never address another user's notifications.
/// </summary>
public interface INotificationService
{
    Task<PagedNotificationsDto> GetForUserAsync(
        Guid actorUserId, bool unreadOnly = false, int page = 1,
        CancellationToken cancellationToken = default);

    Task<OperationResult> MarkReadAsync(Guid notificationId, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<OperationResult> MarkAllReadAsync(Guid actorUserId, CancellationToken cancellationToken = default);
}
