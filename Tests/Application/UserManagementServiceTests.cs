using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Application.Abstractions;
using NETRA.Application.Users;
using NETRA.Domain.Audit;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Application;

/// <summary>
/// Service-level tests for <see cref="UserManagementService"/>: server-side listing, search,
/// role and status filters, page bounds, creation and its duplicate/role/password rejections,
/// profile edits, role changes, activation, the administrator self-protection rules and the
/// audit entries each of them writes.
/// </summary>
/// <remarks>
/// These run on EF Core's in-memory provider with a real <see cref="UserManager{TUser}"/>,
/// <see cref="RoleManager{TRole}"/> and <see cref="AuditService"/> built through the same DI
/// wiring the application uses, so account state and role membership are exercised through
/// Identity itself rather than a stub. They are not live SQL Server tests, and the in-memory
/// provider does not enforce the filtered unique index on <c>BadgeNumber</c>, which is why the
/// service pre-checks it in code.
/// </remarks>
public sealed class UserManagementServiceTests : IDisposable
{
    private const string ValidPassword = "ValidPassw0rd!";
    private const string Admin = "System Administrator";
    private const string Analyst = "Police / Intelligence Analyst";
    private const string Investigator = "Investigator";
    private const string Senior = "Senior Officer";

    private static readonly DateTimeOffset FixedInstant =
        new(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly NetraDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly Guid _actorId;

    public UserManagementServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<NetraDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddLogging();
        services.AddIdentityCore<ApplicationUser>(IdentitySettings)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<NetraDbContext>();

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _roleManager = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var role in new[] { Admin, Analyst, Investigator, Senior })
        {
            _roleManager.CreateAsync(new IdentityRole<Guid> { Name = role })
                .GetAwaiter().GetResult();
        }

