using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Domain.Audit;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Web.Authorization;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// End-to-end checks for the User Management module. Every route is exercised through the
/// real MVC pipeline, so authorisation, anti-forgery, model binding, the application
/// service, Identity and the audit trail are the production implementations.
/// </summary>
public class UsersIntegrationTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";

    private readonly NetraWebFactory _factory;

    public UsersIntegrationTests(NetraWebFactory factory)
    {
        _factory = factory;
    }

    // ---------------------------------------------------------------- helpers

    private async Task<(HttpClient Client, HttpResponseMessage Response)> LoginAttemptAsync(
        string userName, string password)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/Account/Login"));

        var response = await client.PostAsync("/Account/Login", AuthenticationScreenTests.Form(
            ("UserName", userName),
            ("Password", password),
            ("__RequestVerificationToken", token)));

        return (client, response);
    }

    private async Task<HttpClient> SignInAsync(string email, params string[] roles)
    {
        await _factory.SeedUserAsync(email, Password, roles);

        var (client, response) = await LoginAttemptAsync(email, Password);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    /// <summary>
    /// Writes an account straight through the store so a fixture can control profile text,
    /// account state and role membership without a password. Roles are still recorded in the
    /// Identity role tables, exactly as the application does.
    /// </summary>
    private async Task<ApplicationUser> SeedAccountAsync(
        string userName,
        string firstName,
        string lastName,
        string? email = null,
        string? badgeNumber = null,
        string? rank = null,
        bool isActive = true,
        DateTimeOffset? lockoutEnd = null,
        string[]? roles = null)
    {
        var roleNames = roles ?? Array.Empty<string>();
        foreach (var role in roleNames)
        {
            await EnsureRoleAsync(role);
        }

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email ?? userName,
            NormalizedEmail = (email ?? userName).ToUpperInvariant(),
            EmailConfirmed = true,
            // Identity refuses to update an account without a security stamp, so a
            // directly-inserted row carries one exactly as UserManager.CreateAsync would.
            SecurityStamp = Guid.NewGuid().ToString("N"),
            FirstName = firstName,
            LastName = lastName,
            BadgeNumber = badgeNumber,
            Rank = rank,
            IsActive = isActive,
            LockoutEnabled = lockoutEnd is not null,
            LockoutEnd = lockoutEnd,
            CreatedAt = new DateTime(2026, 3, 4, 8, 0, 0, DateTimeKind.Utc)
        };

        context.Users.Add(user);

        foreach (var role in roleNames)
        {
            var roleId = await context.Roles
                .Where(r => r.Name == role)
                .Select(r => r.Id)
                .SingleAsync();

            context.UserRoles.Add(new IdentityUserRole<Guid>
            {
                UserId = user.Id,
                RoleId = roleId
            });
        }

        await context.SaveChangesAsync();
        return user;
    }

    private async Task EnsureRoleAsync(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }
    }

    private async Task<T> InDbAsync<T>(Func<NetraDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<NetraDbContext>());
    }

    private Task<ApplicationUser?> AccountByNameAsync(string userName) =>
        InDbAsync(db => db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.NormalizedUserName == userName.ToUpperInvariant()));

    private Task<ApplicationUser?> AccountByIdAsync(Guid id) =>
        InDbAsync(db => db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id));

    private async Task<IReadOnlyList<string>> RolesOfAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());

        user.Should().NotBeNull();
        return (await userManager.GetRolesAsync(user!)).ToList();
    }

    private Task<List<AuditLog>> TrailAsync(Guid entityId, string action) =>
        InDbAsync(db => db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityId == entityId && a.Action == action)
            .ToListAsync());

    private Task<int> AccountCountAsync() => InDbAsync(db => db.Users.CountAsync());

    /// <summary>
    /// Removes every administrator membership from the role store so a fixture account can
    /// become the installation's only remaining administrator. A session that is already
    /// signed in keeps its role claim, which is exactly the state the guardrail must survive.
    /// </summary>
    private async Task StripAllAdministratorsAsync()
    {
        await InDbAsync(async db =>
        {
            var adminRole = await db.Roles.SingleOrDefaultAsync(
                r => r.Name == NetraRoles.SystemAdministrator);

            if (adminRole is not null)
            {
                foreach (var membership in await db.UserRoles
                             .Where(ur => ur.RoleId == adminRole.Id)
                             .ToListAsync())
                {
                    db.UserRoles.Remove(membership);
                }

                await db.SaveChangesAsync();
            }

            return true;
        });
    }

    private async Task<string> TokenAsync(HttpClient client, string path) =>
        AuthenticationScreenTests.ExtractAntiForgeryToken(await client.GetStringAsync(path));

    private static Guid IdFromRedirect(HttpResponseMessage response) =>
        Guid.Parse(response.Headers.Location!.OriginalString.Split('/')[^1]);

    // ---------------------------------------------------------------- the routes are gated

    [Theory]
    [InlineData("/Users")]
    [InlineData("/Users/Create")]
    [InlineData("/Users/Details/11111111-1111-1111-1111-111111111111")]
    [InlineData("/Users/Edit/11111111-1111-1111-1111-111111111111")]
    public async Task AnonymousGet_RedirectsToSignIn(string path)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
    }

    [Theory]
    [InlineData("/Users/Create")]
    [InlineData("/Users/Edit/11111111-1111-1111-1111-111111111111")]
    [InlineData("/Users/Activate/11111111-1111-1111-1111-111111111111")]
    [InlineData("/Users/Deactivate/11111111-1111-1111-1111-111111111111")]
    public async Task AnonymousPost_IsRedirectedAndWritesNothing(string path)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var before = await AccountCountAsync();
        var response = await client.PostAsync(path, AuthenticationScreenTests.Form(
            ("FirstName", "Anon"), ("Password", Password)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
        (await AccountCountAsync()).Should().Be(before);
    }

    [Theory]
    [InlineData(NetraRoles.Investigator, "/Users")]
    [InlineData(NetraRoles.Investigator, "/Users/Create")]
    [InlineData(NetraRoles.PoliceIntelligenceAnalyst, "/Users")]
    [InlineData(NetraRoles.PoliceIntelligenceAnalyst, "/Users/Edit/11111111-1111-1111-1111-111111111111")]
    [InlineData(NetraRoles.SeniorOfficer, "/Users")]
    [InlineData(NetraRoles.SeniorOfficer, "/Users/Details/11111111-1111-1111-1111-111111111111")]
    public async Task NonAdministratorGet_IsForbidden(string role, string path)
    {
        var client = await SignInAsync(
            $"forbidden.{Slug(role)}@netra.local", role);

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(NetraRoles.Investigator)]
    [InlineData(NetraRoles.SeniorOfficer)]
    [InlineData(NetraRoles.PoliceIntelligenceAnalyst)]
    public async Task NonAdministratorPost_IsForbiddenAndLeavesTheAccountActive(string role)
    {
        var target = await SeedAccountAsync(
            $"guarded.{Slug(role)}@netra.local", "Guarded", "Account",
            roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync($"mutator.{Slug(role)}@netra.local", role);

        var response = await client.PostAsync(
            $"/Users/Deactivate/{target.Id}", AuthenticationScreenTests.Form());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AccountByIdAsync(target.Id))!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AdministratorGet_IsAllowed()
    {
        var client = await SignInAsync(
            "allowed.userlist@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.GetAsync("/Users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string Slug(string role) => role.Replace(" ", "-").Replace("/", "");

    // ---------------------------------------------------------------- the list

    [Fact]
    public async Task Index_RendersAccountsFiltersColumnsAndRowActions()
    {
        await _factory.SeedUserAsync(
            "shell.admin@netra.local", Password, NetraRoles.SystemAdministrator);
        var self = await AccountByNameAsync("shell.admin@netra.local");

        var analyst = await SeedAccountAsync(
            "shell.analyst@netra.local", "Kavita", "Rao", "kavita.rao@netra.local",
            "BADGE-77120", roles: new[] { NetraRoles.PoliceIntelligenceAnalyst });

        var (client, _) = await LoginAttemptAsync("shell.admin@netra.local", Password);

        // The list is scoped to the fixture account so earlier tests in this shared
        // in-memory database cannot push it off the first page.
        var listed = await client.GetStringAsync("/Users?search=BADGE-77120");

        listed.Should().Contain("User Management");
        listed.Should().Contain("NETRA accounts live in ASP.NET Identity");
        listed.Should().Contain("name=\"search\"");
        listed.Should().Contain("name=\"role\"");
        listed.Should().Contain("name=\"status\"");
        listed.Should().Contain("All roles");
        listed.Should().Contain("Any status");
        listed.Should().Contain("Apply Filters");
        listed.Should().Contain("Accounts");
        listed.Should().Contain("<th scope=\"col\">User</th>");
        listed.Should().Contain("<th scope=\"col\">Role(s)</th>");
        listed.Should().Contain("Kavita Rao");
        listed.Should().Contain("kavita.rao@netra.local");
        listed.Should().Contain(NetraRoles.PoliceIntelligenceAnalyst);
        listed.Should().Contain("1 account");
        listed.Should().Contain("/Users/Details/" + analyst.Id);
        listed.Should().Contain("New account");
        listed.Should().Contain("href=\"/Users/Create\"");
        listed.Should().Contain("/Users/Edit/" + analyst.Id);
        listed.Should().Contain($"/Users/Deactivate/{analyst.Id}");

        // The signed-in row is marked and carries no state-change control.
        var own = await client.GetStringAsync("/Users?search=shell.admin");

        own.Should().Contain("Your account");
        own.Should().NotContain($"/Users/Deactivate/{self!.Id}");
    }

    [Fact]
    public async Task Index_WhenNothingMatches_ItSaysSoAndOffersToClear()
    {
        var client = await SignInAsync(
            "empty.users@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync("/Users?search=nobodyhasthisname");

        html.Should().Contain("No accounts match the selected filters");
        html.Should().Contain("Clear filters");
    }

    [Fact]
    public async Task Search_IsAppliedOnTheServer()
    {
        await SeedAccountAsync("search.zephyr@netra.local", "Zephyr", "Matching",
            roles: new[] { NetraRoles.Investigator });
        await SeedAccountAsync("search.other@netra.local", "Quinton", "Unmatched",
            roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("list.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync("/Users?search=Zephyr");

        html.Should().Contain("Zephyr Matching");
        html.Should().NotContain("Quinton Unmatched");
        html.Should().Contain("1 account");
        html.Should().Contain("value=\"Zephyr\"");

        var noBadge = await client.GetStringAsync("/Users?search=BADGE-0000-unused");
        noBadge.Should().Contain("No accounts match the selected filters");
    }

    [Fact]
    public async Task Search_MatchesBadgeAndSignInNameColumns()
    {
        await SeedAccountAsync("badge.findme@netra.local", "Finda", "Badgeholder",
            "badge.findme@netra.local", "BADGE-90012",
            roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("badge.admin@netra.local", NetraRoles.SystemAdministrator);

        (await client.GetStringAsync("/Users?search=BADGE-90012"))
            .Should().Contain("Finda Badgeholder");
        (await client.GetStringAsync("/Users?search=badge.findme"))
            .Should().Contain("Finda Badgeholder");
    }

    [Fact]
    public async Task RoleFilter_ReturnsOnlyHoldersOfThatRole()
    {
        await SeedAccountAsync("filter.investigations@netra.local", "Ines", "Tigator",
            roles: new[] { NetraRoles.Investigator });
        await SeedAccountAsync("filter.analyst@netra.local", "Anita", "Lyst",
            roles: new[] { NetraRoles.PoliceIntelligenceAnalyst });

        var client = await SignInAsync("filter.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync(
            $"/Users?role={Uri.EscapeDataString(NetraRoles.PoliceIntelligenceAnalyst)}");

        html.Should().Contain("Anita Lyst");
        html.Should().NotContain("Ines Tigator");

        var unknown = await client.GetStringAsync("/Users?role=Ghost%20Operative");
        unknown.Should().Contain("No accounts match the selected filters");
    }

    [Theory]
    [InlineData("Inactive", "Ida Off", "Ola Lockholm", "Activea Ann")]
    [InlineData("Locked", "Ola Lockholm", "Ida Off", "Activea Ann")]
    [InlineData("Active", "Activea Ann", "Ida Off", "Ola Lockholm")]
    public async Task StatusFilter_UsesIdentityStatePlusIsActive(
        string status, string expected, string unexpectedOne, string unexpectedTwo)
    {
        await SeedAccountAsync("status.inactive@netra.local", "Ida", "Off",
            isActive: false, roles: new[] { NetraRoles.Investigator });
        await SeedAccountAsync("status.locked@netra.local", "Ola", "Lockholm",
            lockoutEnd: DateTimeOffset.UtcNow.AddMinutes(30),
            roles: new[] { NetraRoles.Investigator });
        await SeedAccountAsync("status.active@netra.local", "Activea", "Ann",
            roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("status.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync($"/Users?status={status}");

        html.Should().Contain(expected);
        html.Should().NotContain(unexpectedOne);
        html.Should().NotContain(unexpectedTwo);
        html.Should().Contain($"value=\"{status}\" selected");
    }

    [Fact]
    public async Task Pagination_SplitsPagesAndKeepsTheActiveFilters()
    {
        foreach (var number in Enumerable.Range(1, 24))
        {
            await SeedAccountAsync(
                $"paging{number:D2}@netra.local", "Pager", $"Officer{number:D2}",
                roles: new[] { NetraRoles.Investigator });
        }

        var client = await SignInAsync("list.admin@netra.local", NetraRoles.SystemAdministrator);

        var first = await client.GetStringAsync("/Users?search=Pager");
        first.Should().Contain("24 accounts");
        first.Should().Contain("Showing 1&ndash;20 of 24");
        first.Should().Contain("Pager Officer01");
        first.Should().NotContain("Pager Officer21");
        first.Should().Contain("search=Pager");
        first.Should().Contain("page=2");

        var second = await client.GetStringAsync("/Users?search=Pager&page=2");
        second.Should().Contain("Showing 21&ndash;24 of 24");
        second.Should().Contain("Pager Officer21");
        second.Should().NotContain("Pager Officer01");
        second.Should().Contain("search=Pager");

        // A page number below 1 is lifted, never honoured as written.
        var lifted = await client.GetStringAsync("/Users?search=Pager&page=0");
        lifted.Should().Contain("Showing 1&ndash;20 of 24");
    }

    // ---------------------------------------------------------------- details

    [Fact]
    public async Task Details_RendersProfileWithoutIdentitySecrets()
    {
        const string userName = "secrets.target@netra.local";
        await _factory.SeedUserAsync(userName, "TargetPassw0rd!", NetraRoles.Investigator);
        var seeded = await AccountByNameAsync(userName);
        seeded.Should().NotBeNull();

        await InDbAsync(async db =>
        {
            var tracked = await db.Users.SingleAsync(u => u.Id == seeded!.Id);
            tracked.BadgeNumber = "BADGE-55001";
            tracked.Rank = "Inspector";
            await db.SaveChangesAsync();
            return true;
        });

        var client = await SignInAsync("secrets.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync($"/Users/Details/{seeded!.Id}");

        html.Should().Contain("Test User");
        html.Should().Contain(userName);
        html.Should().Contain("BADGE-55001");
        html.Should().Contain("Inspector");
        html.Should().Contain("Roles &amp; access");
        html.Should().Contain(NetraRoles.Investigator);
        html.Should().Contain("/Users/Edit/" + seeded.Id);

        // Never displayed, never printed: credential material and the stored hash.
        html.Should().NotContain("PasswordHash");
        html.Should().NotContain("SecurityStamp");
        html.Should().NotContain("ConcurrencyStamp");
        html.Should().NotContain("TargetPassw0rd!");
        html.Should().NotContain(seeded.PasswordHash!);
        html.Should().NotContain(seeded.SecurityStamp!);
    }

    [Fact]
    public async Task Details_UnknownAccount_IsNotFound()
    {
        var client = await SignInAsync("missing.detail@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.GetAsync("/Users/Details/11111111-1111-1111-1111-111111111111");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Details_MalformedIdentifier_IsNotFound()
    {
        var client = await SignInAsync("malformed.detail@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.GetAsync("/Users/Details/not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Details_DeactivatedAccount_ExplainsTheBlock()
    {
        var target = await SeedAccountAsync("blocked.account@netra.local", "Block", "Edaccount",
            "blocked.account@netra.local", isActive: false,
            roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("blocked.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync($"/Users/Details/{target.Id}");

        html.Should().Contain(
            "This account is deactivated. Sign-in is refused until it is reactivated.");
        html.Should().Contain("Activate account");
        html.Should().NotContain("Deactivate account");
    }

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task Create_Get_RendersFormWithCredentialSafeFields()
    {
        var client = await SignInAsync("create.form@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync("/Users/Create");

        html.Should().Contain("New Account");
        html.Should().Contain("name=\"UserName\"");
        html.Should().Contain("name=\"Email\"");
        html.Should().Contain("name=\"FirstName\"");
        html.Should().Contain("name=\"LastName\"");
        html.Should().Contain("name=\"BadgeNumber\"");
        html.Should().Contain("name=\"Rank\"");
        html.Should().Contain("name=\"Password\"");
        html.Should().Contain("name=\"ConfirmPassword\"");
        html.Should().Contain("type=\"password\"");
        html.Should().Contain("name=\"Roles\"");
        html.Should().Contain($"value=\"{NetraRoles.SystemAdministrator}\"");
        html.Should().Contain("Create account");
        html.Should().Contain("__RequestVerificationToken");

        // Identity's own columns are not offered as inputs.
        html.Should().NotContain("name=\"PasswordHash\"");
        html.Should().NotContain("name=\"SecurityStamp\"");
        html.Should().NotContain("name=\"LockoutEnd\"");
    }

    [Fact]
    public async Task Create_Post_CreatesThroughIdentityGrantsRolesAndAudits()
    {
        const string userName = "suresh.iyer@netra.local";
        const string temporaryPassword = "FirstPassw0rd!";

        var client = await SignInAsync("create.ok@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync("/Users/Create", AuthenticationScreenTests.Form(
            ("FirstName", "Suresh"),
            ("LastName", "Iyer"),
            ("UserName", userName),
            ("Email", userName),
            ("BadgeNumber", "BADGE-4412"),
            ("Rank", "Inspector"),
            ("Roles", NetraRoles.Investigator),
            ("Roles", NetraRoles.PoliceIntelligenceAnalyst),
            ("Password", temporaryPassword),
            ("ConfirmPassword", temporaryPassword),
            ("__RequestVerificationToken", await TokenAsync(client, "/Users/Create"))));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var createdId = IdFromRedirect(response);

        var created = await AccountByIdAsync(createdId);
        created.Should().NotBeNull();
        created!.FirstName.Should().Be("Suresh");
        created.Email!.Should().Be(userName);
        created.BadgeNumber!.Should().Be("BADGE-4412");
        created.IsActive.Should().BeTrue();
        created.PasswordHash!.Should().NotBeNullOrWhiteSpace();

        (await RolesOfAsync(createdId)).Should().BeEquivalentTo(
            new[] { NetraRoles.Investigator, NetraRoles.PoliceIntelligenceAnalyst });

        var trail = await TrailAsync(createdId, "UserCreated");
        trail.Should().ContainSingle();
        trail[0].EntityType.Should().Be("Account");
        trail[0].PropertyName!.Should().Be("Roles");
        trail[0].NewValues!.Should().Contain(NetraRoles.Investigator);

        // The password is handed to Identity and appears nowhere in the trail.
        var payload = await InDbAsync(db => db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityId == createdId)
            .ToListAsync());
        payload.Should().NotContain(a =>
            (a.NewValues ?? string.Empty).Contains(temporaryPassword)
            || (a.OldValues ?? string.Empty).Contains(temporaryPassword));

        // The credential works: the new account can sign in.
        var (newClient, login) = await LoginAttemptAsync(userName, temporaryPassword);
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await newClient.GetAsync("/Dashboard")).StatusCode.Should().Be(HttpStatusCode.OK);

        var details = await client.GetStringAsync($"/Users/Details/{createdId}");
        details.Should().Contain($"Account created for {userName}.");
        details.Should().Contain("Suresh Iyer");
        details.Should().Contain("BADGE-4412");
    }

    [Fact]
    public async Task Create_Post_DuplicateUsernameIsRefused()
    {
        const string taken = "duplicate.username@netra.local";
        await SeedAccountAsync(taken, "Existing", "Holder");

        var client = await SignInAsync("create.duplicate@netra.local", NetraRoles.SystemAdministrator);
        var before = await AccountCountAsync();

        var response = await client.PostAsync("/Users/Create", AuthenticationScreenTests.Form(
            ("FirstName", "New"),
            ("LastName", "Account"),
            ("UserName", taken),
            ("Email", "fresh.email@netra.local"),
            ("Roles", NetraRoles.Investigator),
            ("Password", Password),
            ("ConfirmPassword", Password),
            ("__RequestVerificationToken", await TokenAsync(client, "/Users/Create"))));

        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("That username is already taken.");
        html.Should().Contain("netra-field--invalid");
        (await AccountCountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Create_Post_DuplicateEmailIsRefused()
    {
        const string takenEmail = "duplicate.email@netra.local";
        await SeedAccountAsync("some.other.signin@netra.local", "Existing", "Holder", takenEmail);

        var client = await SignInAsync("create.duplicateemail@netra.local", NetraRoles.SystemAdministrator);
        var before = await AccountCountAsync();

        var response = await client.PostAsync("/Users/Create", AuthenticationScreenTests.Form(
            ("FirstName", "New"),
            ("LastName", "Account"),
            ("UserName", "unique.signin@netra.local"),
            ("Email", takenEmail),
            ("Roles", NetraRoles.Investigator),
            ("Password", Password),
            ("ConfirmPassword", Password),
            ("__RequestVerificationToken", await TokenAsync(client, "/Users/Create"))));

        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("That email address is already registered.");
        (await AccountCountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Create_Post_DuplicateBadgeNumberIsRefused()
    {
        await SeedAccountAsync("badge.owner@netra.local", "Badge", "Owner",
            "badge.owner@netra.local", "BADGE-30030");

        var client = await SignInAsync("create.badge@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync("/Users/Create", AuthenticationScreenTests.Form(
            ("FirstName", "Second"),
            ("LastName", "Holder"),
            ("UserName", "second.holder@netra.local"),
            ("Email", "second.holder@netra.local"),
            ("BadgeNumber", "BADGE-30030"),
            ("Roles", NetraRoles.Investigator),
            ("Password", Password),
            ("ConfirmPassword", Password),
            ("__RequestVerificationToken", await TokenAsync(client, "/Users/Create"))));

        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("Another account already uses that badge number.");
        (await AccountByNameAsync("second.holder@netra.local")).Should().BeNull();
    }

    [Fact]
    public async Task Create_Post_WithoutRolesIsRefused()
    {
        var client = await SignInAsync("create.noroles@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync("/Users/Create", AuthenticationScreenTests.Form(
            ("FirstName", "Role"),
            ("LastName", "Less"),
            ("UserName", "role.less@netra.local"),
            ("Email", "role.less@netra.local"),
            ("Password", Password),
            ("ConfirmPassword", Password),
            ("__RequestVerificationToken", await TokenAsync(client, "/Users/Create"))));

        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("Select at least one role.");
        (await AccountByNameAsync("role.less@netra.local")).Should().BeNull();
    }

    [Fact]
    public async Task Create_Post_UnknownRoleIsRefused()
    {
        var client = await SignInAsync("create.unknownrole@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync("/Users/Create", AuthenticationScreenTests.Form(
            ("FirstName", "Ghost"),
            ("LastName", "Operative"),
            ("UserName", "ghost.operative@netra.local"),
            ("Email", "ghost.operative@netra.local"),
            ("Roles", "Ghost Operative"),
            ("Password", Password),
            ("ConfirmPassword", Password),
            ("__RequestVerificationToken", await TokenAsync(client, "/Users/Create"))));

        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("Unknown role: Ghost Operative.");
        (await AccountByNameAsync("ghost.operative@netra.local")).Should().BeNull();
    }

    [Fact]
    public async Task Create_Post_WeakPasswordIsRefusedByIdentity()
    {
        var client = await SignInAsync("create.weak@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync("/Users/Create", AuthenticationScreenTests.Form(
            ("FirstName", "Weak"),
            ("LastName", "Password"),
            ("UserName", "weak.password@netra.local"),
            ("Email", "weak.password@netra.local"),
            ("Roles", NetraRoles.Investigator),
            ("Password", "short"),
            ("ConfirmPassword", "short"),
            ("__RequestVerificationToken", await TokenAsync(client, "/Users/Create"))));

        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("netra-field--invalid");
        (await AccountByNameAsync("weak.password@netra.local")).Should().BeNull();
    }

    [Fact]
    public async Task Create_Post_MismatchedConfirmationIsRefusedBeforeTheService()
    {
        var client = await SignInAsync("create.mismatch@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync("/Users/Create", AuthenticationScreenTests.Form(
            ("FirstName", "Mismatch"),
            ("LastName", "Checker"),
            ("UserName", "mismatch.checker@netra.local"),
            ("Email", "mismatch.checker@netra.local"),
            ("Roles", NetraRoles.Investigator),
            ("Password", Password),
            ("ConfirmPassword", "DifferentPassw0rd!"),
            ("__RequestVerificationToken", await TokenAsync(client, "/Users/Create"))));

        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("The two passwords do not match.");
        (await AccountByNameAsync("mismatch.checker@netra.local")).Should().BeNull();
    }

    [Fact]
    public async Task Create_Post_MissingRequiredNameIsRefused()
    {
        var client = await SignInAsync("create.noname@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync("/Users/Create", AuthenticationScreenTests.Form(
            ("FirstName", "   "),
            ("LastName", "Only"),
            ("UserName", "only.name@netra.local"),
            ("Email", "only.name@netra.local"),
            ("Roles", NetraRoles.Investigator),
            ("Password", Password),
            ("ConfirmPassword", Password),
            ("__RequestVerificationToken", await TokenAsync(client, "/Users/Create"))));

        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("First name is required.");
        (await AccountByNameAsync("only.name@netra.local")).Should().BeNull();
    }

    [Fact]
    public async Task Create_Post_WithoutAntiForgeryToken_IsRejectedAndWritesNothing()
    {
        var client = await SignInAsync("create.notoken@netra.local", NetraRoles.SystemAdministrator);
        var before = await AccountCountAsync();

        var response = await client.PostAsync("/Users/Create", AuthenticationScreenTests.Form(
            ("FirstName", "No"),
            ("LastName", "Token"),
            ("UserName", "no.token@netra.local"),
            ("Email", "no.token@netra.local"),
            ("Roles", NetraRoles.Investigator),
            ("Password", Password),
            ("ConfirmPassword", Password)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AccountCountAsync()).Should().Be(before);
        (await AccountByNameAsync("no.token@netra.local")).Should().BeNull();
    }

    [Fact]
    public async Task Create_Post_IgnoresIdentityStateFieldsInTheForm()
    {
        var client = await SignInAsync("create.massassign@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync("/Users/Create", AuthenticationScreenTests.Form(
            ("FirstName", "Mass"),
            ("LastName", "Assignee"),
            ("UserName", "mass.assignee@netra.local"),
            ("Email", "mass.assignee@netra.local"),
            ("Roles", NetraRoles.Investigator),
            ("Password", Password),
            ("ConfirmPassword", Password),
            ("IsActive", "false"),
            ("LockoutEnd", DateTimeOffset.UtcNow.AddYears(5).ToString("O")),
            ("EmailConfirmed", "false"),
            ("PasswordHash", "attacker-supplied-hash"),
            ("SecurityStamp", "attacker-stamp"),
            ("__RequestVerificationToken", await TokenAsync(client, "/Users/Create"))));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var created = await AccountByIdAsync(IdFromRedirect(response));

        created!.IsActive.Should().BeTrue();
        created.EmailConfirmed.Should().BeTrue("the service confirms the address itself");
        created.LockoutEnd.Should().BeNull();
        created.PasswordHash!.Should().NotBe("attacker-supplied-hash");
        created.SecurityStamp!.Should().NotBe("attacker-stamp");
        created.SecurityStamp!.Should().NotBeNullOrWhiteSpace();
    }

    // ---------------------------------------------------------------- edit

    [Fact]
    public async Task Edit_Get_KeepsSignInNameReadOnlyAndOffersNoCredentials()
    {
        var target = await SeedAccountAsync("edit.target@netra.local", "Edit", "Target",
            "edit.target@netra.local", "BADGE-21000", roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("edit.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync($"/Users/Edit/{target.Id}");

        html.Should().Contain("Edit account");
        html.Should().Contain("netra-input--readonly");
        html.Should().Contain("The sign-in name is fixed when the account is created.");
        html.Should().Contain("Save changes");
        html.Should().Contain("name=\"Email\"");
        html.Should().Contain("value=\"edit.target@netra.local\"");
        html.Should().Contain("checked=\"checked\"");

        html.Should().NotContain("name=\"UserName\"");
        html.Should().NotContain("name=\"Password\"");
        html.Should().NotContain("name=\"PasswordHash\"");
        html.Should().NotContain("name=\"SecurityStamp\"");
        html.Should().NotContain("name=\"IsActive\"");
    }

    [Fact]
    public async Task Edit_Get_OwnAccount_WarnsAboutTheProtectedChanges()
    {
        var client = await SignInAsync("self.edit@netra.local", NetraRoles.SystemAdministrator);
        var self = await AccountByNameAsync("self.edit@netra.local");

        var html = await client.GetStringAsync($"/Users/Edit/{self!.Id}");

        html.Should().Contain("This is your own account.");
        html.Should().Contain("NETRA will refuse to remove the administrator role");
    }

    [Fact]
    public async Task Edit_Post_UpdatesProfileAndWritesBeforeAndAfterAudit()
    {
        var target = await SeedAccountAsync("edit.save@netra.local", "Ed", "Save",
            "edit.save@netra.local", roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("edit.saver@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync($"/Users/Edit/{target.Id}",
            AuthenticationScreenTests.Form(
                ("FirstName", "Edward"),
                ("LastName", "Save"),
                ("Email", "edward.save@netra.local"),
                ("BadgeNumber", "BADGE-21500"),
                ("Roles", NetraRoles.Investigator),
                ("__RequestVerificationToken",
                    await TokenAsync(client, $"/Users/Edit/{target.Id}"))));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be($"/Users/Details/{target.Id}");

        var saved = await AccountByIdAsync(target.Id);
        saved!.FirstName.Should().Be("Edward");
        saved.Email!.Should().Be("edward.save@netra.local");
        saved.BadgeNumber!.Should().Be("BADGE-21500");

        var trail = await TrailAsync(target.Id, "UserEdited");
        trail.Should().ContainSingle();
        trail[0].OldValues!.Should().Contain("First name: \"Ed\"");
        trail[0].NewValues!.Should().Contain("First name: \"Edward\"");
        trail[0].OldValues!.Should().Contain("Badge number: \"(none)\"");
        trail[0].NewValues!.Should().Contain("Badge number: \"BADGE-21500\"");

        var details = await client.GetStringAsync($"/Users/Details/{target.Id}");
        details.Should().Contain("Account updated.");
    }

    [Fact]
    public async Task Edit_Post_NoOpWritesNothingToTheTrail()
    {
        var target = await SeedAccountAsync("edit.noop@netra.local", "No", "Op",
            "edit.noop@netra.local", roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("edit.noop.admin@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync($"/Users/Edit/{target.Id}",
            AuthenticationScreenTests.Form(
                ("FirstName", "No"),
                ("LastName", "Op"),
                ("Email", "edit.noop@netra.local"),
                ("Roles", NetraRoles.Investigator),
                ("__RequestVerificationToken",
                    await TokenAsync(client, $"/Users/Edit/{target.Id}"))));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await TrailAsync(target.Id, "UserEdited")).Should().BeEmpty();
        (await TrailAsync(target.Id, "UserRoleChanged")).Should().BeEmpty();
    }

    [Fact]
    public async Task Edit_Post_RoleChangeIsAppliedThroughIdentityAndAudited()
    {
        var target = await SeedAccountAsync("edit.roles@netra.local", "Rita", "Roles",
            "edit.roles@netra.local", roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("edit.roles.admin@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync($"/Users/Edit/{target.Id}",
            AuthenticationScreenTests.Form(
                ("FirstName", "Rita"),
                ("LastName", "Roles"),
                ("Email", "edit.roles@netra.local"),
                ("Roles", NetraRoles.SeniorOfficer),
                ("__RequestVerificationToken",
                    await TokenAsync(client, $"/Users/Edit/{target.Id}"))));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        (await RolesOfAsync(target.Id)).Should().BeEquivalentTo(new[] { NetraRoles.SeniorOfficer });

        var trail = await TrailAsync(target.Id, "UserRoleChanged");
        trail.Should().ContainSingle();
        trail[0].PropertyName!.Should().Be("Roles");
        trail[0].OldValues!.Should().Be(NetraRoles.Investigator);
        trail[0].NewValues!.Should().Be(NetraRoles.SeniorOfficer);
    }

    [Fact]
    public async Task Edit_Post_RouteIdentifierWinsOverAForgedBodyId()
    {
        var target = await SeedAccountAsync("edit.route@netra.local", "Route", "Wins",
            "edit.route@netra.local", roles: new[] { NetraRoles.Investigator });
        var other = await SeedAccountAsync("edit.other@netra.local", "Other", "Account",
            "edit.other@netra.local", roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("edit.route.admin@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync($"/Users/Edit/{target.Id}",
            AuthenticationScreenTests.Form(
                ("Id", other.Id.ToString()),
                ("FirstName", "Retargeted"),
                ("LastName", "Wins"),
                ("Email", "retargeted@netra.local"),
                ("Roles", NetraRoles.Investigator),
                ("__RequestVerificationToken",
                    await TokenAsync(client, $"/Users/Edit/{target.Id}"))));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be($"/Users/Details/{target.Id}");

        (await AccountByIdAsync(target.Id))!.FirstName.Should().Be("Retargeted");
        (await AccountByIdAsync(other.Id))!.FirstName.Should().Be("Other");
    }

    [Fact]
    public async Task Deactivate_Post_ForgedBodyIdCannotAimAtAnotherAccount()
    {
        var intended = await SeedAccountAsync("spoil.target@netra.local", "Spoiled", "Intended",
            "spoil.target@netra.local", roles: new[] { NetraRoles.Investigator });
        var decoy = await SeedAccountAsync("spoil.decoy@netra.local", "Spoiled", "Decoy",
            "spoil.decoy@netra.local", roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("spoil.admin@netra.local", NetraRoles.SystemAdministrator);

        // The URL names the decoy while the form tries to aim the change at the other account.
        var response = await client.PostAsync($"/Users/Deactivate/{decoy.Id}",
            AuthenticationScreenTests.Form(
                ("Id", intended.Id.ToString()),
                ("__RequestVerificationToken",
                    await TokenAsync(client, $"/Users/Details/{decoy.Id}"))));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be($"/Users/Details/{decoy.Id}");

        (await AccountByIdAsync(decoy.Id))!.IsActive.Should().BeFalse();
        (await AccountByIdAsync(intended.Id))!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Edit_Post_SelfDemotionIsRefusedAndShownToTheOperator()
    {
        var client = await SignInAsync("self.demote@netra.local", NetraRoles.SystemAdministrator);
        var self = await AccountByNameAsync("self.demote@netra.local");

        var response = await client.PostAsync($"/Users/Edit/{self!.Id}",
            AuthenticationScreenTests.Form(
                ("FirstName", "Self"),
                ("LastName", "Demote"),
                ("Email", "self.demote@netra.local"),
                ("Roles", NetraRoles.Investigator),
                ("__RequestVerificationToken",
                    await TokenAsync(client, $"/Users/Edit/{self.Id}"))));

        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("You cannot remove the administrator role from your own account.");
        (await RolesOfAsync(self.Id)).Should().Contain(NetraRoles.SystemAdministrator);

        var trail = await TrailAsync(self.Id, "UserAccessDenied");
        trail.Should().ContainSingle();
        trail[0].PropertyName!.Should().Be("Roles");
    }

    [Fact]
    public async Task Edit_Post_RemovingTheLastAdministratorIsRefused()
    {
        var client = await SignInAsync("last.demote@netra.local", NetraRoles.SystemAdministrator);

        // Leave exactly one holder for the guardrail to protect.
        await StripAllAdministratorsAsync();

        var peer = await SeedAccountAsync("last.peer.demote@netra.local", "Peer", "Administrator",
            "last.peer.demote@netra.local", roles: new[] { NetraRoles.SystemAdministrator });

        var response = await client.PostAsync($"/Users/Edit/{peer.Id}",
            AuthenticationScreenTests.Form(
                ("FirstName", "Peer"),
                ("LastName", "Administrator"),
                ("Email", "last.peer.demote@netra.local"),
                ("Roles", NetraRoles.Investigator),
                ("__RequestVerificationToken",
                    await TokenAsync(client, $"/Users/Edit/{peer.Id}"))));

        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("This is the last administrator account.");
        (await RolesOfAsync(peer.Id)).Should().Contain(NetraRoles.SystemAdministrator);
    }

    [Fact]
    public async Task Edit_Post_UnknownAccount_RedirectsToList()
    {
        var client = await SignInAsync("edit.missing@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync(
            "/Users/Edit/11111111-1111-1111-1111-111111111111",
            AuthenticationScreenTests.Form(
                ("FirstName", "Nobody"),
                ("LastName", "Here"),
                ("Email", "nobody.here@netra.local"),
                ("Roles", NetraRoles.Investigator),
                ("__RequestVerificationToken", await TokenAsync(client, "/Users/Create"))));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/Users");
    }

    [Fact]
    public async Task Edit_Post_WithoutAntiForgeryToken_ChangesNothing()
    {
        var target = await SeedAccountAsync("edit.notoken@netra.local", "No", "Token",
            "edit.notoken@netra.local", roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("edit.notoken.admin@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync($"/Users/Edit/{target.Id}",
            AuthenticationScreenTests.Form(
                ("FirstName", "Hacked"),
                ("LastName", "Token"),
                ("Email", "edit.notoken@netra.local"),
                ("Roles", NetraRoles.SystemAdministrator)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AccountByIdAsync(target.Id))!.FirstName.Should().Be("No");
    }

    [Fact]
    public async Task Edit_Post_DuplicateEmailIsRefusedAndNothingIsRenamed()
    {
        var target = await SeedAccountAsync("edit.clash@netra.local", "Clash", "Target",
            "edit.clash@netra.local", roles: new[] { NetraRoles.Investigator });
        await SeedAccountAsync("edit.owner@netra.local", "Clash", "Owner",
            "edit.owner@netra.local", roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("edit.clash.admin@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync($"/Users/Edit/{target.Id}",
            AuthenticationScreenTests.Form(
                ("FirstName", "Clash"),
                ("LastName", "Target"),
                ("Email", "edit.owner@netra.local"),
                ("Roles", NetraRoles.Investigator),
                ("__RequestVerificationToken",
                    await TokenAsync(client, $"/Users/Edit/{target.Id}"))));

        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Another account already uses that email address.");
        (await AccountByIdAsync(target.Id))!.Email!.Should().Be("edit.clash@netra.local");
    }

    // ---------------------------------------------------------------- account state

    [Fact]
    public async Task Deactivate_Post_SuspendsSignInsAuditsAndActivateReversesIt()
    {
        const string userName = "state.target@netra.local";
        await _factory.SeedUserAsync(userName, Password, NetraRoles.Investigator);
        var target = await AccountByNameAsync(userName);

        var client = await SignInAsync("state.admin@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync($"/Users/Deactivate/{target!.Id}",
            AuthenticationScreenTests.Form(
                ("__RequestVerificationToken",
                    await TokenAsync(client, $"/Users/Details/{target.Id}"))));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be($"/Users/Details/{target.Id}");

        var after = await AccountByIdAsync(target.Id);
        after!.IsActive.Should().BeFalse();
        after.SecurityStamp.Should().NotBe(target.SecurityStamp);

        var trail = await TrailAsync(target.Id, "UserDeactivated");
        trail.Should().ContainSingle();
        trail[0].PropertyName!.Should().Be("IsActive");
        trail[0].OldValues!.Should().Be("True");
        trail[0].NewValues!.Should().Be("False");

        var details = await client.GetStringAsync($"/Users/Details/{target.Id}");
        details.Should().Contain("Account deactivated. Sign-in is now refused for this account.");

        // The correct password is no longer enough to get in.
        var (_, blockedLogin) = await LoginAttemptAsync(userName, Password);
        blockedLogin.StatusCode.Should().Be(HttpStatusCode.OK);
        (await blockedLogin.Content.ReadAsStringAsync())
            .Should().Contain("This account is not active. Contact your administrator.");

        var reactivate = await client.PostAsync($"/Users/Activate/{target.Id}",
            AuthenticationScreenTests.Form(
                ("__RequestVerificationToken",
                    await TokenAsync(client, $"/Users/Details/{target.Id}"))));

        reactivate.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await AccountByIdAsync(target.Id))!.IsActive.Should().BeTrue();
        (await TrailAsync(target.Id, "UserActivated")).Should().ContainSingle();

        var (_, restoredLogin) = await LoginAttemptAsync(userName, Password);
        restoredLogin.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var detailsAgain = await client.GetStringAsync($"/Users/Details/{target.Id}");
        detailsAgain.Should().Contain("Account activated. The user can sign in again.");
        detailsAgain.Should().Contain("Deactivate account");
    }

    [Fact]
    public async Task Deactivate_PostOwnAccount_IsRefusedAndAuditedAsADeniedAttempt()
    {
        var client = await SignInAsync("self.deactivate@netra.local", NetraRoles.SystemAdministrator);
        var self = await AccountByNameAsync("self.deactivate@netra.local");

        var response = await client.PostAsync($"/Users/Deactivate/{self!.Id}",
            AuthenticationScreenTests.Form(
                ("__RequestVerificationToken",
                    await TokenAsync(client, $"/Users/Details/{self.Id}"))));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be($"/Users/Details/{self.Id}");

        (await AccountByIdAsync(self.Id))!.IsActive.Should().BeTrue();

        var details = await client.GetStringAsync($"/Users/Details/{self.Id}");
        details.Should().Contain("You cannot deactivate your own account while signed in.");

        var trail = await TrailAsync(self.Id, "UserAccessDenied");
        trail.Should().ContainSingle();
        trail[0].PropertyName!.Should().Be("IsActive");
    }

    [Fact]
    public async Task AccountState_NeverChangesThroughGet()
    {
        var target = await SeedAccountAsync("get.state@netra.local", "Get", "State",
            "get.state@netra.local", roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("get.state.admin@netra.local", NetraRoles.SystemAdministrator);

        var deactivate = await client.GetAsync($"/Users/Deactivate/{target.Id}");
        var activate = await client.GetAsync($"/Users/Activate/{target.Id}");

        // The routes are POST-only: the request never reaches a state-changing action.
        deactivate.StatusCode.Should().BeOneOf(
            HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
        activate.StatusCode.Should().BeOneOf(
            HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);

        (await AccountByIdAsync(target.Id))!.IsActive.Should().BeTrue();
        (await TrailAsync(target.Id, "UserDeactivated")).Should().BeEmpty();
    }

    [Fact]
    public async Task Deactivate_UnknownAccount_RedirectsWithAMessageAndWritesNothing()
    {
        var client = await SignInAsync("state.unknown@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.PostAsync(
            "/Users/Deactivate/11111111-1111-1111-1111-111111111111",
            AuthenticationScreenTests.Form(
                ("__RequestVerificationToken", await TokenAsync(client, "/Users"))));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/Users");

        var list = await client.GetStringAsync("/Users");
        list.Should().Contain("No account with that identifier exists.");

        (await TrailAsync(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), "UserDeactivated"))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Deactivate_LastAdministrator_IsRefused()
    {
        var client = await SignInAsync("last.admin@netra.local", NetraRoles.SystemAdministrator);

        // Leave exactly one holder for the guardrail to protect.
        await StripAllAdministratorsAsync();

        var peer = await SeedAccountAsync("last.peer.state@netra.local", "Peer", "Administrator",
            "last.peer.state@netra.local", roles: new[] { NetraRoles.SystemAdministrator });

        var response = await client.PostAsync($"/Users/Deactivate/{peer.Id}",
            AuthenticationScreenTests.Form(
                ("__RequestVerificationToken",
                    await TokenAsync(client, $"/Users/Details/{peer.Id}"))));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await AccountByIdAsync(peer.Id))!.IsActive.Should().BeTrue();

        var details = await client.GetStringAsync($"/Users/Details/{peer.Id}");
        details.Should().Contain("This is the last administrator account.");
    }

    // ---------------------------------------------------------------- no client-side logic

    [Fact]
    public async Task UserManagementPages_ContainNoJavaScript()
    {
        var target = await SeedAccountAsync("nojs.target@netra.local", "No", "Js",
            "nojs.target@netra.local", roles: new[] { NetraRoles.Investigator });

        var client = await SignInAsync("nojs.admin@netra.local", NetraRoles.SystemAdministrator);

        var paths = new[]
        {
            "/Users",
            "/Users/Create",
            $"/Users/Details/{target.Id}",
            $"/Users/Edit/{target.Id}"
        };

        foreach (var path in paths)
        {
            var html = await client.GetStringAsync(path);

            html.Should().NotContain("<script");
            html.Should().NotContain("</script");
            html.Should().NotContain("javascript:");
            html.Should().NotContain("onclick");
            html.Should().NotContain("onchange");
            html.Should().NotContain("onsubmit");
            html.Should().NotContain("fetch(");
            html.Should().NotContain("XMLHttpRequest");
            html.Should().NotContain(".js\"");
        }
    }
}
