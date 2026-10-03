using System.Globalization;
using NETRA.Application.Alerts;
using NETRA.Web.Models.Navigation;
using NETRA.Web.Models.Ui;

namespace NETRA.Web.Models.ViewModels.Alerts;

public sealed class NotificationDisplayItem
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public string? Message { get; init; }

    public required string CategoryLabel { get; init; }

    public required bool IsRead { get; init; }

    public required string CreatedAtText { get; init; }

    public string? ReadAtText { get; init; }

    public Guid? AlertId { get; init; }

    public string? AlertTitle { get; init; }

    public string? AlertSeverityLabel { get; init; }

    public NetraTone AlertSeverityTone { get; init; }

    public static NotificationDisplayItem From(NotificationDto dto) => new()
    {
        Id = dto.Id,
        Title = dto.Title,
        Message = dto.Message,
        CategoryLabel = NotificationCategories.Label(dto.Category),
        IsRead = dto.IsRead,
        CreatedAtText = dto.CreatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture),
        ReadAtText = dto.ReadAt?.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture),
        AlertId = dto.AlertId,
        AlertTitle = dto.AlertTitle,
        AlertSeverityLabel = dto.AlertSeverity is { } severity
            ? NetraDisplay.Words(severity.ToString())
            : null,
        AlertSeverityTone = dto.AlertSeverity is { } s ? AlertTones.Severity(s) : NetraTone.Neutral
    };
}

public sealed class NotificationsIndexViewModel
{
    public required IReadOnlyList<NotificationDisplayItem> Notifications { get; init; }

    public required PaginationModel Pagination { get; init; }

    public int UnreadCount { get; init; }

    public int TotalCount { get; init; }

    public bool UnreadOnly { get; init; }

    public int Page { get; init; } = 1;

    public bool HasItems => Notifications.Count > 0;

    public static NotificationsIndexViewModel Create(PagedNotificationsDto paged, bool unreadOnly)
    {
        var routeValues = new Dictionary<string, string?>
        {
            ["unreadOnly"] = unreadOnly ? "true" : null
        };

        return new NotificationsIndexViewModel
        {
            Notifications = paged.Items.Select(NotificationDisplayItem.From).ToList(),
            UnreadCount = paged.UnreadCount,
            TotalCount = paged.TotalCount,
            UnreadOnly = unreadOnly,
            Page = paged.Page,
            Pagination = new PaginationModel
            {
                Page = paged.Page,
                TotalPages = Math.Max(1, paged.TotalPages),
                TotalItems = paged.TotalCount,
                PageSize = paged.PageSize,
                Controller = "Notifications",
                Action = "Index",
                RouteValues = routeValues
            }
        };
    }
}
