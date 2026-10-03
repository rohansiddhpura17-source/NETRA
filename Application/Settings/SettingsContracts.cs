using NETRA.Application.Abstractions;
using NETRA.Domain.Enums;

namespace NETRA.Application.Settings;

/// <summary>
/// Row spacing applied by the server-rendered NETRA theme. There is no theme
/// framework and no CSS is stored: the value only selects one of two fixed
/// spacing overrides.
/// </summary>
public enum UiDensity
{
    Comfortable,
    Compact
}

/// <summary>The signed-in user's own account details. Read-only Identity fields are included for display only.</summary>
public sealed class ProfileDto
{
    public required Guid UserId { get; init; }

    /// <summary>Sign-in identifier. Fixed at creation and never editable from the settings pages.</summary>
    public required string UserName { get; init; }

    public string? Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public string? BadgeNumber { get; init; }

    public string? Rank { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }

    public required DateTime MemberSince { get; init; }

    public DateTime? LastLoginAt { get; init; }

    /// <summary>False while an administrator has deactivated the account.</summary>
    public bool IsActive { get; init; }

    /// <summary>The name other officers see; falls back to the sign-in name when the profile is blank.</summary>
    public string FullName =>
        $"{FirstName} {LastName}".Trim() is { Length: > 0 } name ? name : UserName;
}

/// <summary>
/// The fields a user may change about themselves. There is no user-name, email or
/// role field: those belong to the account's sign-in identity and are changed through
/// User Management by an administrator.
/// </summary>
public sealed class UpdateProfileInput
{
    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public string? BadgeNumber { get; init; }

    public string? Rank { get; init; }
}

/// <summary>Account security state for the current user. Carries no secret value whatsoever.</summary>
public sealed class SecurityStatusDto
{
    public required Guid UserId { get; init; }

    public required string UserName { get; init; }

    public string? Email { get; init; }

    public bool EmailConfirmed { get; init; }

    public bool TwoFactorEnabled { get; init; }

    public bool IsLockedOut { get; init; }

    /// <summary>When an active lockout expires.</summary>
    public DateTimeOffset? LockoutExpiresAt { get; init; }

    public DateTime? LastLoginAt { get; init; }

    /// <summary>The password policy Identity actually enforces on this deployment.</summary>
    public required PasswordPolicyDto PasswordPolicy { get; init; }

    /// <summary>True when the account has any password set at all.</summary>
    public bool HasPassword { get; init; }
}

/// <summary>Password rules read from <c>IdentityOptions</c> so the page cannot promise rules the server does not apply.</summary>
public sealed class PasswordPolicyDto
{
    public required int RequiredLength { get; init; }

    public required bool RequireDigit { get; init; }

    public required bool RequireUppercase { get; init; }

    public required bool RequireLowercase { get; init; }

    public required bool RequireNonAlphanumeric { get; init; }
}

/// <summary>
/// Password change input. The values are handed to <see cref="Microsoft.AspNetCore.Identity.UserManager{TUser}"/>
/// and are never stored, echoed, or written to the audit trail.
/// </summary>
public sealed class ChangePasswordInput
{
    public required string CurrentPassword { get; init; }

    public required string NewPassword { get; init; }

    public required string ConfirmPassword { get; init; }
}

/// <summary>
/// One notification category with the user's effective choice. <see cref="IsEnabled"/>
/// is the effective state: <c>AlertService</c> treats a missing row as enabled, so
/// <see cref="HasStoredRow"/> only tells the UI whether the choice is explicit.
/// </summary>
public sealed class NotificationPreferenceDto
{
    public required NotificationCategory Category { get; init; }

    public required string Label { get; init; }

    public required bool IsEnabled { get; init; }

    public required bool HasStoredRow { get; init; }
}

/// <summary>Desired enable state per notification category.</summary>
public sealed class UpdateNotificationPreferencesInput
{
    public required IReadOnlyDictionary<NotificationCategory, bool> EnabledByCategory { get; init; }
}

/// <summary>Effective display choices for the current user.</summary>
public sealed class AppearanceDto
{
    public required UiDensity Density { get; init; }

    /// <summary>When true, hover and drawer transitions are removed by a body class.</summary>
    public required bool ReducedMotion { get; init; }
}

public sealed class UpdateAppearanceInput
{
    public required UiDensity Density { get; init; }

    public required bool ReducedMotion { get; init; }
}

/// <summary>A stored administrative setting row. Presented read-only.</summary>
public sealed class AdminSettingDto
{
    public required string Key { get; init; }

    public required string Value { get; init; }

    public string? Category { get; init; }

    public string? Description { get; init; }

    public required DateTime UpdatedAt { get; init; }

    public string? UpdatedBy { get; init; }

    /// <summary>System preferences are flagged read-only; security settings are system-owned.</summary>
    public required bool IsSystemOwned { get; init; }
}

/// <summary>Everything the system settings page may show. Nothing here is editable from the UI.</summary>
public sealed class SystemSettingsDto
{
    public required IReadOnlyList<AdminSettingDto> Preferences { get; init; }

    public required IReadOnlyList<AdminSettingDto> SecuritySettings { get; init; }
}

/// <summary>
/// Personal and administrative settings. Every personal method takes the actor's user
/// id, which callers derive from the authenticated principal — never from a form value —
/// and the service scopes all reads and writes to it.
/// </summary>
public interface ISettingsService
{
    Task<ProfileDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateProfileAsync(
        Guid userId, UpdateProfileInput input, CancellationToken cancellationToken = default);

    Task<SecurityStatusDto?> GetSecurityStatusAsync(
        Guid userId, CancellationToken cancellationToken = default);

    Task<OperationResult> ChangePasswordAsync(
        Guid userId, ChangePasswordInput input, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationPreferenceDto>> GetNotificationPreferencesAsync(
        Guid userId, CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateNotificationPreferencesAsync(
        Guid userId,
        UpdateNotificationPreferencesInput input,
        CancellationToken cancellationToken = default);

    Task<AppearanceDto> GetAppearanceAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateAppearanceAsync(
        Guid userId, UpdateAppearanceInput input, CancellationToken cancellationToken = default);

    Task<SystemSettingsDto> GetSystemSettingsAsync(CancellationToken cancellationToken = default);
}
