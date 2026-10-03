using System.ComponentModel.DataAnnotations;
using System.Globalization;
using NETRA.Application.Users;
using NETRA.Web.Models.Ui;

namespace NETRA.Web.Models.ViewModels.Users;

/// <summary>
/// Server-side GET filter for the user list. Every value becomes a SQL predicate in
/// <see cref="IUserManagementService"/>: there is no client-side filtering anywhere.
/// </summary>
public sealed class UserListFilterViewModel
{
    public const int MaxSearchLength = 100;

    private string? _search;

    [StringLength(MaxSearchLength, ErrorMessage = "Search must be 100 characters or fewer.")]
    public string? Search
    {
        get => _search;
        set => _search = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Exact NETRA role name, or null for all roles.</summary>
    public string? Role { get; set; }

    /// <summary>Invalid status text simply fails to bind, and the filter is dropped.</summary>
    public UserAccountStatus? Status { get; set; }

    private int _page = 1;

    /// <summary>Never below page 1, whatever the query string claims.</summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public UserListFilterCriteria ToCriteria() => new()
    {
        Search = Search,
        Role = string.IsNullOrWhiteSpace(Role) ? null : Role,
        Status = Status,
        Page = Page,
        PageSize = UserManagementLimits.DefaultPageSize
    };

    /// <summary>Route values that keep the active filters when paging.</summary>
    public Dictionary<string, string?> RouteValues() => new()
    {
        ["search"] = Search,
        ["role"] = Role,
        ["status"] = Status?.ToString(),
        ["page"] = Page.ToString(CultureInfo.InvariantCulture)
    };

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Search)
        || !string.IsNullOrWhiteSpace(Role)
        || Status is not null;

    public static IReadOnlyList<UserAccountStatus> StatusOptions { get; } =
        new[] { UserAccountStatus.Active, UserAccountStatus.Inactive, UserAccountStatus.Locked };
}

/// <summary>One row of the user table.</summary>
public sealed class UserRowViewModel
{
    public required UserListItemDto User { get; init; }

    public string StatusLabel => NetraDisplay.Words(User.Status.ToString());

    public NetraTone StatusTone => User.Status switch
    {
        UserAccountStatus.Active => NetraTone.Success,
        UserAccountStatus.Inactive => NetraTone.Neutral,
        _ => NetraTone.Danger
    };

    public string CreatedAtText => User.CreatedAt.ToString(
        "dd MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>Administratively deactivated, as opposed to locked out by Identity.</summary>
    public bool IsDeactivated => User.Status == UserAccountStatus.Inactive;

    public string EmailOrDash => string.IsNullOrWhiteSpace(User.Email) ? "—" : User.Email!;
}

public sealed class UsersIndexViewModel
{
    public required IReadOnlyList<UserRowViewModel> Users { get; init; }

    public required UserListFilterViewModel Filter { get; init; }

    public required IReadOnlyList<string> RoleOptions { get; init; }

    public required int TotalCount { get; init; }

    public required PaginationModel Pagination { get; init; }

    public bool HasActiveFilters => Filter.HasActiveFilters;

    public static UsersIndexViewModel Create(
        NETRA.Application.Abstractions.PagedResult<UserListItemDto> paged,
        UserListFilterViewModel filter,
        IReadOnlyList<string> roleOptions) => new()
    {
        Users = paged.Items.Select(item => new UserRowViewModel { User = item }).ToList(),
        Filter = filter,
        RoleOptions = roleOptions,
        TotalCount = paged.TotalCount,
        Pagination = new PaginationModel
        {
            Page = paged.Page,
            TotalPages = Math.Max(1, paged.TotalPages),
            TotalItems = paged.TotalCount,
            PageSize = paged.PageSize,
            Controller = "Users",
            Action = "Index",
            RouteValues = filter.RouteValues()
        }
    };
}

/// <summary>
/// Create-account form. It is a plain DTO: the Identity entity is never bound from the
/// form, so a crafted post cannot set a password hash, security stamp or lockout field.
/// </summary>
public sealed class CreateUserViewModel
{
    [Display(Name = "First name")]
    [Required(ErrorMessage = "First name is required.")]
    [StringLength(100, ErrorMessage = "First name must be 100 characters or fewer.")]
    public string FirstName { get; set; } = string.Empty;

    [Display(Name = "Last name")]
    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(100, ErrorMessage = "Last name must be 100 characters or fewer.")]
    public string LastName { get; set; } = string.Empty;

