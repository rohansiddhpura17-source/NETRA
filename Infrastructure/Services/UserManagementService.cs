using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.Users;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// Account administration for the User Management module. Every write goes through
/// <see cref="UserManager{TUser}"/> or <see cref="RoleManager{TRole}"/>: no raw SQL
/// and no direct edits of Identity tables, so the security stamp, value normalisation
/// and concurrency rules Identity owns stay intact.
/// </summary>
public sealed class UserManagementService : IUserManagementService
{
    /// <summary>
    /// The role whose loss would lock the installation out of its own administration.
    /// Mirrors <c>NetraRoles.Administrators</c>; it is the only role name this service
    /// knows, and administrative rules are enforced here rather than in controllers.
    /// </summary>
    public const string AdministratorRoleName = "System Administrator";

    private const int MaxNameLength = 100;
    private const int MaxUserNameLength = 256;
    private const int MaxEmailLength = 256;
    private const int MaxBadgeLength = 50;
    private const int MaxRankLength = 100;

    private readonly NetraDbContext _context;
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole<Guid>> _roles;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;

    public UserManagementService(
        NetraDbContext context,
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        IAuditService audit,
        TimeProvider time)
    {
        _context = context;
        _users = users;
        _roles = roles;
        _audit = audit;
        _time = time;
    }

    public async Task<PagedResult<UserListItemDto>> ListAsync(
        UserListFilterCriteria criteria,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, criteria.Page);
        var pageSize = Math.Clamp(criteria.PageSize, 1, UserManagementLimits.MaxPageSize);
        var now = _time.GetUtcNow();

        var query = _context.Users.AsNoTracking();

        // Search runs on the columns the Identity schema provides plus the NETRA profile
        // fields. FullName is a computed getter, so it is not a searchable column.
        var search = criteria.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var normalized = search.ToUpperInvariant();
            query = query.Where(u =>
                u.NormalizedUserName!.Contains(normalized)
                || (u.NormalizedEmail != null && u.NormalizedEmail.Contains(normalized))
                || u.FirstName.Contains(search)
                || u.LastName.Contains(search)
                || (u.BadgeNumber != null && u.BadgeNumber.Contains(search))
                || (u.PhoneNumber != null && u.PhoneNumber.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Role))
        {
            var roleId = await _context.Roles
                .Where(r => r.Name == criteria.Role)
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            query = roleId == Guid.Empty
                ? query.Where(u => false)
                : query.Where(u => _context.UserRoles
                    .Any(ur => ur.UserId == u.Id && ur.RoleId == roleId));
        }

        query = criteria.Status switch
        {
            UserAccountStatus.Inactive => query.Where(u => !u.IsActive),
            UserAccountStatus.Locked => query.Where(u =>
                u.IsActive && u.LockoutEnabled && u.LockoutEnd > now),
            UserAccountStatus.Active => query.Where(u =>
                u.IsActive && (!u.LockoutEnabled || u.LockoutEnd == null || u.LockoutEnd <= now)),
            _ => query
        };

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(u => u.NormalizedUserName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.FirstName,
                u.LastName,
                u.Email,
                u.IsActive,
                u.LockoutEnabled,
                u.LockoutEnd,
                u.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var roleNamesByUser = await LoadRolesAsync(
            rows.Select(r => r.Id).ToList(), cancellationToken);

        var items = rows.Select(r => new UserListItemDto
        {
            Id = r.Id,
            UserName = r.UserName ?? string.Empty,
            FullName = DisplayName(r.FirstName, r.LastName),
            Email = r.Email,
            Roles = roleNamesByUser.TryGetValue(r.Id, out var names)
                ? names
                : Array.Empty<string>(),
            Status = ResolveStatus(r.IsActive, r.LockoutEnabled, r.LockoutEnd, now),
            CreatedAt = r.CreatedAt,
            IsCurrentActor = r.Id == actorId
        }).ToList();

        return new PagedResult<UserListItemDto>(items, total, page, pageSize);
    }

