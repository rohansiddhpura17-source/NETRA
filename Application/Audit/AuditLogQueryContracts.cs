using NETRA.Application.Abstractions;
using NETRA.Domain.Audit;

namespace NETRA.Application.Audit;

/// <summary>
/// Read bounds for the audit trail. The trail is append-only and grows without a
/// ceiling, so every query is page-bounded and the filter pick lists are capped.
/// </summary>
public static class AuditLogLimits
{
    public const int DefaultPageSize = 25;

    public const int MaxPageSize = 100;

    /// <summary>Upper bound for a filter pick list, not a correctness limit.</summary>
    public const int MaxFilterOptions = 200;
}

/// <summary>
/// Server-side audit filter. Every field is a real <see cref="AuditLog"/> column;
/// the model has no severity or workflow state, so neither is offered.
/// </summary>
public sealed class AuditLogFilterCriteria
{
    public Guid? ActorUserId { get; init; }

    public string? EntityType { get; init; }

    public string? Action { get; init; }

    public DateTime? DateFrom { get; init; }

    public DateTime? DateTo { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = AuditLogLimits.DefaultPageSize;
}

/// <summary>One audit row as listed. Payload text is present but page-bounded.</summary>
public sealed class AuditLogListItemDto
{
    public required Guid Id { get; init; }

    public required DateTime Timestamp { get; init; }

    public required Guid ActorUserId { get; init; }

    /// <summary>Null when the actor's account has been deleted.</summary>
    public string? ActorName { get; init; }

    public string? ActorUserName { get; init; }

    public required string EntityType { get; init; }

    public required Guid EntityId { get; init; }

    public required string Action { get; init; }

    public string? PropertyName { get; init; }

    public string? OldValues { get; init; }

    public string? NewValues { get; init; }

    public required bool IsSystemAction { get; init; }
}

/// <summary>The complete record, including the request context that produced it.</summary>
public sealed class AuditLogDetailDto
{
    public required Guid Id { get; init; }

    public required DateTime Timestamp { get; init; }

    public required Guid ActorUserId { get; init; }

    public string? ActorName { get; init; }

    public string? ActorUserName { get; init; }

    public required string EntityType { get; init; }

    public required Guid EntityId { get; init; }

    public required string Action { get; init; }

    public string? PropertyName { get; init; }

    public string? OldValues { get; init; }

    public string? NewValues { get; init; }

    public string? IpAddress { get; init; }

    public string? UserAgent { get; init; }

    public string? CorrelationId { get; init; }

    public string? SessionId { get; init; }

    public required bool IsSystemAction { get; init; }
}

public sealed class AuditActorOptionDto
{
    public required Guid UserId { get; init; }

    public required string DisplayName { get; init; }
}

/// <summary>Pick-list values drawn from the rows that actually exist.</summary>
public sealed class AuditLogFilterOptionsDto
{
    public IReadOnlyList<AuditActorOptionDto> Actors { get; init; } = Array.Empty<AuditActorOptionDto>();

    public IReadOnlyList<string> EntityTypes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Read-only view of the existing audit trail. Writes stay with
/// <see cref="IAuditService"/>; this service never inserts, updates or deletes.
/// </summary>
public interface IAuditLogQueryService
{
    Task<PagedResult<AuditLogListItemDto>> ListAsync(
        AuditLogFilterCriteria criteria, CancellationToken cancellationToken = default);

    Task<AuditLogDetailDto?> GetDetailAsync(
        Guid id, CancellationToken cancellationToken = default);

    Task<AuditLogFilterOptionsDto> GetFilterOptionsAsync(
        CancellationToken cancellationToken = default);
}
