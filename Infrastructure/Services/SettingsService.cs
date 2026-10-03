using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.Alerts;
using NETRA.Application.Settings;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;
using NETRA.Domain.Settings;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// Settings module: the signed-in user's own profile, password, notification
/// preferences and display choices, plus a read-only view of the administrative
/// setting tables.
/// </summary>
/// <remarks>
/// Every method is scoped to the <paramref name="userId"/> the caller derived from the
/// authenticated principal; there is no parameter that lets one account write another's
/// settings. Account fields go through <see cref="UserManager{TUser}"/> so Identity keeps
/// owning normalisation and the security stamp, and display choices are stored in the
/// existing <c>UserSettings</c> table rather than a new one.
/// </remarks>
public sealed class SettingsService : ISettingsService
{
    /// <summary>Category tag the display choices share in <c>UserSettings</c>.</summary>
    public const string AppearanceSettingCategory = "Appearance";

    public const string DensitySettingKey = "ui.density";

    public const string ReducedMotionSettingKey = "ui.reduced-motion";

    /// <summary>
    /// Matches <c>AlertService</c>: notifications in this deployment are delivered
    /// in-application only, so preferences are stored against that single channel.
    /// </summary>
    public const int InApplicationDeliveryChannel = 1;

    /// <summary>Upper bound on the administrative listing, so no request can read an unbounded table.</summary>
    public const int MaxAdminSettingRows = 200;

    private const int MaxNameLength = 100;
    private const int MaxBadgeLength = 50;
    private const int MaxRankLength = 100;

    private readonly NetraDbContext _context;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;

    public SettingsService(
        NetraDbContext context,
        UserManager<ApplicationUser> users,
        IAuditService audit,
        TimeProvider time)
    {
        _context = context;
        _users = users;
        _audit = audit;
        _time = time;
    }

