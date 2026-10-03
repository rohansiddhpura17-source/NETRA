using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Abstractions;
using NETRA.Application.Settings;
using NETRA.Domain.Security;
using NETRA.Domain.Enums;
using NETRA.Web.Authorization;
using NETRA.Web.Models.Navigation;
using NETRA.Web.Models.Ui;
using NETRA.Web.Models.ViewModels.Settings;

namespace NETRA.Web.Controllers;

/// <summary>
/// Settings: the signed-in user's own account, password, notification and display
/// preferences, plus the administrator-only system settings page.
/// </summary>
/// <remarks>
/// There is no user identifier in any route or form: every action resolves the actor
/// from <see cref="IAuditActorAccessor"/> against <c>HttpContext.User</c>, so a posted
/// field cannot aim a change at another account. Personal settings need only the
/// authentication gate; the system page uses the existing administrative policy.
/// </remarks>
[Authorize]
public class SettingsController : Controller
{
    private readonly ISettingsService _settings;
    private readonly IAuditActorAccessor _actor;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public SettingsController(
        ISettingsService settings,
        IAuditActorAccessor actor,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _settings = settings;
        _actor = actor;
        _signInManager = signInManager;
        _userManager = userManager;
    }

    /// <summary>The actor's own account id. Empty only when the cookie has no Identity subject.</summary>
    private Guid ActorId => _actor.GetUserId(User);