        _actorId = CreateUserAsync("admin.one@netra.local", "Rohit", "Sharma", Admin)
            .GetAwaiter().GetResult().Id;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    // ------------------------------------------------------------------------ test doubles

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedInstant;
    }

    private sealed class StubActorAccessor(Guid id) : IAuditActorAccessor
    {
        public Guid GetUserId() => id;

        public Guid GetUserId(System.Security.Claims.ClaimsPrincipal? principal) => id;
    }

    /// <summary>
    /// Mirrors the password policy registered in <c>Program</c>, so a rejection here is the
    /// rejection an operator would see.
    /// </summary>
    private static void IdentitySettings(IdentityOptions options)
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
    }

    private UserManagementService CreateService() => CreateServiceAs(_actorId);

    private UserManagementService CreateServiceAs(Guid actorId) => new(
        _context,
        _userManager,
        _roleManager,
        new AuditService(
            _context,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new StubActorAccessor(actorId)),
        new FixedTimeProvider());

    // ------------------------------------------------------------------------------ fixtures

    private async Task<ApplicationUser> CreateUserAsync(
        string userName, string firstName, string lastName, params string[] roles)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = userName,
            FirstName = firstName,
            LastName = lastName,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = FixedInstant.UtcDateTime
        };

        (await _userManager.CreateAsync(user, ValidPassword)).Succeeded.Should().BeTrue();

        if (roles.Length > 0)
        {
            await _userManager.AddToRolesAsync(user, roles);
        }

        return user;
    }

    private static CreateUserInput NewUser(
        string userName,
        string email = "",
        string badge = "",
        IReadOnlyList<string>? roles = null) => new()
    {
        FirstName = "New",
        LastName = "Officer",
        UserName = userName,
        Email = string.IsNullOrEmpty(email) ? $"{userName}@netra.local" : email,
        BadgeNumber = string.IsNullOrEmpty(badge) ? null : badge,
        Rank = "Constable",
        Roles = roles ?? [Investigator],
        Password = ValidPassword
    };

    private async Task<List<AuditLog>> TrailAsync(Guid entityId) =>
        await _context.AuditLogs
            .AsNoTracking()
            .Where(entry => entry.EntityId == entityId)
            .ToListAsync();

    // ------------------------------------------------------------------------ listing: §4

    [Fact]
    public async Task List_ReturnsPagedRowsWithRolesStatusAndNoSecrets()
    {
        await CreateUserAsync("analyst.two@netra.local", "Meera", "Nair", Analyst, Senior);

        var paged = await CreateService().ListAsync(
            new UserListFilterCriteria { PageSize = 10 }, _actorId);

        paged.TotalCount.Should().Be(2);
        paged.Items.Should().HaveCount(2);
        paged.Page.Should().Be(1);
        paged.TotalPages.Should().Be(1);

        // Ordered by the Identity normalised user name, so the order is stable on any provider.
        paged.Items.Select(row => row.UserName).Should().Equal(
            "admin.one@netra.local", "analyst.two@netra.local");

        var analyst = paged.Items.Single(row => row.UserName == "analyst.two@netra.local");
        analyst.FullName.Should().Be("Meera Nair");
        analyst.Email.Should().Be("analyst.two@netra.local");
        analyst.Roles.Should().Equal(Analyst, Senior);
        analyst.Status.Should().Be(UserAccountStatus.Active);
        analyst.CreatedAt.Should().Be(FixedInstant.UtcDateTime);
        analyst.IsCurrentActor.Should().BeFalse();

        paged.Items.Single(row => row.IsCurrentActor).Id.Should().Be(_actorId);
    }

    [Fact]
    public async Task List_PageBeyondTheLastPageIsHonestlyEmptyButStillCounts()
    {
        await CreateUserAsync("extra@netra.local", "Extra", "Entry", Investigator);

        var paged = await CreateService().ListAsync(
            new UserListFilterCriteria { Page = 9, PageSize = 2 }, _actorId);

        paged.TotalCount.Should().Be(2);
        paged.Items.Should().BeEmpty();
        paged.Page.Should().Be(9);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5000, 100)]
    public async Task List_ClampsPageSizeToTheDocumentedBounds(int requested, int expected)
    {
        var paged = await CreateService().ListAsync(
            new UserListFilterCriteria { PageSize = requested }, _actorId);

        paged.PageSize.Should().Be(expected);
        paged.Page.Should().Be(1);
    }

    [Fact]
    public async Task List_PageBelowOneIsLiftedToTheFirstPage()
    {
        await CreateUserAsync("lifted@netra.local", "Lift", "Ed", Investigator);

        var paged = await CreateService().ListAsync(
            new UserListFilterCriteria { Page = -3, PageSize = 2 }, _actorId);

        paged.Page.Should().Be(1);
        paged.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_SecondPageHoldsTheRemainderAndNeverRepeatsARow()
    {
        for (var i = 1; i <= 5; i++)
        {
            await CreateUserAsync($"page{i:00}@netra.local", "Page", $"Row{i:00}", Investigator);
        }

        var service = CreateService();
        var first = await service.ListAsync(
            new UserListFilterCriteria { PageSize = 2, Page = 1 }, _actorId);
        var second = await service.ListAsync(
            new UserListFilterCriteria { PageSize = 2, Page = 2 }, _actorId);

        first.TotalCount.Should().Be(6);
        first.Items.Select(row => row.Id).Should().NotIntersectWith(
            second.Items.Select(row => row.Id));
        second.Items.Should().HaveCount(2);
    }

    // ------------------------------------------------------------------------- search: §5

    [Theory]
    [InlineData("Kavita", "by first name")]
    [InlineData("Rao", "by last name")]
    [InlineData("DETECTIVE", "by normalised username, upper case")]
    [InlineData("detective@netra.local", "by email address")]
    [InlineData("BADGE-77120", "by badge number")]
    [InlineData("+91 90000 11223", "by phone number")]
    public async Task List_SearchMatchesTheIdentityColumnsAndNetraProfileFields(
        string term, string because)
    {
        await CreateUserAsync("analyst.search@netra.local", "Meera", "Nair", Analyst);
        var target = await CreateUserAsync("detective@netra.local", "Kavita", "Rao", Investigator);
        target.BadgeNumber = "BADGE-77120";
        target.PhoneNumber = "+91 90000 11223";
        await _userManager.UpdateAsync(target);

        var paged = await CreateService().ListAsync(
            new UserListFilterCriteria { Search = term }, _actorId);

        paged.TotalCount.Should().Be(1, $"the search should resolve exactly one account {because}");
        paged.Items.Single().UserName.Should().Be("detective@netra.local");
    }

    [Fact]
    public async Task List_UsernameAndEmailSearchIsCaseInsensitiveBecauseIdentityStoresThemUpper()
    {
        await CreateUserAsync("case.insensitive@netra.local", "Case", "Sensitive", Investigator);

        var service = CreateService();

        foreach (var term in new[] { "CASE.INS", "case.ins", "Case.Insensitive@NETRA.local" })
        {
            (await service.ListAsync(new UserListFilterCriteria { Search = term }, _actorId))
                .Items.Should().ContainSingle($"the search for {term} must still find the account");
        }
    }

    [Fact]
    public async Task List_SearchIsTrimmedSoWhitespaceChangesNothing()
    {
        await CreateUserAsync("padded@netra.local", "Padded", "Search", Investigator);

        var paged = await CreateService().ListAsync(
            new UserListFilterCriteria { Search = "   padded   " }, _actorId);

        paged.Items.Should().ContainSingle(row => row.UserName == "padded@netra.local");
    }

    [Fact]
    public async Task List_SearchWithoutAMatchReturnsAnEmptyPageNotAnError()
    {
        var paged = await CreateService().ListAsync(
            new UserListFilterCriteria { Search = "nobody-here" }, _actorId);

        paged.TotalCount.Should().Be(0);
        paged.Items.Should().BeEmpty();
        paged.From.Should().Be(0);
    }

    // ------------------------------------------------------------------- role + status: §6, §7

    [Fact]
    public async Task List_RoleFilterSelectsOnlyMembersOfThatRole()
    {
        await CreateUserAsync("senior.filter@netra.local", "Anil", "Kumar", Senior);
        await CreateUserAsync("multi.filter@netra.local", "Dual", "Role", Senior, Investigator);

        var paged = await CreateService().ListAsync(
            new UserListFilterCriteria { Role = Senior }, _actorId);

        paged.Items.Select(row => row.UserName).Should().BeEquivalentTo(
            ["multi.filter@netra.local", "senior.filter@netra.local"]);
        paged.Items.Should().OnlyContain(row => row.Roles.Contains(Senior));
    }

    [Fact]
    public async Task List_UnknownRoleNameMatchesNothingInsteadOfEverything()
    {
        await CreateUserAsync("other@netra.local", "Some", "One", Investigator);

        var paged = await CreateService().ListAsync(
            new UserListFilterCriteria { Role = "Chief Constable" }, _actorId);

        paged.TotalCount.Should().Be(0);
        paged.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task List_StatusFilterSeparatesActiveInactiveAndLocked()
    {
        var inactive = await CreateUserAsync("gone@netra.local", "Inactive", "Account", Investigator);
        inactive.IsActive = false;
        await _userManager.UpdateAsync(inactive);

        var locked = await CreateUserAsync("frozen@netra.local", "Locked", "Account", Investigator);
        await _userManager.SetLockoutEnabledAsync(locked, true);
        await _userManager.SetLockoutEndDateAsync(locked, FixedInstant.AddHours(2));

        var service = CreateService();

        (await service.ListAsync(
            new UserListFilterCriteria { Status = UserAccountStatus.Inactive }, _actorId))
            .Items.Single().UserName.Should().Be("gone@netra.local");

        (await service.ListAsync(
            new UserListFilterCriteria { Status = UserAccountStatus.Locked }, _actorId))
            .Items.Single().UserName.Should().Be("frozen@netra.local");

        // A lockout that has already expired is an active account again, not a locked one.
        var expired = await CreateUserAsync("thawed@netra.local", "Expired", "Lockout", Investigator);
        await _userManager.SetLockoutEnabledAsync(expired, true);
        await _userManager.SetLockoutEndDateAsync(expired, FixedInstant.AddHours(-1));

        var active = await service.ListAsync(
            new UserListFilterCriteria { Status = UserAccountStatus.Active }, _actorId);

        active.Items.Select(row => row.UserName).Should().BeEquivalentTo(
            ["admin.one@netra.local", "thawed@netra.local"]);
    }

    [Fact]
    public async Task GetDetail_ReportsDerivedStatusAndLockoutExpiry()
    {
        var user = await CreateUserAsync("detail@netra.local", "Deep", "Detail", Analyst);
        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, FixedInstant.AddMinutes(45));

        var detail = await CreateService().GetDetailAsync(user.Id, _actorId);

        detail!.Status.Should().Be(UserAccountStatus.Locked);
        detail.IsActive.Should().BeTrue("a lockout is not a deactivation");
        detail.LockoutExpiresAt.Should().Be(FixedInstant.AddMinutes(45));
        detail.IsAdministrator.Should().BeFalse();
        detail.IsCurrentActor.Should().BeFalse();
        detail.Roles.Should().Equal(Analyst);
    }

    [Fact]
    public async Task GetDetail_UnknownIdentifierReturnsNull()
    {
        (await CreateService().GetDetailAsync(Guid.NewGuid(), _actorId))
            .Should().BeNull();
    }

    [Fact]
    public async Task AccountDtosCarryNoCredentialOrStampFields()
    {
        // The views are bound to these DTOs, so the DTO surface is the data-minimisation
        // guarantee: if the field is not here, it cannot be rendered.
        foreach (var type in new[] { typeof(UserListItemDto), typeof(UserDetailDto) })
        {
            type.GetProperties().Select(p => p.Name).Should().NotContain(name =>
                name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Hash", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Stamp", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Concurrency", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---------------------------------------------------------------------- roles: §6, §10

    [Fact]
    public async Task GetAssignableRoles_ComesFromTheIdentityRoleStore()
    {
        (await CreateService().GetAssignableRolesAsync())
            .Should().BeEquivalentTo([Admin, Analyst, Investigator, Senior]);
    }

    [Fact]
    public async Task Create_WithARoleThatDoesNotExistIsRefusedAndWritesNoAccount()
    {
        var result = await CreateService().CreateAsync(
            NewUser("invented", roles: ["District Superintendent"]), _actorId);

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors["Roles"].Should().ContainSingle()
            .Which.Should().Contain("District Superintendent");
        (await _userManager.FindByNameAsync("invented")).Should().BeNull();
    }

    [Fact]
    public async Task Create_WithoutAnyRoleIsRefused()
    {
        var result = await CreateService().CreateAsync(
            NewUser("noroles", roles: []), _actorId);

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors["Roles"].Should().ContainSingle()
            .Which.Should().Be("Select at least one role.");
    }

    [Fact]
    public async Task Create_AssignsTheRolesIdentityOwns()
    {
        var id = (await CreateService().CreateAsync(
            NewUser("rolecheck", roles: [Analyst, Investigator]), _actorId)).Value;

        var user = await _userManager.FindByIdAsync(id.ToString());
        (await _userManager.GetRolesAsync(user!))
            .Should().BeEquivalentTo([Analyst, Investigator]);
    }

    // ---------------------------------------------------------------------- create: §8

    [Fact]
    public async Task Create_PersistsTheProfileAndAuditsTheCreation()
    {
        var result = await CreateService().CreateAsync(
            NewUser("fresh.recruit", email: "fresh.recruit@netra.local", badge: "BADGE-40021"),
            _actorId);

        result.Succeeded.Should().BeTrue(string.Join(", ", result.Errors.SelectMany(e => e.Value)));

        var user = await _userManager.FindByNameAsync("fresh.recruit");
        user!.FirstName.Should().Be("New");
        user.Email.Should().Be("fresh.recruit@netra.local");
        user.BadgeNumber.Should().Be("BADGE-40021");
        user.EmailConfirmed.Should().BeTrue("no email provider exists in this deployment");
        user.IsActive.Should().BeTrue();
        user.CreatedAt.Should().Be(FixedInstant.UtcDateTime);

        // Normalisation is Identity's job: an email lookup must work immediately.
        (await _userManager.FindByEmailAsync("fresh.recruit@netra.local")).Should().NotBeNull();

        var entry = (await TrailAsync(user.Id)).Single();
        entry.Action.Should().Be(AuditActionNames.UserCreated);
        entry.EntityType.Should().Be(AuditEntityTypeNames.Account);
        entry.UserId.Should().Be(_actorId);
        entry.PropertyName.Should().Be("Roles");
        entry.NewValues.Should().Be(Investigator);
    }

    [Fact]
    public async Task Create_NeverWritesThePasswordToTheTrailOrTheAccount()
    {
        var result = await CreateService().CreateAsync(
            new CreateUserInput
            {
                FirstName = "Secret",
                LastName = "Keeper",
                UserName = "secrety",
                Email = "secrety@netra.local",
                Roles = [Investigator],
                Password = "Uniq ueP@ssw0rd-Sentinel"
            },
            _actorId);

        var user = await _userManager.FindByNameAsync("secrety");
        var trail = await TrailAsync(user!.Id);

        result.Succeeded.Should().BeTrue();
        string.Concat(trail.SelectMany(e => new[] { e.OldValues, e.NewValues }))
            .Should().NotContain("Uniq ueP@ssw0rd-Sentinel");
        user.PasswordHash.Should().NotBeNullOrWhiteSpace()
            .And.NotBe("Uniq ueP@ssw0rd-Sentinel");
    }

    [Fact]
    public async Task Create_DuplicateUserNameIsAConflictOnTheUserNameField()
    {
        var result = await CreateService().CreateAsync(
            NewUser("admin.one@netra.local", email: "another.person@netra.local"), _actorId);

        result.Status.Should().Be(OperationStatus.Conflict);
        result.Errors["UserName"].Should().ContainSingle()
            .Which.Should().Be("That username is already taken.");
    }

    [Fact]
    public async Task Create_DuplicateEmailIsAConflictOnTheEmailField()
    {
        await CreateUserAsync("taken.email@netra.local", "First", "Holder", Investigator);

        var result = await CreateService().CreateAsync(
            NewUser("second.holder", email: "taken.email@netra.local"), _actorId);

        result.Status.Should().Be(OperationStatus.Conflict);
        result.Errors["Email"].Should().ContainSingle()
            .Which.Should().Be("That email address is already registered.");
        (await _userManager.FindByNameAsync("second.holder")).Should().BeNull();
    }

    [Fact]
    public async Task Create_DuplicateBadgeNumberIsAConflictNotADatabaseError()
    {
        await CreateService().CreateAsync(NewUser("badge.first", badge: "BADGE-90300"), _actorId);

        var result = await CreateService().CreateAsync(
            NewUser("badge.second", badge: "badge.90300"), _actorId);

        // The same badge value is what matters, so the check is on the trimmed column.
        var clash = await CreateService().CreateAsync(
            NewUser("badge.third", badge: "BADGE-90300"), _actorId);

        result.Succeeded.Should().BeTrue();
        clash.Status.Should().Be(OperationStatus.Conflict);
        clash.Errors["BadgeNumber"].Should().ContainSingle()
            .Which.Should().Be("Another account already uses that badge number.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_BlankProfileFieldsAreRefusedBeforeIdentityRuns(string firstName)
    {
        var result = await CreateService().CreateAsync(
            new CreateUserInput
            {
                FirstName = firstName,
                LastName = "Officer",
                UserName = "blank.first",
                Email = "blank.first@netra.local",
                Roles = [Investigator],
                Password = ValidPassword
            },
            _actorId);

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors["FirstName"].Should().ContainSingle()
            .Which.Should().Be("First name is required.");
        (await _userManager.FindByNameAsync("blank.first")).Should().BeNull();
    }

    [Fact]
    public async Task Create_WeakPasswordIsRejectedByIdentityAndReportedOnThePasswordField()
    {
        var result = await CreateService().CreateAsync(
            new CreateUserInput
            {
                FirstName = "Weak",
                LastName = "Password",
                UserName = "weak.password",
                Email = "weak.password@netra.local",
                Roles = [Investigator],
                Password = "short"
            },
            _actorId);

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors.Should().ContainKey("Password");
        result.Errors["Password"].Should().NotBeEmpty();
        (await _userManager.FindByNameAsync("weak.p@ss")).Should().BeNull();
    }

    // ------------------------------------------------------------------------ edit: §9

    [Fact]
    public async Task Edit_ChangesTheProfileAndAuditsEveryFieldThatMoved()
    {
        var user = await CreateUserAsync("editme@netra.local", "Ed", "ited", Investigator);

        var result = await CreateService().EditAsync(new EditUserInput
        {
            UserId = user.Id,
            FirstName = "Edward",
            LastName = "ited",
            Email = "edward.ited@netra.local",
            BadgeNumber = "BADGE-11",
            Rank = "Sergeant",
            Roles = [Investigator]
        }, _actorId);

        result.Succeeded.Should().BeTrue();

        var saved = await _userManager.FindByIdAsync(user.Id.ToString());
        saved!.FirstName.Should().Be("Edward");
        saved.Email.Should().Be("edward.ited@netra.local");
        saved.BadgeNumber.Should().Be("BADGE-11");

        // Re-normalised, so the new address is still searchable.
        (await _userManager.FindByEmailAsync("edward.ited@netra.local")).Should().NotBeNull();

        var entry = (await TrailAsync(user.Id)).Single(e => e.Action == AuditActionNames.UserEdited);
        entry.EntityType.Should().Be(AuditEntityTypeNames.Account);
        entry.UserId.Should().Be(_actorId);
        entry.OldValues.Should().Contain(@"First name: ""Ed""");
        entry.NewValues.Should().Contain(@"First name: ""Edward""");
        entry.OldValues.Should().Contain(@"Badge number: ""(none)""");
        entry.NewValues.Should().Contain(@"Badge number: ""BADGE-11""");
    }

    [Fact]
    public async Task Edit_WithNothingChangedWritesNoAuditEntry()
    {
        var user = await CreateUserAsync("still@netra.local", "Still", "Same", Investigator);

        var result = await CreateService().EditAsync(new EditUserInput
        {
            UserId = user.Id,
            FirstName = "Still",
            LastName = "Same",
            Email = "still@netra.local",
            Roles = [Investigator]
        }, _actorId);

        result.Succeeded.Should().BeTrue();
        (await TrailAsync(user.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Edit_UnknownAccountIsNotFound()
    {
        var result = await CreateService().EditAsync(new EditUserInput
        {
            UserId = Guid.NewGuid(),
            FirstName = "Ghost",
            LastName = "Writer",
            Email = "ghost@netra.local",
            Roles = [Investigator]
        }, _actorId);

        result.Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task Edit_CannotTakeAnEmailAnotherAccountAlreadyUses()
    {
        await CreateUserAsync("occupied@netra.local", "Occ", "Upied", Investigator);
        var user = await CreateUserAsync("movor@netra.local", "Mov", "Or", Investigator);

        var result = await CreateService().EditAsync(new EditUserInput
        {
            UserId = user.Id,
            FirstName = "Mov",
            LastName = "Or",
            Email = "occupied@netra.local",
            Roles = [Investigator]
        }, _actorId);

        result.Status.Should().Be(OperationStatus.Conflict);
        result.Errors["Email"].Should().ContainSingle()
            .Which.Should().Be("Another account already uses that email address.");
        (await _userManager.FindByIdAsync(user.Id.ToString()))!
            .Email.Should().Be("movor@netra.local");
    }

    [Fact]
    public async Task Edit_KeepsTheUserNameUntouchedBecauseTheInputHasNoSuchField()
    {
        var user = await CreateUserAsync("fixed.name@netra.local", "Fixed", "Name", Investigator);
        var inputProperties = typeof(EditUserInput).GetProperties().Select(p => p.Name).ToList();

        inputProperties.Should().NotContain(nameof(ApplicationUser.UserName));
        inputProperties.Should().NotContain(nameof(ApplicationUser.PasswordHash));
        inputProperties.Should().NotContain(nameof(ApplicationUser.SecurityStamp));
        inputProperties.Should().NotContain(nameof(ApplicationUser.LockoutEnd));
        inputProperties.Should().NotContain(nameof(ApplicationUser.IsActive));

        await CreateService().EditAsync(new EditUserInput
        {
            UserId = user.Id,
            FirstName = "Renamed",
            LastName = "Name",
            Email = "fixed.name@netra.local",
            Roles = [Investigator]
        }, _actorId);

        var saved = await _userManager.FindByIdAsync(user.Id.ToString());
        saved!.UserName.Should().Be("fixed.name@netra.local");
        saved.FirstName.Should().Be("Renamed");
    }

    [Fact]
    public async Task Edit_RoleChangeIsAuditedWithBeforeAndAfter()
    {
        var user = await CreateUserAsync("role.swap@netra.local", "Swap", "Picker", Investigator);

        var result = await CreateService().EditAsync(new EditUserInput
        {
            UserId = user.Id,
            FirstName = "Swap",
            LastName = "Picker",
            Email = "role.swap@netra.local",
            Roles = [Analyst, Senior]
        }, _actorId);

        result.Succeeded.Should().BeTrue();

        var saved = await _userManager.FindByIdAsync(user.Id.ToString());
        (await _userManager.GetRolesAsync(saved!)).Should().BeEquivalentTo([Analyst, Senior]);

        var entry = (await TrailAsync(user.Id))
            .Single(e => e.Action == AuditActionNames.UserRoleChanged);
        entry.PropertyName.Should().Be("Roles");
        entry.OldValues.Should().Be(Investigator);
        entry.NewValues.Should().Be($"{Analyst}, {Senior}");
    }

    // --------------------------------------------------------------- self-protection: §12

    [Fact]
    public async Task Edit_CannotStripTheAdministratorRoleFromTheSignedInAccount()
    {
        var result = await CreateService().EditAsync(new EditUserInput
        {
            UserId = _actorId,
            FirstName = "Rohit",
            LastName = "Sharma",
            Email = "admin.one@netra.local",
            Roles = [Investigator]
        }, _actorId);

        result.Status.Should().Be(OperationStatus.Forbidden);
        result.Errors[""].Should().ContainSingle()
            .Which.Should().Be("You cannot remove the administrator role from your own account.");

        var stillAdmin = await _userManager.FindByIdAsync(_actorId.ToString());
        (await _userManager.GetRolesAsync(stillAdmin!)).Should().Equal(Admin);

        var refusal = (await TrailAsync(_actorId))
            .Single(e => e.Action == AuditActionNames.UserAccessDenied);
        refusal.UserId.Should().Be(_actorId);
        refusal.PropertyName.Should().Be("Roles");
    }

    [Fact]
    public async Task Deactivate_WithAnotherAdministratorStillInPlaceIsPermitted()
    {
        var second = await CreateUserAsync("second.ready@netra.local", "Second", "Ready", Admin);

        var result = await CreateService().SetActiveAsync(second.Id, isActive: false, _actorId);

        // Two administrators exist, so switching one off cannot lock the installation out.
        result.Succeeded.Should().BeTrue();
        (await _userManager.FindByIdAsync(second.Id.ToString()))!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Deactivate_LastAdministratorIsRefusedWhenNoOtherHolderExists()
    {
        // Demote the fixture account directly, leaving exactly one administrator in place.
        var actor = await _userManager.FindByIdAsync(_actorId.ToString());
        await _userManager.RemoveFromRoleAsync(actor!, Admin);

        var successor = await CreateUserAsync("successor@netra.local", "Sur", "Vivor", Admin);

        var result = await CreateService().SetActiveAsync(successor.Id, isActive: false, _actorId);

        result.Status.Should().Be(OperationStatus.Forbidden);
        result.Errors[""].Should().ContainSingle()
            .Which.Should().Contain("last administrator account");
        (await _userManager.FindByIdAsync(successor.Id.ToString()))!.IsActive.Should().BeTrue();
        (await TrailAsync(successor.Id)).Single()
            .Action.Should().Be(AuditActionNames.UserAccessDenied);
    }

    [Fact]
    public async Task Edit_LastAdministratorDemotionIsRefusedAndAudited()
    {
        var lone = await CreateUserAsync("lone.admin@netra.local", "Lone", "Admin", Admin);
        var other = await CreateUserAsync("another.admin@netra.local", "Another", "Admin", Admin);

        // Both administrators are removable while a third holder exists: the acting account.
        (await CreateService().EditAsync(new EditUserInput
        {
            UserId = lone.Id,
            FirstName = "Lone",
            LastName = "Admin",
            Email = "lone.admin@netra.local",
            Roles = [Senior]
        }, _actorId)).Succeeded.Should().BeTrue();

        // With the actor demoted below, `other` would become the only administrator left, so
        // removing the role from it too is refused.
        var actor = await _userManager.FindByIdAsync(_actorId.ToString());
        await _userManager.RemoveFromRoleAsync(actor!, Admin);

        var result = await CreateService().EditAsync(new EditUserInput
        {
            UserId = other.Id,
            FirstName = "Another",
            LastName = "Admin",
            Email = "another.admin@netra.local",
            Roles = [Senior]
        }, _actorId);

        result.Status.Should().Be(OperationStatus.Forbidden);
        result.Errors[""].Should().ContainSingle()
            .Which.Should().Contain("last administrator account");
        (await _userManager.GetRolesAsync(other)).Should().Equal(Admin);
        (await TrailAsync(other.Id)).Last()
            .Action.Should().Be(AuditActionNames.UserAccessDenied);
    }

    // ------------------------------------------------------------------ activation: §11, §12

    [Fact]
    public async Task Deactivate_TurnsOffSignInInvalidatesTheStampAndIsAudited()
    {
        var user = await CreateUserAsync("suspend@netra.local", "Sus", "Pend", Investigator);
        var stampBefore = user.SecurityStamp;

        var result = await CreateService().SetActiveAsync(user.Id, isActive: false, _actorId);

        result.Succeeded.Should().BeTrue();
        var saved = await _userManager.FindByIdAsync(user.Id.ToString());
        saved!.IsActive.Should().BeFalse();
        saved.SecurityStamp.Should().NotBe(stampBefore,
            "an existing cookie must not survive a deactivation");

        var entry = (await TrailAsync(user.Id)).Single();
        entry.Action.Should().Be(AuditActionNames.UserDeactivated);
        entry.PropertyName.Should().Be("IsActive");
        entry.OldValues.Should().Be(bool.TrueString);
        entry.NewValues.Should().Be(bool.FalseString);
    }

    [Fact]
    public async Task Activate_ReversesADeactivationAndIsAudited()
    {
        var user = await CreateUserAsync("restore@netra.local", "Re", "Store", Investigator);
        var service = CreateService();

        await service.SetActiveAsync(user.Id, isActive: false, _actorId);
        var restored = await service.SetActiveAsync(user.Id, isActive: true, _actorId);

        restored.Succeeded.Should().BeTrue();
        (await _userManager.FindByIdAsync(user.Id.ToString()))!.IsActive.Should().BeTrue();

        (await TrailAsync(user.Id)).Select(e => e.Action).Should().BeEquivalentTo(
            [AuditActionNames.UserDeactivated, AuditActionNames.UserActivated]);
    }

    [Fact]
    public async Task SetActive_IdempotentRepeatWritesNothingToTheTrail()
    {
        var user = await CreateUserAsync("twice@netra.local", "Twice", "Told", Investigator);
        var service = CreateService();

        await service.SetActiveAsync(user.Id, isActive: false, _actorId);
        var again = await service.SetActiveAsync(user.Id, isActive: false, _actorId);

        again.Succeeded.Should().BeTrue();
        (await TrailAsync(user.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task SetActive_UnknownAccountIsNotFound()
    {
        (await CreateService().SetActiveAsync(Guid.NewGuid(), isActive: false, _actorId))
            .Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task Deactivate_CannotBeUsedByTheSignedInAdministratorOnTheirOwnAccount()
    {
        var result = await CreateService().SetActiveAsync(_actorId, isActive: false, _actorId);

        result.Status.Should().Be(OperationStatus.Forbidden);
        result.Errors[""].Should().ContainSingle()
            .Which.Should().Be("You cannot deactivate your own account while signed in.");
        (await _userManager.FindByIdAsync(_actorId.ToString()))!.IsActive.Should().BeTrue();
        (await TrailAsync(_actorId)).Single()
            .Action.Should().Be(AuditActionNames.UserAccessDenied);
    }

    [Fact]
    public async Task SetActive_DoesNotWriteIdentityRoleTablesOrRawSqlByHand()
    {
        // The service holds no command text: role membership changes go through
        // UserManager/RoleManager, which own the security stamp and concurrency rules.
        typeof(UserManagementService)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(f => f.FieldType.Name)
            .Should().BeEquivalentTo(
                ["NetraDbContext", "UserManager`1", "RoleManager`1", "IAuditService", "TimeProvider"]);
    }
}