    public async Task<UserDetailDto?> GetDetailAsync(
        Guid userId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();

        var row = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.FirstName,
                u.LastName,
                u.Email,
                u.EmailConfirmed,
                u.PhoneNumber,
                u.BadgeNumber,
                u.Rank,
                u.IsActive,
                u.LockoutEnabled,
                u.LockoutEnd,
                u.CreatedAt,
                u.LastLoginAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var roleNamesByUser = await LoadRolesAsync([row.Id], cancellationToken);
        var roles = roleNamesByUser.TryGetValue(row.Id, out var names)
            ? names
            : Array.Empty<string>();

        return new UserDetailDto
        {
            Id = row.Id,
            UserName = row.UserName ?? string.Empty,
            FirstName = row.FirstName,
            LastName = row.LastName,
            Email = row.Email,
            EmailConfirmed = row.EmailConfirmed,
            PhoneNumber = row.PhoneNumber,
            BadgeNumber = row.BadgeNumber,
            Rank = row.Rank,
            Roles = roles,
            Status = ResolveStatus(row.IsActive, row.LockoutEnabled, row.LockoutEnd, now),
            IsActive = row.IsActive,
            LockoutExpiresAt = row.LockoutEnabled && row.LockoutEnd > now ? row.LockoutEnd : null,
            CreatedAt = row.CreatedAt,
            LastLoginAt = row.LastLoginAt,
            IsCurrentActor = row.Id == actorId,
            IsAdministrator = roles.Contains(AdministratorRoleName, StringComparer.Ordinal)
        };
    }

