using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.Alerts;
using NETRA.Domain.Alerts;
using NETRA.Domain.Enums;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// In-application notification center. Every read and write is ownership-scoped
/// inside the database query (<c>RecipientId == actorUserId</c>); there is no code
/// path that addresses notifications by an externally supplied user id. Foreign
/// notification ids are audited and answered with the same "not found" result as
/// genuinely missing ones, so existence is never leaked.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly NetraDbContext _context;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;

    public NotificationService(NetraDbContext context, IAuditService audit, TimeProvider time)
    {
        _context = context;
        _audit = audit;
        _time = time;
    }

    public async Task<PagedNotificationsDto> GetForUserAsync(
        Guid actorUserId, bool unreadOnly = false, int page = 1,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);

        var query = _context.Notifications.AsNoTracking()
            .Where(n => n.RecipientId == actorUserId);

        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        var total = await query.CountAsync(cancellationToken);
        var unread = await _context.Notifications.AsNoTracking()
            .CountAsync(n => n.RecipientId == actorUserId && !n.IsRead, cancellationToken);

        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            // A fan-out writes many notifications with the same timestamp.
            .ThenBy(n => n.Id)
            .Skip((page - 1) * AlertLimits.MaxNotificationPageSize)
            .Take(AlertLimits.MaxNotificationPageSize)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.Message,
                n.NotificationType,
                n.IsRead,
                n.CreatedAt,
                n.ReadAt,
                n.AlertId,
                AlertTitle = n.Alert == null ? null : n.Alert.Title,
                AlertSeverity = n.Alert == null ? null : (AlertSeverity?)n.Alert.Severity
            })
            .ToListAsync(cancellationToken);

        return new PagedNotificationsDto
        {
            Items = rows.Select(n => new NotificationDto
            {
                Id = n.Id,
                Title = n.Title,
                Message = n.Message,
                Category = (NotificationCategory)n.NotificationType,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt,
                ReadAt = n.ReadAt,
                AlertId = n.AlertId,
                AlertTitle = n.AlertTitle,
                AlertSeverity = n.AlertSeverity
            }).ToList(),
            Page = page,
            PageSize = AlertLimits.MaxNotificationPageSize,
            TotalCount = total,
            UnreadCount = unread
        };
    }

    public async Task<OperationResult> MarkReadAsync(
        Guid notificationId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        // Ownership filter is part of the lookup: a foreign id resolves to null.
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientId == actorUserId,
                cancellationToken);

        if (notification is null)
        {
            var exists = await _context.Notifications.AnyAsync(n => n.Id == notificationId, cancellationToken);
            if (exists)
            {
                _audit.RecordSecurityEvent(
                    AuditActionNames.NotificationAccessDenied, actorUserId,
                    detail: $"Attempted to mark notification {notificationId:N} belonging to another user.");
                await _context.SaveChangesAsync(cancellationToken);
            }

            return OperationResult.NotFound("The notification was not found.");
        }

        if (!notification.IsRead)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            notification.IsRead = true;
            notification.ReadAt = now;
            _audit.Record(
                AuditEntityTypeNames.Notification, notification.Id,
                AuditActionNames.NotificationMarkedRead, actorUserId,
                propertyName: nameof(Notification.IsRead), oldValue: "false", newValue: "true");
            await _context.SaveChangesAsync(cancellationToken);
        }

        return OperationResult.Success();
    }

    public async Task<OperationResult> MarkAllReadAsync(
        Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var unread = await _context.Notifications
            .Where(n => n.RecipientId == actorUserId && !n.IsRead)
            .ToListAsync(cancellationToken);

        if (unread.Count == 0)
        {
            return OperationResult.Success();
        }

        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        _audit.Record(
            AuditEntityTypeNames.Notification, actorUserId,
            AuditActionNames.NotificationMarkedRead, actorUserId,
            newValue: $"{unread.Count} notification(s) marked read");
        await _context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success();
    }
}
