using NETRA.Application.Abstractions;

namespace NETRA.Application.Users;

/// <summary>
/// Page-size bounds for the user list. The list is always server-side paged, so no
/// request can ask for the whole account table.
/// </summary>
public static class UserManagementLimits
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public const int MaxSearchLength = 100;
}

/// <summary>
/// The three states an account can present. This is a *derived view*, not a stored
/// column: Active/Inactive come from <c>ApplicationUser.IsActive</c> and Locked from
/// the Identity lockout fields, so there is exactly one source of truth per fact.
/// </summary>
public enum UserAccountStatus
{
    Active,

    /// <summary>Administratively deactivated.</summary>
    Inactive,

    /// <summary>Locked out by Identity, still an active account.</summary>
    Locked
}

/// <summary>Server-side list filter. Every value is applied as a SQL predicate.</summary>
public sealed class UserListFilterCriteria
{
    public string? Search { get; init; }

    /// <summary>Exact role name, validated against the Identity role store.</summary>
    public string? Role { get; init; }

    public UserAccountStatus? Status { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = UserManagementLimits.DefaultPageSize;
}

/// <summary>One row of the user list. Never carries credential or stamp data.</summary>
public sealed class UserListItemDto
{
    public required Guid Id { get; init; }

    public required string UserName { get; init; }

    public required string FullName { get; init; }

    public string? Email { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    public required UserAccountStatus Status { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>True for the signed-in administrator's own row, so the UI can explain
    /// why self-deactivation and self-demotion are refused.</summary>
    public bool IsCurrentActor { get; init; }
}

/// <summary>Full account view. Deliberately omits every Identity secret field.</summary>
public sealed class UserDetailDto
{
    public required Guid Id { get; init; }

    public required string UserName { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public string? Email { get; init; }

    public bool EmailConfirmed { get; init; }

    public string? PhoneNumber { get; init; }

    public string? BadgeNumber { get; init; }

    public string? Rank { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }

    public required UserAccountStatus Status { get; init; }

    public required bool IsActive { get; init; }

    /// <summary>When the Identity lockout expires, if the account is currently locked.</summary>
    public DateTimeOffset? LockoutExpiresAt { get; init; }

    public required DateTime CreatedAt { get; init; }

    public DateTime? LastLoginAt { get; init; }

    /// <summary>True when this is the signed-in administrator's own account.</summary>
    public bool IsCurrentActor { get; init; }

    /// <summary>True when this account holds the administrator role.</summary>
    public bool IsAdministrator { get; init; }
}

public sealed class CreateUserInput
{
    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string UserName { get; init; }

    public required string Email { get; init; }

    public string? BadgeNumber { get; init; }

    public string? Rank { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    /// <summary>Passed straight to <see cref="Microsoft.AspNetCore.Identity.UserManager{TUser}"/>; never stored or logged.</summary>
    public required string Password { get; init; }
}

/// <summary>
/// Profile edit. There is no user-name field: the sign-in identity is fixed at
/// creation, so no code path can re-write it by accident.
/// </summary>
public sealed class EditUserInput
{
    public required Guid UserId { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string Email { get; init; }

    public string? BadgeNumber { get; init; }

    public string? Rank { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The single account-administration entry point. Everything here goes through
/// <c>UserManager</c>/<c>RoleManager</c>: no caller touches Identity tables directly.
/// </summary>
public interface IUserManagementService
{
    Task<PagedResult<UserListItemDto>> ListAsync(
        UserListFilterCriteria criteria,
        Guid actorId,
        CancellationToken cancellationToken = default);

    Task<UserDetailDto?> GetDetailAsync(
        Guid userId, Guid actorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetAssignableRolesAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult<Guid>> CreateAsync(
        CreateUserInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<OperationResult> EditAsync(
        EditUserInput input, Guid actorId, CancellationToken cancellationToken = default);

    Task<OperationResult> SetActiveAsync(
        Guid userId, bool isActive, Guid actorId, CancellationToken cancellationToken = default);
}