    public async Task<IReadOnlyList<string>> GetAssignableRolesAsync(
        CancellationToken cancellationToken = default)
    {
        // The Identity role store is the source of truth: the seeded NETRA roles are
        // exactly what an administrator may assign, and nothing else can be invented here.
        return await _context.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => r.Name!)
            .ToListAsync(cancellationToken);
    }

    public async Task<OperationResult<Guid>> CreateAsync(
        CreateUserInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        var errors = ValidateProfile(
            input.FirstName, input.LastName, input.UserName, input.Email,
            input.BadgeNumber, input.Rank, requireUserName: true);

        var roleError = await ValidateRolesAsync(input.Roles);
        if (roleError is not null)
        {
            errors["Roles"] = [roleError];
        }

        if (errors.Count > 0)
        {
            return OperationResult<Guid>.ValidationFailed(errors);
        }

        var firstName = input.FirstName.Trim();
        var lastName = input.LastName.Trim();
        var userName = input.UserName.Trim();
        var email = input.Email.Trim();
        var badge = TrimToNull(input.BadgeNumber);

        if (await _users.FindByNameAsync(userName) is not null)
        {
            return OperationResult<Guid>.Conflict(
                "UserName", "That username is already taken.");
        }

        if (await _users.FindByEmailAsync(email) is not null)
        {
            return OperationResult<Guid>.Conflict(
                "Email", "That email address is already registered.");
        }

        if (badge is not null && await BadgeTakenAsync(badge, exceptUserId: null, cancellationToken))
        {
            return OperationResult<Guid>.Conflict(
                "BadgeNumber", "Another account already uses that badge number.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            UserName = userName,
            Email = email,
            // No email delivery provider exists in this deployment, so confirmation is
            // granted administratively at creation instead of leaving the account unusable.
            EmailConfirmed = true,
            BadgeNumber = badge,
            Rank = TrimToNull(input.Rank),
            IsActive = true,
            CreatedAt = _time.GetUtcNow().UtcDateTime
        };

        var created = await _users.CreateAsync(user, input.Password);
        if (!created.Succeeded)
        {
            return OperationResult<Guid>.ValidationFailed(IdentityErrors(created));
        }

        foreach (var role in input.Roles)
        {
            var added = await _users.AddToRoleAsync(user, role);
            if (!added.Succeeded)
            {
                return OperationResult<Guid>.ValidationFailed(IdentityErrors(added));
            }
        }

        // The password is never written to the trail — only the fact of the creation and
        // the roles granted.
        _audit.Record(
            AuditEntityTypeNames.Account,
            user.Id,
            AuditActionNames.UserCreated,
            actorId,
            propertyName: "Roles",
            newValue: DescribeRoles(input.Roles));

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<Guid>.Success(user.Id);
    }

    public async Task<OperationResult> EditAsync(
        EditUserInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        var errors = ValidateProfile(
            input.FirstName, input.LastName, userName: null, input.Email,
            input.BadgeNumber, input.Rank, requireUserName: false);

        var roleError = await ValidateRolesAsync(input.Roles);
        if (roleError is not null)
        {
            errors["Roles"] = [roleError];
        }

        if (errors.Count > 0)
        {
            return OperationResult.ValidationFailed(errors);
        }

        var user = await _users.FindByIdAsync(input.UserId.ToString());
        if (user is null)
        {
            return OperationResult.NotFound("No account with that identifier exists.");
        }

        var firstName = input.FirstName.Trim();
        var lastName = input.LastName.Trim();
        var email = input.Email.Trim();
        var badge = TrimToNull(input.BadgeNumber);
        var rank = TrimToNull(input.Rank);

        var existingByEmail = await _users.FindByEmailAsync(email);
        if (existingByEmail is not null && existingByEmail.Id != user.Id)
        {
            return OperationResult.Conflict(
                "Email", "Another account already uses that email address.");
        }

        if (badge is not null && await BadgeTakenAsync(badge, user.Id, cancellationToken))
        {
            return OperationResult.Conflict(
                "BadgeNumber", "Another account already uses that badge number.");
        }

        var currentRoles = (await _users.GetRolesAsync(user)).ToList();
        var requestedRoles = input.Roles.Distinct(StringComparer.Ordinal).ToList();
        var removedRoles = currentRoles
            .Where(r => !requestedRoles.Contains(r, StringComparer.Ordinal))
            .ToList();

        if (removedRoles.Contains(AdministratorRoleName, StringComparer.Ordinal))
        {
            if (user.Id == actorId)
            {
                return await RefuseAsync(user.Id, actorId, "Roles",
                    "You cannot remove the administrator role from your own account.",
                    cancellationToken);
            }

            if (await IsLastAdministratorAsync(user, cancellationToken))
            {
                return await RefuseAsync(user.Id, actorId, "Roles",
                    "This is the last administrator account. Grant the administrator role to " +
                    "another account before removing it here.",
                    cancellationToken);
            }
        }

        var changes = CollectChanges(user, firstName, lastName, email, badge, rank);
        var roleDiffers = !requestedRoles.ToHashSet(StringComparer.Ordinal)
            .SetEquals(currentRoles);

        if (changes.Count == 0 && !roleDiffers)
        {
            return OperationResult.Success();
        }

        user.FirstName = firstName;
        user.LastName = lastName;
        user.BadgeNumber = badge;
        user.Rank = rank;

        if (changes.Count > 0)
        {
            var updated = await _users.UpdateAsync(user);
            if (!updated.Succeeded)
            {
                return OperationResult.ValidationFailed(IdentityErrors(updated));
            }

            _audit.Record(
                AuditEntityTypeNames.Account,
                user.Id,
                AuditActionNames.UserEdited,
                actorId,
                oldValue: string.Join(" | ", changes.Select(c => c.Old)),
                newValue: string.Join(" | ", changes.Select(c => c.New)));
        }

        if (!string.Equals(user.Email, email, StringComparison.Ordinal))
        {
            // SetEmailAsync (not a direct assignment) is what re-normalises the value,
            // so FindByEmailAsync keeps working after an edit.
            var emailed = await _users.SetEmailAsync(user, email);
            if (!emailed.Succeeded)
            {
                return OperationResult.ValidationFailed(IdentityErrors(emailed));
            }
        }

        if (roleDiffers)
        {
            await ApplyRoleChangesAsync(user, requestedRoles, currentRoles, actorId);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetActiveAsync(
        Guid userId, bool isActive, Guid actorId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return OperationResult.NotFound("No account with that identifier exists.");
        }

        if (user.IsActive == isActive)
        {
            // Idempotent: a repeated form submission must not write a second trail entry.
            return OperationResult.Success();
        }

        if (!isActive)
        {
            if (user.Id == actorId)
            {
                return await RefuseAsync(userId, actorId, "IsActive",
                    "You cannot deactivate your own account while signed in.",
                    cancellationToken);
            }

            if (await IsLastAdministratorAsync(user, cancellationToken))
            {
                return await RefuseAsync(userId, actorId, "IsActive",
                    "This is the last administrator account. Grant the administrator role to " +
                    "another account before deactivating it.",
                    cancellationToken);
            }
        }

        user.IsActive = isActive;

        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return OperationResult.ValidationFailed(IdentityErrors(updated));
        }

        if (!isActive)
        {
            // Invalidates this account's existing authentication cookies when the browser
            // next revalidates them. Identity revalidates the security stamp at most every
            // 30 minutes by default, so an already-signed-in session can outlive the change
            // briefly; the next sign-in attempt is refused outright.
            await _users.UpdateSecurityStampAsync(user);
        }

        _audit.Record(
            AuditEntityTypeNames.Account,
            userId,
            isActive ? AuditActionNames.UserActivated : AuditActionNames.UserDeactivated,
            actorId,
            propertyName: "IsActive",
            oldValue: (!isActive).ToString(),
            newValue: isActive.ToString());

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Records a refused administrative change and returns the message for the operator.
    /// The refusal itself is audited: an attempt to strip or disable the last
    /// administrator is a security-relevant event even when it fails.
    /// </summary>
    private async Task<OperationResult> RefuseAsync(
        Guid targetUserId,
        Guid actorId,
        string propertyName,
        string message,
        CancellationToken cancellationToken)
    {
        _audit.Record(
            AuditEntityTypeNames.Account,
            targetUserId,
            AuditActionNames.UserAccessDenied,
            actorId,
            propertyName: propertyName,
            newValue: message);

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult.Forbidden(message);
    }

    private async Task ApplyRoleChangesAsync(
        ApplicationUser user,
        IReadOnlyList<string> requested,
        IReadOnlyList<string> current,
        Guid actorId)
    {
        var toAdd = requested.Where(r => !current.Contains(r, StringComparer.Ordinal)).ToList();
        var toRemove = current.Where(r => !requested.Contains(r, StringComparer.Ordinal)).ToList();

        if (toRemove.Count > 0)
        {
            await _users.RemoveFromRolesAsync(user, toRemove);
        }

        if (toAdd.Count > 0)
        {
            await _users.AddToRolesAsync(user, toAdd);
        }

        _audit.Record(
            AuditEntityTypeNames.Account,
            user.Id,
            AuditActionNames.UserRoleChanged,
            actorId,
            propertyName: "Roles",
            oldValue: DescribeRoles(current),
            newValue: DescribeRoles(requested));
    }

    /// <summary>True when no other account would retain the administrator role.</summary>
    private async Task<bool> IsLastAdministratorAsync(
        ApplicationUser user, CancellationToken cancellationToken)
    {
        if (!await _users.IsInRoleAsync(user, AdministratorRoleName))
        {
            return false;
        }

        var adminRoleId = await _context.Roles
            .Where(r => r.Name == AdministratorRoleName)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (adminRoleId == Guid.Empty)
        {
            return false;
        }

        var holders = await _context.UserRoles
            .Where(ur => ur.RoleId == adminRoleId && ur.UserId != user.Id)
            .Select(ur => ur.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

        return holders == 0;
    }

    private async Task<Dictionary<Guid, IReadOnlyList<string>>> LoadRolesAsync(
        IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        var rows = await _context.UserRoles
            .AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(
                _context.Roles.AsNoTracking(),
                ur => ur.RoleId,
                role => role.Id,
                (ur, role) => new { ur.UserId, RoleName = role.Name })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => r.RoleName != null)
            .GroupBy(r => r.UserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g
                    .Select(x => x.RoleName!)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList());
    }

    private async Task<string?> ValidateRolesAsync(IReadOnlyList<string> roles)
    {
        if (roles.Count == 0)
        {
            return "Select at least one role.";
        }

        var unknown = new List<string>();
        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            // RoleExistsAsync is the RoleManager's own lookup, so a role can only be
            // assigned if the Identity role store actually holds it.
            if (string.IsNullOrWhiteSpace(role) || !await _roles.RoleExistsAsync(role))
            {
                unknown.Add(role);
            }
        }

        return unknown.Count == 0
            ? null
            : $"Unknown role{(unknown.Count == 1 ? "" : "s")}: {string.Join(", ", unknown)}.";
    }

    /// <summary>Field-by-field "Name: old -> new" pairs, for the audit payload.</summary>
    private sealed record FieldChange(string Field, string? OldValue, string? NewValue)
    {
        public string Old => $@"{Field}: ""{Show(OldValue)}""";

        public string New => $@"{Field}: ""{Show(NewValue)}""";

        private static string Show(string? value) => string.IsNullOrEmpty(value) ? "(none)" : value;
    }

    private static List<FieldChange> CollectChanges(
        ApplicationUser user, string firstName, string lastName, string email,
        string? badge, string? rank)
    {
        var changes = new List<FieldChange>();

        void Add(string field, string? from, string? to)
        {
            if (!string.Equals(from, to, StringComparison.Ordinal))
            {
                changes.Add(new FieldChange(field, from, to));
            }
        }

        Add("First name", user.FirstName, firstName);
        Add("Last name", user.LastName, lastName);
        Add("Email", user.Email, email);
        Add("Badge number", user.BadgeNumber, badge);
        Add("Rank", user.Rank, rank);

        return changes;
    }

    private async Task<bool> BadgeTakenAsync(
        string badge, Guid? exceptUserId, CancellationToken cancellationToken)
    {
        // BadgeNumber has a filtered unique index; the pre-check reports the conflict
        // as a validation error instead of letting the insert fail.
        return await _context.Users.AnyAsync(
            u => u.BadgeNumber == badge && (exceptUserId == null || u.Id != exceptUserId),
            cancellationToken);
    }

    private static Dictionary<string, string[]> ValidateProfile(
        string? firstName, string? lastName, string? userName, string? email,
        string? badge, string? rank, bool requireUserName)
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

        Require("FirstName", firstName, "First name", MaxNameLength);
        Require("LastName", lastName, "Last name", MaxNameLength);
        Require("Email", email, "Email", MaxEmailLength);

        if (requireUserName)
        {
            Require("UserName", userName, "Username", MaxUserNameLength);
        }

        if (badge is not null && badge.Trim().Length > MaxBadgeLength)
        {
            errors["BadgeNumber"] =
                [$"Badge number must be {MaxBadgeLength} characters or fewer."];
        }

        if (rank is not null && rank.Trim().Length > MaxRankLength)
        {
            errors["Rank"] = [$"Rank must be {MaxRankLength} characters or fewer."];
        }

        return errors;
    }

    private static IReadOnlyDictionary<string, string[]> IdentityErrors(IdentityResult result) =>
        result.Errors
            .Select(e => (Field: FieldFor(e.Code), Message: e.Description))
            .GroupBy(e => e.Field)
            .ToDictionary(
                g => g.Key,
                g => (string[])g.Select(e => e.Message).Distinct().ToArray());

    private static string FieldFor(string errorCode) => errorCode switch
    {
        "DuplicateUserName" or "DuplicateName" => "UserName",
        "DuplicateEmail" => "Email",
        _ when errorCode.StartsWith("Password", StringComparison.Ordinal) => "Password",
        _ => string.Empty
    };

    private static UserAccountStatus ResolveStatus(
        bool isActive, bool lockoutEnabled, DateTimeOffset? lockoutEnd, DateTimeOffset now)
    {
        if (!isActive)
        {
            return UserAccountStatus.Inactive;
        }

        return lockoutEnabled && lockoutEnd > now
            ? UserAccountStatus.Locked
            : UserAccountStatus.Active;
    }

    private static string DisplayName(string firstName, string lastName) =>
        $"{firstName} {lastName}".Trim();

    private static string DescribeRoles(IReadOnlyList<string> roles) =>
        roles.Count == 0
            ? "(no roles)"
            : string.Join(", ", roles.OrderBy(r => r, StringComparer.Ordinal));

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