    [Display(Name = "Username")]
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(256, ErrorMessage = "Username must be 256 characters or fewer.")]
    public string UserName { get; set; } = string.Empty;

    [Display(Name = "Email address")]
    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256, ErrorMessage = "Email must be 256 characters or fewer.")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Badge number")]
    [StringLength(50, ErrorMessage = "Badge number must be 50 characters or fewer.")]
    public string? BadgeNumber { get; set; }

    [Display(Name = "Rank")]
    [StringLength(100, ErrorMessage = "Rank must be 100 characters or fewer.")]
    public string? Rank { get; set; }

    /// <summary>Zero selections is a validation failure, not an account with no access.</summary>
    [Display(Name = "Roles")]
    public IList<string> Roles { get; set; } = new List<string>();

    [Display(Name = "Temporary password")]
    [DataType(DataType.Password)]
    [Required(ErrorMessage = "A password is required.")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Confirm password")]
    [DataType(DataType.Password)]
    [Required(ErrorMessage = "Confirm the password.")]
    [Compare(nameof(Password), ErrorMessage = "The two passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public IReadOnlyList<string> RoleOptions { get; set; } = Array.Empty<string>();

    public CreateUserInput ToInput() => new()
    {
        FirstName = FirstName,
        LastName = LastName,
        UserName = UserName,
        Email = Email,
        BadgeNumber = BadgeNumber,
        Rank = Rank,
        Roles = Roles?.ToList() ?? new List<string>(),
        Password = Password
    };
}

/// <summary>
/// Edit-account form. There is no username or password field: the sign-in identity is
/// fixed at creation and credentials are changed only through the reset flow.
/// </summary>
public sealed class EditUserViewModel
{
    public Guid Id { get; set; }

    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [Display(Name = "First name")]
    [Required(ErrorMessage = "First name is required.")]
    [StringLength(100, ErrorMessage = "First name must be 100 characters or fewer.")]
    public string FirstName { get; set; } = string.Empty;

    [Display(Name = "Last name")]
    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(100, ErrorMessage = "Last name must be 100 characters or fewer.")]
    public string LastName { get; set; } = string.Empty;

    [Display(Name = "Email address")]
    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256, ErrorMessage = "Email must be 256 characters or fewer.")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Badge number")]
    [StringLength(50, ErrorMessage = "Badge number must be 50 characters or fewer.")]
    public string? BadgeNumber { get; set; }

    [Display(Name = "Rank")]
    [StringLength(100, ErrorMessage = "Rank must be 100 characters or fewer.")]
    public string? Rank { get; set; }

    [Display(Name = "Roles")]
    public IList<string> Roles { get; set; } = new List<string>();

    public IReadOnlyList<string> RoleOptions { get; set; } = Array.Empty<string>();

    public string StatusLabel { get; set; } = string.Empty;

    public NetraTone StatusTone { get; set; } = NetraTone.Neutral;

    /// <summary>True when this is the signed-in administrator's own account.</summary>
    public bool IsSelf { get; set; }

    public static EditUserViewModel FromDetail(UserDetailDto detail, IReadOnlyList<string> roleOptions) => new()
    {
        Id = detail.Id,
        UserName = detail.UserName,
        FirstName = detail.FirstName,
        LastName = detail.LastName,
        Email = detail.Email ?? string.Empty,
        BadgeNumber = detail.BadgeNumber,
        Rank = detail.Rank,
        Roles = detail.Roles.ToList(),
        RoleOptions = roleOptions,
        StatusLabel = NetraDisplay.Words(detail.Status.ToString()),
        StatusTone = StatusToneOf(detail.Status),
        IsSelf = detail.IsCurrentActor
    };

    public EditUserInput ToInput() => new()
    {
        UserId = Id,
        FirstName = FirstName,
        LastName = LastName,
        Email = Email,
        BadgeNumber = BadgeNumber,
        Rank = Rank,
        Roles = Roles?.ToList() ?? new List<string>()
    };

    internal static NetraTone StatusToneOf(UserAccountStatus status) => status switch
    {
        UserAccountStatus.Active => NetraTone.Success,
        UserAccountStatus.Inactive => NetraTone.Neutral,
        _ => NetraTone.Danger
    };
}

/// <summary>
/// Read-only account view. Only the fields an administrator needs are carried: Identity
/// secrets (password hash, security stamp, concurrency tokens) are never in the model.
/// </summary>
public sealed class UserDetailsViewModel
{
    public required UserDetailDto User { get; init; }

    public string FullName => $"{User.FirstName} {User.LastName}".Trim();

    public string StatusLabel => NetraDisplay.Words(User.Status.ToString());

    public NetraTone StatusTone => EditUserViewModel.StatusToneOf(User.Status);

    public string CreatedAtText => User.CreatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);

    public string? LastLoginText => User.LastLoginAt?.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);

    public string? LockoutText => User.LockoutExpiresAt?.ToString(
        "dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);

    public string EmailOrDash => OrDash(User.Email);

    public string PhoneOrDash => OrDash(User.PhoneNumber);

    public string BadgeOrDash => OrDash(User.BadgeNumber);

    public string RankOrDash => OrDash(User.Rank);

    /// <summary>The account exists but cannot sign in, either deactivated or locked out.</summary>
    public bool IsBlocked => User.Status != UserAccountStatus.Active;

    /// <summary>Administratively deactivated, so the reversible action is activation.</summary>
    public bool IsDeactivated => User.Status == UserAccountStatus.Inactive;

    public string BlockedReason => User.Status switch
    {
        UserAccountStatus.Inactive =>
            "This account is deactivated. Sign-in is refused until it is reactivated.",
        UserAccountStatus.Locked =>
            $"This account is locked out by Identity until {LockoutText} UTC. " +
            "Lockout expires automatically, or an administrator can reset it.",
        _ => string.Empty
    };

    public static UserDetailsViewModel Create(UserDetailDto detail) => new() { User = detail };

    private static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
