using System.ComponentModel.DataAnnotations;
using NETRA.Application.Settings;
using NETRA.Domain.Enums;
using NETRA.Web.Models.Navigation;
using NETRA.Web.Models.Ui;

namespace NETRA.Web.Models.ViewModels.Settings;

/// <summary>
/// The signed-in user's own profile. Only the four officer-detail fields are editable;
/// the sign-in identity (username, email) is rendered read-only with no <c>name</c>
/// attribute, so it is never posted and cannot be bound from a forged field.
/// </summary>
/// <remarks>
/// <c>UserId</c> and <c>UserName</c> are deliberately not <c>required</c>: a property the
/// form never posts must not take part in model validation, or an honest submission would
/// fail on a field the user cannot even see. They are server-assigned by
/// <see cref="FromProfile"/> and the editable fields are the only ones written back.
/// </remarks>
public sealed class AccountSettingsViewModel
{
    public const int MaxNameLength = 100;
    public const int MaxBadgeLength = 50;
    public const int MaxRankLength = 100;

    public Guid UserId { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string? Email { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    public DateTime MemberSince { get; init; }

    public DateTime? LastLoginAt { get; init; }

    public bool IsActive { get; init; }

    [Required(ErrorMessage = "First name is required.")]
    [StringLength(MaxNameLength, ErrorMessage = "First name must be 100 characters or fewer.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(MaxNameLength, ErrorMessage = "Last name must be 100 characters or fewer.")]
    public string LastName { get; set; } = string.Empty;

    [StringLength(MaxBadgeLength, ErrorMessage = "Badge number must be 50 characters or fewer.")]
    public string? BadgeNumber { get; set; }

    [StringLength(MaxRankLength, ErrorMessage = "Rank must be 100 characters or fewer.")]
    public string? Rank { get; set; }

    public string DisplayName => $"{FirstName} {LastName}".Trim();

    public static AccountSettingsViewModel FromProfile(ProfileDto profile) => new()
    {
        UserId = profile.UserId,
        UserName = profile.UserName,
        Email = profile.Email,
        Roles = profile.Roles,
        MemberSince = profile.MemberSince,
        LastLoginAt = profile.LastLoginAt,
        IsActive = profile.IsActive,
        FirstName = profile.FirstName,
        LastName = profile.LastName,
        BadgeNumber = profile.BadgeNumber,
        Rank = profile.Rank
    };

    public UpdateProfileInput ToInput() => new()
    {
        FirstName = FirstName,
        LastName = LastName,
        BadgeNumber = BadgeNumber,
        Rank = Rank
    };
}

/// <summary>
/// Password change plus a read-only account security summary. The three password fields
/// are write-only: the controller clears their submitted values before any re-render, so a
/// password is never echoed back into the response body. The read-only members are not
/// <c>required</c> because the form does not post them.
/// </summary>
public sealed class SecuritySettingsViewModel
{
    public Guid UserId { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string? Email { get; init; }

    public bool EmailConfirmed { get; init; }

    public bool TwoFactorEnabled { get; init; }

    public bool IsLockedOut { get; init; }

    public DateTimeOffset? LockoutExpiresAt { get; init; }

    public DateTime? LastLoginAt { get; init; }

    public bool HasPassword { get; init; }

    public PasswordPolicyViewModel PasswordPolicy { get; init; } = new();

    [Required(ErrorMessage = "Enter your current password.")]
    [DataType(DataType.Password)]
    public string? CurrentPassword { get; set; }

    [Required(ErrorMessage = "Enter a new password.")]
    [DataType(DataType.Password)]
    public string? NewPassword { get; set; }

    [Required(ErrorMessage = "Re-enter the new password.")]
    [DataType(DataType.Password)]
    public string? ConfirmPassword { get; set; }

    public static SecuritySettingsViewModel FromStatus(SecurityStatusDto status) => new()
    {
        UserId = status.UserId,
        UserName = status.UserName,
        Email = status.Email,
        EmailConfirmed = status.EmailConfirmed,
        TwoFactorEnabled = status.TwoFactorEnabled,
        IsLockedOut = status.IsLockedOut,
        LockoutExpiresAt = status.LockoutExpiresAt,
        LastLoginAt = status.LastLoginAt,
        HasPassword = status.HasPassword,
        PasswordPolicy = PasswordPolicyViewModel.From(status.PasswordPolicy)
    };

    public ChangePasswordInput ToInput() => new()
    {
        CurrentPassword = CurrentPassword ?? string.Empty,
        NewPassword = NewPassword ?? string.Empty,
        ConfirmPassword = ConfirmPassword ?? string.Empty
    };
}

/// <summary>The password rules Identity enforces on this deployment, for display only.</summary>
public sealed class PasswordPolicyViewModel
{
    public int RequiredLength { get; init; }

    public bool RequireDigit { get; init; }

    public bool RequireUppercase { get; init; }

    public bool RequireLowercase { get; init; }

    public bool RequireNonAlphanumeric { get; init; }

    public static PasswordPolicyViewModel From(PasswordPolicyDto policy) => new()
    {
        RequiredLength = policy.RequiredLength,
        RequireDigit = policy.RequireDigit,
        RequireUppercase = policy.RequireUppercase,
        RequireLowercase = policy.RequireLowercase,
        RequireNonAlphanumeric = policy.RequireNonAlphanumeric
    };

    public string Describe()
    {
        var rules = new List<string> { $"at least {RequiredLength} characters" };

        if (RequireDigit)
        {
            rules.Add("a digit");
        }

        if (RequireUppercase)
        {
            rules.Add("an uppercase letter");
        }

        if (RequireLowercase)
        {
            rules.Add("a lowercase letter");
        }

        if (RequireNonAlphanumeric)
        {
            rules.Add("a non-alphanumeric character");
        }

        return "Passwords must contain " + string.Join(", ", rules) + ".";
    }
}

/// <summary>
/// The user's notification categories. The chosen boxes are posted as repeated
/// <c>EnabledCategories</c> values — the same no-JavaScript pattern the role picker
/// uses — so an unchecked box simply sends nothing.
/// </summary>
public sealed class NotificationSettingsViewModel
{
    public IReadOnlyList<NotificationCategoryRowViewModel> Rows { get; set; } =
        Array.Empty<NotificationCategoryRowViewModel>();

    /// <summary>
    /// Bound from the form: the categories left enabled. The binder refuses a value that is
    /// not one of the application's categories, so a hand-made post is reported rather than
    /// quietly dropped.
    /// </summary>
    public List<NotificationCategory> EnabledCategories { get; set; } = new();

    public static NotificationSettingsViewModel From(
        IEnumerable<NotificationPreferenceDto> preferences) => new()
    {
        Rows = preferences.Select(p => new NotificationCategoryRowViewModel
        {
            Category = p.Category,
            Label = p.Label,
            IsEnabled = p.IsEnabled,
            HasStoredRow = p.HasStoredRow
        }).ToList()
    };

    /// <summary>
    /// Re-renders a rejected post with the boxes exactly as the user left them, so a
    /// validation message never silently reverts their selection.
    /// </summary>
    public void ReflectPostedSelection()
    {
        var wanted = ToInput().EnabledByCategory;
        Rows = Rows
            .Select(row =>
            {
                row.IsEnabled = wanted.TryGetValue(row.Category, out var enabled) && enabled;
                return row;
            })
            .ToList();
    }

    /// <summary>
    /// Every category the application can send is given an explicit state: a category
    /// missing from the post means the user unchecked it, never "leave it alone".
    /// </summary>
    public UpdateNotificationPreferencesInput ToInput()
    {
        var enabled = EnabledCategories.Where(Enum.IsDefined).ToHashSet();

        return new UpdateNotificationPreferencesInput
        {
            EnabledByCategory = Enum.GetValues<NotificationCategory>()
                .ToDictionary(category => category, enabled.Contains)
        };
    }
}

/// <summary>One notification category row.</summary>
public sealed class NotificationCategoryRowViewModel
{
    public required NotificationCategory Category { get; init; }

    public required string Label { get; init; }

    /// <summary>Effective state; rewritten by <see cref="NotificationSettingsViewModel.ReflectPostedSelection"/> on a rejected post.</summary>
    public required bool IsEnabled { get; set; }

    /// <summary>False when the state is the default rather than a stored choice.</summary>
    public required bool HasStoredRow { get; init; }

    public string StateLabel => IsEnabled ? "Enabled" : "Disabled";

    public NetraTone StateTone => IsEnabled ? NetraTone.Success : NetraTone.Neutral;

    public string SourceLabel => HasStoredRow ? "Your choice" : "Default";
}

/// <summary>
/// Display choices. Only two fixed, server-applied options exist: row spacing and
/// whether transitions play. No CSS is stored and there is no theme framework.
/// </summary>
public sealed class AppearanceSettingsViewModel
{
    [Required(ErrorMessage = "Choose a row spacing.")]
    public UiDensity Density { get; set; } = UiDensity.Comfortable;

    public bool ReducedMotion { get; set; }

    /// <summary>Every spacing option the theme can apply.</summary>
    public static IReadOnlyList<UiDensity> DensityOptions { get; } =
        Enum.GetValues<UiDensity>();

    public static AppearanceSettingsViewModel From(AppearanceDto appearance) => new()
    {
        Density = appearance.Density,
        ReducedMotion = appearance.ReducedMotion
    };

    public UpdateAppearanceInput ToInput() => new()
    {
        Density = Density,
        ReducedMotion = ReducedMotion
    };

    public string DensityLabel => NetraDisplay.Words(Density.ToString());

    public string MotionLabel => ReducedMotion ? "Reduced" : "Full";
}

/// <summary>One stored administrative setting row, rendered read-only.</summary>
public sealed class SystemSettingsViewModel
{
    public IReadOnlyList<AdminSettingDto> Preferences { get; init; } = Array.Empty<AdminSettingDto>();

    public IReadOnlyList<AdminSettingDto> SecuritySettings { get; init; } = Array.Empty<AdminSettingDto>();

    public static SystemSettingsViewModel From(SystemSettingsDto settings) => new()
    {
        Preferences = settings.Preferences,
        SecuritySettings = settings.SecuritySettings
    };

    public bool HasAnyRows => Preferences.Count > 0 || SecuritySettings.Count > 0;
}

/// <summary>A settings section card on the overview page.</summary>
public sealed class SettingsCardViewModel
{
    public required string Title { get; init; }

    public required string Description { get; init; }

    public required string Url { get; init; }

    public required NetraIcon Icon { get; init; }

    /// <summary>Short current-state line, e.g. "Density: Compact".</summary>
    public string? Status { get; init; }

    public NetraTone StatusTone { get; init; } = NetraTone.Neutral;
}

/// <summary>The Settings landing page.</summary>
public sealed class SettingsOverviewViewModel
{
    public required IReadOnlyList<SettingsCardViewModel> Cards { get; init; }

    public required string DisplayName { get; init; }

    public required string UserName { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }
}
