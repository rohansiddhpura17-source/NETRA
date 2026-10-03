using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using NETRA.Domain.Security;
using NETRA.Web.Models.ViewModels;

namespace NETRA.Web.Services;

public interface INetraUserContext
{
    Task<CurrentUserViewModel?> GetCurrentUserAsync(ClaimsPrincipal principal);
}

/// <summary>
/// Resolves the signed-in user for presentation purposes.
/// View-visibility only — never a substitute for authorization.
/// </summary>
public sealed class NetraUserContext : INetraUserContext
{
    private readonly UserManager<ApplicationUser> _userManager;

    public NetraUserContext(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<CurrentUserViewModel?> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var user = await _userManager.GetUserAsync(principal);
        if (user == null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);

        var displayName = !string.IsNullOrWhiteSpace(user.FullName)
            ? user.FullName
            : user.UserName ?? user.Email ?? "User";

        return new CurrentUserViewModel
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            DisplayName = displayName,
            BadgeNumber = user.BadgeNumber,
            Rank = user.Rank,
            Initials = BuildInitials(displayName),
            Roles = roles.ToList(),
            PrimaryRole = roles.FirstOrDefault() ?? string.Empty
        };
    }

    internal static string BuildInitials(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "U";
        }

        var parts = displayName
            .Split(new[] { ' ', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !char.IsDigit(part[0]))
            .ToArray();

        if (parts.Length == 0)
        {
            return displayName[..1].ToUpperInvariant();
        }

        if (parts.Length == 1)
        {
            return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        }

        return string.Concat(
            parts[0][..1].ToUpperInvariant(),
            parts[^1][..1].ToUpperInvariant());
    }
}