    [HttpGet("/settings")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = ActorId;
        if (userId == Guid.Empty)
        {
            return NotFound();
        }

        var profile = await _settings.GetProfileAsync(userId, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        var appearance = await _settings.GetAppearanceAsync(userId, cancellationToken);
        var preferences = await _settings.GetNotificationPreferencesAsync(userId, cancellationToken);

        var cards = new List<SettingsCardViewModel>
        {
            new()
            {
                Title = "Account",
                Description = "Your name, badge number and rank as other officers see them.",
                Url = "/settings/account",
                Icon = NetraIcon.User,
                Status = profile.IsActive ? "Active account" : "Inactive account",
                StatusTone = profile.IsActive ? NetraTone.Success : NetraTone.Warning
            },
            new()
            {
                Title = "Security",
                Description = "Change your password and review your account's sign-in state.",
                Url = "/settings/security",
                Icon = NetraIcon.Shield,
                Status = "Password change only",
                StatusTone = NetraTone.Neutral
            },
            new()
            {
                Title = "Notifications",
                Description = "Choose which NETRA alert categories reach your in-app inbox.",
                Url = "/settings/notifications",
                Icon = NetraIcon.Bell,
                Status = $"{preferences.Count(p => p.IsEnabled)} of {preferences.Count} enabled",
                StatusTone = NetraTone.Info
            },
            new()
            {
                Title = "Appearance",
                Description = "Row spacing and motion for the NETRA theme. Applied by the server.",
                Url = "/settings/appearance",
                Icon = NetraIcon.Appearance,
                Status = $"{appearance.Density} · motion {(appearance.ReducedMotion ? "reduced" : "full")}",
                StatusTone = NetraTone.Neutral
            }
        };

        if (User.IsInRole(NetraRoles.SystemAdministrator))
        {
            cards.Add(new SettingsCardViewModel
            {
                Title = "System preferences",
                Description = "Stored system and security settings. Read-only; runtime configuration " +
                    "is owned by the deployment.",
                Url = "/settings/system-preferences",
                Icon = NetraIcon.Sliders,
                Status = "Administrator",
                StatusTone = NetraTone.Warning
            });
        }

        cards.Add(new SettingsCardViewModel
        {
            Title = "Support",
            Description = "How NETRA works: module guidance, workflows and the access model.",
            Url = "/support",
            Icon = NetraIcon.Support,
            Status = "Help",
            StatusTone = NetraTone.Neutral
        });

        return View(new SettingsOverviewViewModel
        {
            Cards = cards,
            DisplayName = profile.FullName,
            UserName = profile.UserName,
            Roles = profile.Roles
        });
    }

    [HttpGet("/settings/account")]
    public async Task<IActionResult> Account(CancellationToken cancellationToken)
    {
        var profile = await _settings.GetProfileAsync(ActorId, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        return View(AccountSettingsViewModel.FromProfile(profile));
    }

    [HttpPost("/settings/account")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Account(
        AccountSettingsViewModel posted, CancellationToken cancellationToken)
    {
        var userId = ActorId;
        if (userId == Guid.Empty)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            var result = await _settings.UpdateProfileAsync(userId, posted.ToInput(), cancellationToken);

            if (result.Succeeded)
            {
                TempData["Netra.Success"] = "Profile updated.";
                return RedirectToAction(nameof(Account));
            }

            TryCarryOverFailures(result);
        }

        // Re-read the account so the read-only fields are the server's values, then put the
        // submitted text back in the editable boxes so the user sees what they typed.
        var profile = await _settings.GetProfileAsync(userId, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        var model = AccountSettingsViewModel.FromProfile(profile);
        model.FirstName = posted.FirstName;
        model.LastName = posted.LastName;
        model.BadgeNumber = posted.BadgeNumber;
        model.Rank = posted.Rank;

        return View(model);
    }

    [HttpGet("/settings/security")]
    public async Task<IActionResult> Security(CancellationToken cancellationToken)
    {
        var status = await _settings.GetSecurityStatusAsync(ActorId, cancellationToken);
        if (status is null)
        {
            return NotFound();
        }

        return View(SecuritySettingsViewModel.FromStatus(status));
    }

    [HttpPost("/settings/security")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Security(
        SecuritySettingsViewModel posted, CancellationToken cancellationToken)
    {
        var userId = ActorId;
        if (userId == Guid.Empty)
        {
            return NotFound();
        }

        // Validation runs on the submitted values first: clearing them beforehand would
        // erase the [Required] failures along with the values, leaving a form that re-renders
        // with nothing to explain itself.
        if (ModelState.IsValid)
        {
            var result = await _settings.ChangePasswordAsync(userId, posted.ToInput(), cancellationToken);

            if (result.Succeeded)
            {
                // Changing a password rotates the security stamp, which would otherwise end
                // this session at its next revalidation. Re-issuing the cookie keeps the
                // current browser signed in; every other session is invalidated as intended.
                if (await _userManager.GetUserAsync(User) is { } account)
                {
                    await _signInManager.RefreshSignInAsync(account);
                }

                TempData["Netra.Success"] =
                    "Password changed. Other sessions using this account will be signed out.";
                return RedirectToAction(nameof(Security));
            }

            TryCarryOverFailures(result);
        }

        ClearPasswordValues();

        var status = await _settings.GetSecurityStatusAsync(userId, cancellationToken);
        if (status is null)
        {
            return NotFound();
        }

        // A brand new view model: no field of it was ever populated from a post.
        return View(SecuritySettingsViewModel.FromStatus(status));
    }

    [HttpGet("/settings/notifications")]
    public async Task<IActionResult> Notifications(CancellationToken cancellationToken)
    {
        var preferences = await _settings.GetNotificationPreferencesAsync(
            ActorId, cancellationToken);

        return View(NotificationSettingsViewModel.From(preferences));
    }

    [HttpPost("/settings/notifications")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Notifications(
        NotificationSettingsViewModel posted, CancellationToken cancellationToken)
    {
        var userId = ActorId;
        if (userId == Guid.Empty)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            var result = await _settings.UpdateNotificationPreferencesAsync(
                userId, posted.ToInput(), cancellationToken);

            if (result.Succeeded)
            {
                TempData["Netra.Success"] = "Notification preferences updated.";
                return RedirectToAction(nameof(Notifications));
            }

            TryCarryOverFailures(result);
        }

        // Re-render from the stored state, then re-apply the posted selection so a rejected
        // submission never silently reverts the boxes the user left them in.
        var model = NotificationSettingsViewModel.From(
            await _settings.GetNotificationPreferencesAsync(userId, cancellationToken));

        model.EnabledCategories = posted.EnabledCategories;
        model.ReflectPostedSelection();

        return View(model);
    }

    [HttpGet("/settings/appearance")]
    public async Task<IActionResult> Appearance(CancellationToken cancellationToken)
    {
        var appearance = await _settings.GetAppearanceAsync(ActorId, cancellationToken);

        return View(AppearanceSettingsViewModel.From(appearance));
    }

    [HttpPost("/settings/appearance")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Appearance(
        AppearanceSettingsViewModel model, CancellationToken cancellationToken)
    {
        var userId = ActorId;
        if (userId == Guid.Empty)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _settings.UpdateAppearanceAsync(userId, model.ToInput(), cancellationToken);

        if (result.Succeeded)
        {
            // The redirect is what proves the choice took effect: the next render reads the
            // stored value and the layout applies the spacing class.
            TempData["Netra.Success"] = "Appearance updated.";
            return RedirectToAction(nameof(Appearance));
        }

        TryCarryOverFailures(result);

        return View(AppearanceSettingsViewModel.From(
            await _settings.GetAppearanceAsync(userId, cancellationToken)));
    }

    /// <summary>
    /// System settings. Read-only by design — the page lists the stored setting tables so an
    /// administrator can see what the application has recorded, and it deliberately renders
    /// nothing from configuration files, environment variables or connection strings.
    /// </summary>
    [HttpGet("/settings/system-preferences")]
    [Authorize(Policy = NetraPolicies.Administration)]
    public async Task<IActionResult> SystemPreferences(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetSystemSettingsAsync(cancellationToken);

        return View(SystemSettingsViewModel.From(settings));
    }

    /// <summary>
    /// Drops the submitted password values from the binding context while keeping the
    /// messages beside them, so a rejected change re-renders explained but no password can be
    /// read back out of the response body.
    /// </summary>
    private void ClearPasswordValues()
    {
        foreach (var field in new[]
                 {
                     nameof(SecuritySettingsViewModel.CurrentPassword),
                     nameof(SecuritySettingsViewModel.NewPassword),
                     nameof(SecuritySettingsViewModel.ConfirmPassword)
                 })
        {
            if (ModelState.TryGetValue(field, out var entry) is true && entry is { RawValue: not null })
            {
                var messages = entry.Errors
                    .Select(error => error.ErrorMessage)
                    .Where(message => !string.IsNullOrEmpty(message))
                    .ToArray();

                ModelState.Remove(field);

                foreach (var message in messages)
                {
                    ModelState.AddModelError(field, message);
                }
            }
        }
    }

    /// <summary>
    /// Copies service validation, conflict and permission messages into <c>ModelState</c> so
    /// the form re-renders with the reason. Field-level messages keep their field; a bare
    /// message lands in the summary.
    /// </summary>
    private void TryCarryOverFailures(OperationResult result)
    {
        foreach (var (field, messages) in result.Errors)
        {
            foreach (var message in messages)
            {
                if (string.IsNullOrEmpty(field))
                {
                    ModelState.AddModelError(string.Empty, message);
                }
                else
                {
                    ModelState.AddModelError(field, message);
                }
            }
        }
    }
}
