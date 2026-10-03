using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.Audit;
using NETRA.Domain.Audit;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// Read-only queries over the existing audit trail written by
/// <see cref="AuditService"/>. It introduces no table and no columns: the trail is
/// append-only, and this service only selects, filters and pages it.
/// </summary>
public sealed class AuditLogQueryService : IAuditLogQueryService
{
    private readonly NetraDbContext _context;

    public AuditLogQueryService(NetraDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<AuditLogListItemDto>> ListAsync(
        AuditLogFilterCriteria criteria, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, criteria.Page);
        var pageSize = Math.Clamp(criteria.PageSize, 1, AuditLogLimits.MaxPageSize);

        var query = _context.AuditLogs.AsNoTracking();

        if (criteria.ActorUserId is { } actorUserId)
        {
            query = query.Where(a => a.UserId == actorUserId);
        }

        if (!string.IsNullOrWhiteSpace(criteria.EntityType))
        {
            query = query.Where(a => a.EntityType == criteria.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Action))
        {
            query = query.Where(a => a.Action == criteria.Action);
        }

        if (criteria.DateFrom is { } dateFrom)
        {
            query = query.Where(a => a.Timestamp >= dateFrom);
        }

        if (criteria.DateTo is { } dateTo)
        {
            // Inclusive of the whole final day.
            var endExclusive = dateTo.Date.AddDays(1);
            query = query.Where(a => a.Timestamp < endExclusive);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.Timestamp,
                a.UserId,
                a.EntityType,
                a.EntityId,
                a.Action,
                a.PropertyName,
                a.OldValues,
                a.NewValues,
                a.IsSystemAction
            })
            .ToListAsync(cancellationToken);

        var actors = await LoadActorsAsync(
            rows.Select(r => r.UserId).Distinct().ToList(), cancellationToken);

        var items = rows.Select(r => new AuditLogListItemDto
        {
            Id = r.Id,
            Timestamp = r.Timestamp,
            ActorUserId = r.UserId,
            ActorName = actors.TryGetValue(r.UserId, out var actor) ? actor.DisplayName : null,
            ActorUserName = actors.TryGetValue(r.UserId, out var named) ? named.UserName : null,
            EntityType = r.EntityType,
            EntityId = r.EntityId,
            Action = r.Action,
            PropertyName = r.PropertyName,
            OldValues = r.OldValues,
            NewValues = r.NewValues,
            IsSystemAction = r.IsSystemAction
        }).ToList();

        return new PagedResult<AuditLogListItemDto>(items, total, page, pageSize);
    }

    public async Task<AuditLogDetailDto?> GetDetailAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                a.Id,
                a.Timestamp,
                a.UserId,
                a.EntityType,
                a.EntityId,
                a.Action,
                a.PropertyName,
                a.OldValues,
                a.NewValues,
                a.IpAddress,
                a.UserAgent,
                a.CorrelationId,
                a.SessionId,
                a.IsSystemAction
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var actors = await LoadActorsAsync([row.UserId], cancellationToken);
        actors.TryGetValue(row.UserId, out var actor);

        return new AuditLogDetailDto
        {
            Id = row.Id,
            Timestamp = row.Timestamp,
            ActorUserId = row.UserId,
            ActorName = actor?.DisplayName,
            ActorUserName = actor?.UserName,
            EntityType = row.EntityType,
            EntityId = row.EntityId,
            Action = row.Action,
            PropertyName = row.PropertyName,
            OldValues = row.OldValues,
            NewValues = row.NewValues,
            IpAddress = row.IpAddress,
            UserAgent = row.UserAgent,
            CorrelationId = row.CorrelationId,
            SessionId = row.SessionId,
            IsSystemAction = row.IsSystemAction
        };
    }

    public async Task<AuditLogFilterOptionsDto> GetFilterOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        // Each pick list is built from values that actually occur in the trail, so the
        // UI cannot offer a filter for a field the model does not have.
        var entityTypes = await _context.AuditLogs
            .AsNoTracking()
            .Select(a => a.EntityType)
            .Distinct()
            .OrderBy(v => v)
            .Take(AuditLogLimits.MaxFilterOptions)
            .ToListAsync(cancellationToken);

        var actions = await _context.AuditLogs
            .AsNoTracking()
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(v => v)
            .Take(AuditLogLimits.MaxFilterOptions)
            .ToListAsync(cancellationToken);

        var actorIds = await _context.AuditLogs
            .AsNoTracking()
            .Select(a => a.UserId)
            .Distinct()
            .OrderBy(v => v)
            .Take(AuditLogLimits.MaxFilterOptions)
            .ToListAsync(cancellationToken);

        var actors = await LoadActorsAsync(actorIds, cancellationToken);

        return new AuditLogFilterOptionsDto
        {
            EntityTypes = entityTypes,
            Actions = actions,
            Actors = actorIds
                .Select(id => actors.TryGetValue(id, out var actor)
                    ? new AuditActorOptionDto
                    {
                        UserId = id,
                        DisplayName = actor?.DisplayName ?? actor?.UserName ?? "Deleted account"
                    }
                    : null)
                .Where(a => a is not null)
                .Select(a => a!)
                .OrderBy(a => a.DisplayName, StringComparer.Ordinal)
                .ToList()
        };
    }

    private sealed record Actor(string DisplayName, string? UserName);

    /// <summary>
    /// Resolves actor display names in one bounded query per page instead of joining the
    /// user table into the trail query: the foreign key is required, but an explicit
    /// lookup behaves identically on every provider and keeps the page payload small.
    /// </summary>
    private async Task<Dictionary<Guid, Actor>> LoadActorsAsync(
        IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, Actor>();
        }

        var rows = await _context.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.Id,
            r => new Actor($"{r.FirstName} {r.LastName}".Trim(), r.UserName));
    }
}