    public async Task<ProfileDto?> GetProfileAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return null;
        }

        var roles = await _users.GetRolesAsync(user);

        return new ProfileDto
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            BadgeNumber = user.BadgeNumber,
            Rank = user.Rank,
            Roles = roles.ToList(),
            MemberSince = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            IsActive = user.IsActive
        };
    }

    public async Task<OperationResult> UpdateProfileAsync(
        Guid userId, UpdateProfileInput input, CancellationToken cancellationToken = default)
    {
        var errors = ValidateProfile(input);
        if (errors.Count > 0)
        {
            return OperationResult.ValidationFailed(errors);
        }

        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return OperationResult.NotFound("No account with that identifier exists.");
        }

        var firstName = input.FirstName.Trim();
        var lastName = input.LastName.Trim();
        var badge = TrimToNull(input.BadgeNumber);
        var rank = TrimToNull(input.Rank);

        if (badge is not null && await BadgeTakenAsync(userId, badge, cancellationToken))
        {
            return OperationResult.Conflict(
                "BadgeNumber", "Another account already uses that badge number.");
        }

        var changes = CollectChanges(user, firstName, lastName, badge, rank);
        if (changes.Count == 0)
        {
            // Idempotent: a repeated submission writes nothing and adds no trail entry.
            return OperationResult.Success();
        }

        user.FirstName = firstName;
        user.LastName = lastName;
        user.BadgeNumber = badge;
        user.Rank = rank;

        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return OperationResult.ValidationFailed(IdentityErrors(updated));
        }

        foreach (var change in changes)
        {
            _audit.Record(
                AuditEntityTypeNames.Account,
                user.Id,
                AuditActionNames.UserEdited,
                user.Id,
                propertyName: change.Property,
                oldValue: change.OldValue,
                newValue: change.NewValue);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<SecurityStatusDto?> GetSecurityStatusAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return null;
        }

        var policy = _users.Options.Password;
        var isLockedOut = await _users.IsLockedOutAsync(user);

        return new SecurityStatusDto
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email,
            EmailConfirmed = user.EmailConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled,
            IsLockedOut = isLockedOut,
            LockoutExpiresAt = isLockedOut ? user.LockoutEnd : null,
            LastLoginAt = user.LastLoginAt,
            HasPassword = await _users.HasPasswordAsync(user),
            PasswordPolicy = new PasswordPolicyDto
            {
                RequiredLength = policy.RequiredLength,
                RequireDigit = policy.RequireDigit,
                RequireUppercase = policy.RequireUppercase,
                RequireLowercase = policy.RequireLowercase,
                RequireNonAlphanumeric = policy.RequireNonAlphanumeric
            }
        };
    }

    public async Task<OperationResult> ChangePasswordAsync(
        Guid userId, ChangePasswordInput input, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrEmpty(input.CurrentPassword))
        {
            errors["CurrentPassword"] = ["Enter your current password."];
        }

        if (string.IsNullOrEmpty(input.NewPassword))
        {
            errors["NewPassword"] = ["Enter a new password."];
        }
        else if (input.NewPassword != input.ConfirmPassword)
        {
            errors["ConfirmPassword"] = ["The two passwords do not match."];
        }

        if (errors.Count > 0)
        {
            return OperationResult.ValidationFailed(errors);
        }

        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return OperationResult.NotFound("No account with that identifier exists.");
        }

        // Identity owns the password rules, the hashing and the current-password check.
        // Nothing on this path logs or echoes a password.
        var changed = await _users.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword!);
        if (!changed.Succeeded)
        {
            return OperationResult.ValidationFailed(PasswordErrors(changed));
        }

        _audit.RecordSecurityEvent(
            AuditActionNames.PasswordChanged, user.Id, detail: "Password changed by the account owner.");

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<IReadOnlyList<NotificationPreferenceDto>> GetNotificationPreferencesAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await _context.NotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == userId && p.DeliveryChannel == InApplicationDeliveryChannel)
            .ToListAsync(cancellationToken);

        // Enum.GetValues keeps the page aligned with the categories the application can
        // actually send; nothing here invents a new one.
        return Enum.GetValues<NotificationCategory>()
            .Select(category =>
            {
                var row = rows.FirstOrDefault(r => r.NotificationType == (int)category);
                return new NotificationPreferenceDto
                {
                    Category = category,
                    Label = NotificationCategories.Label(category),
                    // No row means the default-enabled state AlertService applies.
                    IsEnabled = row?.IsEnabled ?? true,
                    HasStoredRow = row is not null
                };
            })
            .ToList();
    }

    public async Task<OperationResult> UpdateNotificationPreferencesAsync(
        Guid userId,
        UpdateNotificationPreferencesInput input,
        CancellationToken cancellationToken = default)
    {
        var unknown = input.EnabledByCategory.Keys
            .Where(c => !Enum.IsDefined(c))
            .Select(c => c.ToString())
            .ToList();

        if (unknown.Count > 0)
        {
            // Keyed by the posted checkbox group's field name, so the message renders under
            // the boxes the user actually chose, exactly as the other settings failures do.
            return OperationResult.ValidationFailed(new Dictionary<string, string[]>
            {
                ["EnabledCategories"] =
                    [$"Unknown notification category: {string.Join(", ", unknown)}."]
            });
        }

        if (userId == Guid.Empty)
        {
            return OperationResult.Forbidden();
        }

        var rows = await _context.NotificationPreferences
            .Where(p => p.UserId == userId && p.DeliveryChannel == InApplicationDeliveryChannel)
            .ToListAsync(cancellationToken);

        var now = _time.GetUtcNow().UtcDateTime;
        var changedLabels = new List<string>();

        foreach (var (category, enabled) in input.EnabledByCategory
                     .OrderBy(pair => (int)pair.Key)
                     .ToList())
        {
            var row = rows.FirstOrDefault(r => r.NotificationType == (int)category);
            var current = row?.IsEnabled ?? true;
            if (current == enabled)
            {
                continue;
            }

            if (row is null)
            {
                // An explicit "off" needs a row; an explicit "on" is the default already,
                // so only the disabling choice is persisted when no row exists yet.
                if (!enabled)
                {
                    _context.NotificationPreferences.Add(new NotificationPreference
                    {
                        UserId = userId,
                        NotificationType = (int)category,
                        DeliveryChannel = InApplicationDeliveryChannel,
                        IsEnabled = false,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }
            else
            {
                row.IsEnabled = enabled;
                row.UpdatedAt = now;
            }

            _audit.Record(
                AuditEntityTypeNames.Account,
                userId,
                AuditActionNames.NotificationPreferencesUpdated,
                userId,
                propertyName: $"Notifications.{category}",
                oldValue: DescribeEnabled(current),
                newValue: DescribeEnabled(enabled));

            changedLabels.Add(NotificationCategories.Label(category));
        }

        if (changedLabels.Count == 0)
        {
            return OperationResult.Success();
        }

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<AppearanceDto> GetAppearanceAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return new AppearanceDto
            {
                Density = UiDensity.Comfortable,
                ReducedMotion = false
            };
        }

        var rows = await _context.UserSettings.AsNoTracking()
            .Where(s => s.UserId == userId
                        && s.Category == AppearanceSettingCategory
                        && (s.Key == DensitySettingKey || s.Key == ReducedMotionSettingKey))
            .ToListAsync(cancellationToken);

        return ReadAppearance(rows);
    }

    public async Task<OperationResult> UpdateAppearanceAsync(
        Guid userId, UpdateAppearanceInput input, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return OperationResult.Forbidden();
        }

        if (!Enum.IsDefined(input.Density))
        {
            return OperationResult.ValidationFailed(new Dictionary<string, string[]>
            {
                ["Density"] = ["Choose either Comfortable or Compact."]
            });
        }

        var current = await GetAppearanceAsync(userId, cancellationToken);
        if (current.Density == input.Density && current.ReducedMotion == input.ReducedMotion)
        {
            return OperationResult.Success();
        }

        var rows = await _context.UserSettings
            .Where(s => s.UserId == userId
                        && s.Category == AppearanceSettingCategory
                        && (s.Key == DensitySettingKey || s.Key == ReducedMotionSettingKey))
            .ToListAsync(cancellationToken);

        var now = _time.GetUtcNow().UtcDateTime;

        if (current.Density != input.Density)
        {
            Upsert(rows, DensitySettingKey, input.Density.ToString(), now, userId);
            _audit.Record(
                AuditEntityTypeNames.Account, userId,
                AuditActionNames.AppearancePreferencesUpdated, userId,
                propertyName: "Density",
                oldValue: current.Density.ToString(),
                newValue: input.Density.ToString());
        }

        if (current.ReducedMotion != input.ReducedMotion)
        {
            Upsert(rows, ReducedMotionSettingKey,
                input.ReducedMotion ? bool.TrueString : bool.FalseString, now, userId);
            _audit.Record(
                AuditEntityTypeNames.Account, userId,
                AuditActionNames.AppearancePreferencesUpdated, userId,
                propertyName: "ReducedMotion",
                oldValue: DescribeEnabled(current.ReducedMotion),
                newValue: DescribeEnabled(input.ReducedMotion));
        }

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<SystemSettingsDto> GetSystemSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        // Read-only by design: these tables are not seeded by the application and the
        // deployment configuration that actually drives the runtime (connection strings,
        // provider endpoints) is deliberately not read or rendered here.
        var preferences = await _context.SystemPreferences.AsNoTracking()
            .OrderBy(p => p.Key)
            .Take(MaxAdminSettingRows)
            .Select(p => new
            {
                p.Key,
                p.Value,
                p.Category,
                p.Description,
                p.UpdatedAt,
                p.IsReadOnly,
                UpdatedByName = p.UpdatedBy!.UserName
            })
            .ToListAsync(cancellationToken);

        var security = await _context.SecuritySettings.AsNoTracking()
            .OrderBy(s => s.Key)
            .Take(MaxAdminSettingRows)
            .Select(s => new
            {
                s.Key,
                s.Value,
                s.Category,
                s.Description,
                s.UpdatedAt,
                s.IsSystemSetting,
                UpdatedByName = s.UpdatedBy!.UserName
            })
            .ToListAsync(cancellationToken);

        return new SystemSettingsDto
        {
            Preferences = preferences.Select(p => new AdminSettingDto
            {
                Key = p.Key,
                Value = p.Value,
                Category = p.Category,
                Description = p.Description,
                UpdatedAt = p.UpdatedAt,
                UpdatedBy = p.UpdatedByName,
                IsSystemOwned = p.IsReadOnly
            }).ToList(),

            SecuritySettings = security.Select(s => new AdminSettingDto
            {
                Key = s.Key,
                Value = s.Value,
                Category = s.Category,
                Description = s.Description,
                UpdatedAt = s.UpdatedAt,
                UpdatedBy = s.UpdatedByName,
                IsSystemOwned = s.IsSystemSetting
            }).ToList()
        };
    }

    private static AppearanceDto ReadAppearance(IEnumerable<UserSetting> rows)
    {
        var densityValue = rows.FirstOrDefault(r => r.Key == DensitySettingKey)?.Value;
        var motionValue = rows.FirstOrDefault(r => r.Key == ReducedMotionSettingKey)?.Value;

        // Anything unparseable falls back to the theme default rather than being trusted.
        var density = Enum.TryParse<UiDensity>(densityValue, ignoreCase: true, out var parsed)
                      && Enum.IsDefined(parsed)
            ? parsed
            : UiDensity.Comfortable;

        return new AppearanceDto
        {
            Density = density,
            ReducedMotion = bool.TryParse(motionValue, out var motion) && motion
        };
    }

    private void Upsert(
        List<UserSetting> rows, string key, string value, DateTime now, Guid userId)
    {
        var row = rows.FirstOrDefault(r => r.Key == key);
        if (row is null)
        {
            _context.UserSettings.Add(new UserSetting
            {
                UserId = userId,
                Key = key,
                Value = value,
                Category = AppearanceSettingCategory,
                Description = "Display choice applied by the server-rendered theme.",
                CreatedAt = now,
                UpdatedAt = now
            });
            return;
        }

        row.Value = value;
        row.UpdatedAt = now;
    }

    private static string DescribeEnabled(bool enabled) => enabled ? "Enabled" : "Disabled";

    private async Task<bool> BadgeTakenAsync(Guid userId, string badge, CancellationToken cancellationToken) =>
        await _context.Users.AsNoTracking()
            .AnyAsync(u => u.Id != userId && u.BadgeNumber == badge, cancellationToken);

    private static IReadOnlyDictionary<string, string[]> ValidateProfile(UpdateProfileInput input)
    {
        var errors = new Dictionary<string, string[]>();

        void Require(string field, string? value, string label, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors[field] = [$"{label} is required."];
            }
            else if (value.Trim().Length > maxLength)
            {
                errors[field] = [$"{label} must be {maxLength} characters or fewer."];
            }
        }

        Require(nameof(input.FirstName), input.FirstName, "First name", MaxNameLength);
        Require(nameof(input.LastName), input.LastName, "Last name", MaxNameLength);

        var badge = TrimToNull(input.BadgeNumber);
        if (badge is not null && badge.Length > MaxBadgeLength)
        {
            errors[nameof(input.BadgeNumber)] =
                [$"Badge number must be {MaxBadgeLength} characters or fewer."];
        }

        var rank = TrimToNull(input.Rank);
        if (rank is not null && rank.Length > MaxRankLength)
        {
            errors[nameof(input.Rank)] = [$"Rank must be {MaxRankLength} characters or fewer."];
        }

        return errors;
    }

    private static List<(string Property, string? OldValue, string? NewValue)> CollectChanges(
        ApplicationUser user, string firstName, string lastName, string? badge, string? rank)
    {
        var changes = new List<(string Property, string? OldValue, string? NewValue)>();

        void Diff(string property, string? before, string? after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                changes.Add((property, before, after));
            }
        }

        Diff(nameof(user.FirstName), user.FirstName, firstName);
        Diff(nameof(user.LastName), user.LastName, lastName);
        Diff(nameof(user.BadgeNumber), user.BadgeNumber, badge);
        Diff(nameof(user.Rank), user.Rank, rank);

        return changes;
    }

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, string[]> IdentityErrors(IdentityResult result) =>
        result.Errors
            .GroupBy(e => e.Code, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(e => e.Description).ToArray(),
                StringComparer.Ordinal);

    /// <summary>
    /// Maps Identity's password failure codes onto the form fields so each reason lands
    /// under the box it refers to. A wrong current password is reported against the
    /// current-password field; the message itself comes from Identity, never a value.
    /// </summary>
    private static Dictionary<string, string[]> PasswordErrors(IdentityResult result) =>
        result.Errors
            .Select(e => (Field: FieldFor(e.Code), Message: e.Description))
            .GroupBy(e => e.Field, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(e => e.Message).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

    private static string FieldFor(string errorCode) => errorCode switch
    {
        "PasswordMismatch" or "InvalidPassword" or "UserNotFound" => "CurrentPassword",
        _ when errorCode.StartsWith("Password", StringComparison.Ordinal) => "NewPassword",
        _ => string.Empty
    };
}
