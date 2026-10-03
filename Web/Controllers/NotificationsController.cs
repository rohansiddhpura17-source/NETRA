using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Abstractions;
using NETRA.Application.Alerts;
using NETRA.Web.Models.ViewModels.Alerts;

namespace NETRA.Web.Controllers;

/// <summary>
/// In-application notification center. There is deliberately no userId parameter
/// anywhere: the recipient is always the signed-in user, and ownership is enforced
/// inside the service query, not by hidden form fields.
/// </summary>
[Authorize]
public class NotificationsController : Controller
{
    private readonly INotificationService _notifications;
    private readonly IAuditActorAccessor _actor;

    public NotificationsController(INotificationService notifications, IAuditActorAccessor actor)
    {
        _notifications = notifications;
        _actor = actor;
    }

    [HttpGet("/notifications")]
    public async Task<IActionResult> Index(
        bool unreadOnly = false,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var paged = await _notifications.GetForUserAsync(
            _actor.GetUserId(User), unreadOnly, page, cancellationToken);

        return View(NotificationsIndexViewModel.Create(paged, unreadOnly));
    }

    [HttpPost("/notifications/read/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(
        Guid id, bool unreadOnly = false, int page = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await _notifications.MarkReadAsync(id, _actor.GetUserId(User), cancellationToken);

        TempData[result.Succeeded ? "Netra.Success" : "Netra.Error"] = result.Succeeded
            ? "Notification marked as read."
            : result.Errors.Values.SelectMany(e => e).FirstOrDefault() ?? "The notification could not be updated.";

        return RedirectToAction(nameof(Index), new { unreadOnly, page });
    }

    [HttpPost("/notifications/read-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken = default)
    {
        var result = await _notifications.MarkAllReadAsync(_actor.GetUserId(User), cancellationToken);

        TempData[result.Succeeded ? "Netra.Success" : "Netra.Error"] = result.Succeeded
            ? "All notifications marked as read."
            : result.Errors.Values.SelectMany(e => e).FirstOrDefault() ?? "Notifications could not be updated.";

        return RedirectToAction(nameof(Index));
    }
}
