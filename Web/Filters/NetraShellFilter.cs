using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NETRA.Application.Alerts;
using NETRA.Web.Models.Navigation;
using NETRA.Web.Models.ViewModels;
using NETRA.Web.Services;

namespace NETRA.Web.Filters;

/// <summary>
/// Populates the data the NETRA shell needs: the signed-in user, their roles, the
/// sidebar navigation filtered to those roles, and the real unread-notification
/// count shown on the bell.
/// </summary>
/// <remarks>
/// Navigation visibility is a presentation concern only. It is not a security control:
/// every action still enforces its own <c>[Authorize]</c> / role attributes.
/// </remarks>
public sealed class NetraShellFilter : IAsyncActionFilter
{
    private readonly INetraUserContext _userContext;
    private readonly INotificationService _notifications;

    public NetraShellFilter(INetraUserContext userContext, INotificationService notifications)
    {
        _userContext = userContext;
        _notifications = notifications;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = await _userContext.GetCurrentUserAsync(context.HttpContext.User);
        var roles = user?.Roles ?? (IReadOnlyList<string>)Array.Empty<string>();

        int unreadNotifications = 0;
        if (user != null)
        {
            // Single indexed count query; the result is a real number, never a placeholder.
            var inbox = await _notifications.GetForUserAsync(user.Id, page: 1);
            unreadNotifications = inbox.UnreadCount;
        }

        var sections = NetraNavigation.Sections
            .Select(section => new NetraNavSection
            {
                Label = section.Label,
                Items = section.Items.Where(item => item.IsVisibleTo(roles)).ToList()
            })
            .Where(section => section.Items.Count > 0)
            .ToList();

        var footerItems = NetraNavigation.FooterItems
            .Where(item => item.IsVisibleTo(roles))
            .ToList();

        // Controller.ViewData is the dictionary the view will render against.
        if (context.Controller is Controller controller)
        {
            controller.ViewData["CurrentUser"] = user;
            controller.ViewData["CurrentUserRoles"] = roles;
            controller.ViewData["NavigationSections"] = sections;
            controller.ViewData["NavigationFooterItems"] = footerItems;
            controller.ViewData["UnreadNotificationCount"] = unreadNotifications;
        }

        await next();
    }
}