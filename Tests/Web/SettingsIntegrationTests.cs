using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Application.Abstractions;
using NETRA.Domain.Settings;
using NETRA.Infrastructure.Data;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// End-to-end checks for the Settings module. Every route runs through the real MVC pipeline,
/// so authorisation, anti-forgery, model binding, <c>SettingsService</c>, Identity and the audit
/// trail are the production implementations.
/// </summary>
/// <remarks>
/// The tests assert what a browser would receive: status codes, the rendered HTML of the forms,
/// the rows written to the store and the entries left in the audit trail. The database is EF
/// Core's in-memory provider, not a live SQL Server instance.
/// </remarks>
public class SettingsIntegrationTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";
    private const string NewPassword = "RotationPassw0rd#7";
    private const string Admin = "System Administrator";
    private const string Investigator = "Investigator";
    private const string Analyst = "Police / Intelligence Analyst";

    private static readonly string[] PersonalPages =
    ["/settings", "/settings/account", "/settings/security", "/settings/notifications", "/settings/appearance"];

    private readonly NetraWebFactory _factory;

    public SettingsIntegrationTests(NetraWebFactory factory)
    {
        _factory = factory;
    }

    // ------------------------------------------------------------------------ helpers

    private static string Email(string label) => $"{label}.{Guid.NewGuid():N}@netra.local";

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Seeds an account, signs it in through the real login form, and returns its client.</summary>
    private async Task<HttpClient> SignInAsync(string email, params string[] roles)
    {
        await _factory.SeedUserAsync(email, Password, roles);

        var client = CreateClient();
        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/Account/Login"));

        var response = await client.PostAsync("/Account/Login", AuthenticationScreenTests.Form(
            ("UserName", email),
            ("Password", Password),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect, "the fixture account must sign in");
        return client;
    }

    /// <summary>
    /// Reads the page so the request carries the form's own anti-forgery token, then posts the
    /// fields exactly as a browser would.
    /// </summary>
    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string path, params (string Key, string Value)[] fields)
    {
        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(await client.GetStringAsync(path));

        var pairs = fields
            .Select(field => new KeyValuePair<string, string>(field.Key, field.Value))
            .ToList();
        pairs.Add(new KeyValuePair<string, string>("__RequestVerificationToken", token));

        return await client.PostAsync(path, new FormUrlEncodedContent(pairs));
    }

    private async Task<NetraDbContext> ContextAsync()
    {
        var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<NetraDbContext>();
    }

    private async Task<Guid> UserIdAsync(string email)
    {
        await using var context = await ContextAsync();
        return await context.Users.Where(u => u.UserName == email).Select(u => u.Id).SingleAsync();
    }

    /// <summary>The settings actions written to the trail for one account, oldest first.</summary>
    private async Task<List<(string Action, string? Property, string? Old, string? New)>> TrailAsync(
        Guid userId, params string[] actions)
    {
        await using var context = await ContextAsync();
        var entries = await context.AuditLogs.AsNoTracking()
            .Where(entry => entry.EntityId == userId && actions.Contains(entry.Action))
            .OrderBy(entry => entry.Timestamp)
            .ToListAsync();

        return [.. entries.Select(entry =>
            (entry.Action, entry.PropertyName, entry.OldValues, entry.NewValues))];
    }

    /// <summary>The rendered <c>class</c> attribute of the page body, which carries the stored theme choice.</summary>
    private static string BodyClass(string html) =>
        Regex.Match(html, "<body class=\"(?<c>[^\"]*)\"").Groups["c"].Value;

    // ------------------------------------------------------------------ anonymous: §11, §13

    [Theory]
    [InlineData("/settings")]
    [InlineData("/settings/account")]
    [InlineData("/settings/security")]
    [InlineData("/settings/notifications")]
    [InlineData("/settings/appearance")]
    [InlineData("/settings/system-preferences")]
    public async Task Anonymous_Get_IsRedirectedToSignIn(string path)
    {
        var client = CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().StartWith("/Account/Login");
    }

    [Theory]
    [InlineData("/settings/account")]
    [InlineData("/settings/security")]
    [InlineData("/settings/notifications")]
    [InlineData("/settings/appearance")]
    public async Task Anonymous_Post_IsRefusedBeforeItCanBind(string path)
    {
        var client = CreateClient();

        var response = await client.PostAsync(path, AuthenticationScreenTests.Form(("FirstName", "Someone")));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().StartWith("/Account/Login");
    }

    // ---------------------------------------------------------------------- authorization

    [Theory]
    [InlineData(Investigator)]
    [InlineData(Analyst)]
    [InlineData("Senior Officer")]
    public async Task SystemSettings_NonAdministratorsAreForbidden(string role)
    {
        var email = Email($"nonsys.{role.Split(' ')[0]}");
        var client = await SignInAsync(email, role);

        var response = await client.GetAsync("/settings/system-preferences");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SystemSettings_AdministratorsAreServed()
    {
        var client = await SignInAsync(Email("sys.admin"), Admin);

        var html = await client.GetStringAsync("/settings/system-preferences");

        html.Should().Contain("System preferences");
        html.Should().Contain("are not read or displayed here");
    }

    [Theory]
    [InlineData("/settings/account")]
    [InlineData("/settings/security")]
    [InlineData("/settings/notifications")]
    [InlineData("/settings/appearance")]
    public async Task PersonalSettings_AreOpenToAnySignedInRole(string path)
    {
        var client = await SignInAsync(Email("personal.investigator"), Investigator);

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SettingsOverview_HidesTheSystemCardFromNonAdministrators()
    {
        var investigator = await SignInAsync(Email("overview.investigator"), Investigator);
        var administrator = await SignInAsync(Email("overview.admin"), Admin);

        (await investigator.GetStringAsync("/settings"))
            .Should().NotContain("/settings/system-preferences");

        var adminHtml = await administrator.GetStringAsync("/settings");
        adminHtml.Should().Contain("/settings/system-preferences");
        adminHtml.Should().Contain("/settings/account");
        adminHtml.Should().Contain("/support");
    }

    // ---------------------------------------------------------------- account / profile

    [Fact]
    public async Task Account_RendersIdentityFieldsReadAndUnpostable()
    {
        var email = Email("account.readonly");
        var client = await SignInAsync(email, Investigator);

        var html = await client.GetStringAsync("/settings/account");

        html.Should().Contain($"value=\"{email}\"");
        html.Should().Contain("netra-input--readonly");
        html.Should().Contain("name=\"FirstName\"");

        // Nothing that identifies or gates the account is a form field.
        html.Should().NotContain("name=\"UserName\"");
        html.Should().NotContain("name=\"Email\"");
        html.Should().NotContain("name=\"UserId\"");
        html.Should().NotContain("name=\"IsActive\"");
        html.Should().NotContain("PasswordHash");
        html.Should().NotContain("SecurityStamp");
        html.Should().NotContain("ConcurrencyStamp");
    }

    [Fact]
    public async Task Account_Post_UpdatesTheProfileAndRecordsEachField()
    {
        var email = Email("account.update");
        var client = await SignInAsync(email, Investigator);
        var userId = await UserIdAsync(email);

        var response = await PostAsync(client, "/settings/account",
            ("FirstName", "Ananya"),
            ("LastName", "Bose"),
            ("BadgeNumber", "MH-2210"),
            ("Rank", "Senior PSIC"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/settings/account");

        var html = await client.GetStringAsync("/settings/account");
        html.Should().Contain("Profile updated.");
        html.Should().Contain("value=\"MH-2210\"");

        await using (var context = await ContextAsync())
        {
            var user = await context.Users.SingleAsync(u => u.Id == userId);
            user.FirstName.Should().Be("Ananya");
            user.Rank.Should().Be("Senior PSIC");
        }

        var trail = await TrailAsync(userId, AuditActionNames.UserEdited);
        trail.Should().Contain(entry => entry.Property == "BadgeNumber"
            && entry.Old == null
            && entry.New == "MH-2210");
        trail.Should().Contain(entry => entry.Property == "FirstName"
            && entry.Old == "Test"
            && entry.New == "Ananya");
    }

    [Theory]
    [InlineData("", "Ananya", "First name is required.")]
    [InlineData("Ananya", "", "Last name is required.")]
    public async Task Account_Post_RejectsMissingNamesButKeepsWhatWasTyped(
        string firstName, string lastName, string expectedMessage)
    {
        var email = Email("account.invalid");
        var client = await SignInAsync(email, Investigator);

        var html = await (await PostAsync(client, "/settings/account",
            ("FirstName", firstName),
            ("LastName", lastName),
            ("BadgeNumber", "KEPT-1"),
            ("Rank", "PSI"))).Content.ReadAsStringAsync();

        html.Should().Contain("netra-field__error");
        html.Should().Contain(expectedMessage);
        html.Should().Contain("value=\"KEPT-1\"");
    }

    [Fact]
    public async Task Account_Post_RejectsAnOverlongBadgeOnTheServer()
    {
        var email = Email("account.longbadge");
        var client = await SignInAsync(email, Investigator);

        var html = await (await PostAsync(client, "/settings/account",
            ("FirstName", "Ananya"),
            ("LastName", "Bose"),
            ("BadgeNumber", new string('B', 51)))).Content.ReadAsStringAsync();

        html.Should().Contain("Badge number must be 50 characters or fewer.");

        await using var context = await ContextAsync();
        (await context.Users.Where(u => u.UserName == email && u.BadgeNumber != null)
            .AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Account_Post_RefusesABadgeAnotherAccountAlreadyHas()
    {
        var email = Email("account.clash");
        var other = Email("account.holder");
        var client = await SignInAsync(email, Investigator);
        await _factory.SeedUserAsync(other, Password, Analyst);

        var otherId = await UserIdAsync(other);
        await using (var context = await ContextAsync())
        {
            var holder = await context.Users.SingleAsync(u => u.Id == otherId);
            holder.BadgeNumber = "MH-7781";
            await context.SaveChangesAsync();
        }

        var posted = await PostAsync(client, "/settings/account",
            ("FirstName", "Ananya"),
            ("LastName", "Bose"),
            ("BadgeNumber", "MH-7781"));
        var html = await posted.Content.ReadAsStringAsync();

        posted.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Another account already uses that badge number.");
        html.Should().Contain("netra-field--invalid");

        await using var check = await ContextAsync();
        (await check.Users.Where(u => u.UserName == email && u.FirstName == "Ananya")
            .AnyAsync()).Should().BeFalse("a refused write must not change the account");
    }

    [Fact]
    public async Task Account_Post_IgnoresForgedIdentityRoleAndOwnershipFields()
    {
        var email = Email("account.forged");
        var client = await SignInAsync(email, Investigator);
        var userId = await UserIdAsync(email);

        var response = await PostAsync(client, "/settings/account",
            ("FirstName", "Ananya"),
            ("LastName", "Bose"),
            ("UserId", Guid.NewGuid().ToString()),
            ("Id", Guid.NewGuid().ToString()),
            ("UserName", "attacker@netra.local"),
            ("Email", "attacker@netra.local"),
            ("IsActive", "false"),
            ("Roles", Admin),
            ("PasswordHash", "forged"),
            ("SecurityStamp", "forged"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        await using var context = await ContextAsync();
        var user = await context.Users.SingleAsync(u => u.Id == userId);
        user.FirstName.Should().Be("Ananya");
        user.UserName.Should().Be(email);
        user.Email.Should().Be(email);
        user.IsActive.Should().BeTrue();
        user.PasswordHash.Should().NotBe("forged");
        user.SecurityStamp.Should().NotBe("forged");

        // Only the role the fixture granted survives: the posted Roles value changed nothing.
        var roleNames = await context.UserRoles
            .Where(link => link.UserId == userId)
            .Join(context.Roles, link => link.RoleId, role => role.Id, (_, role) => role.Name)
            .ToListAsync();
        roleNames.Should().BeEquivalentTo([Investigator]);

        // The write is aimed at the authenticated account, never at the forged UserId, and it
        // records only the editable fields.
        (await TrailAsync(userId, AuditActionNames.UserEdited))
            .Select(entry => entry.Property)
            .Should()
            .BeEquivalentTo(["FirstName", "LastName"]);
    }

    [Fact]
    public async Task Account_Post_WithoutAntiForgeryToken_IsRejected()
    {
        var client = await SignInAsync(Email("account.token"), Investigator);

        var response = await client.PostAsync("/settings/account", AuthenticationScreenTests.Form(
            ("FirstName", "Ananya"), ("LastName", "Bose")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------------ security

    [Fact]
    public async Task Security_RendersThePolicyIdentityEnforcesAndNoStoredCredential()
    {
        var client = await SignInAsync(Email("security.get"), Investigator);

        var html = await client.GetStringAsync("/settings/security");

        html.Should().Contain("name=\"CurrentPassword\"");
        html.Should().Contain("name=\"NewPassword\"");
        html.Should().Contain("name=\"ConfirmPassword\"");
        html.Should().Contain("type=\"password\"");
        html.Should().Contain("autocomplete=\"current-password\"");
        html.Should().Contain("at least 8 characters");
        html.Should().NotContain(Password);
        html.Should().NotContain("PasswordHash");
        html.Should().NotContain("SecurityStamp");
    }

    [Fact]
    public async Task Security_Post_ChangesThePasswordAndKeepsThisSessionSignedIn()
    {
        var email = Email("security.change");
        var client = await SignInAsync(email, Investigator);
        var userId = await UserIdAsync(email);

        var response = await PostAsync(client, "/settings/security",
            ("CurrentPassword", Password),
            ("NewPassword", NewPassword),
            ("ConfirmPassword", NewPassword));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // The stamp rotated, yet the current browser keeps working because the cookie was re-issued.
        var html = await client.GetStringAsync("/settings/security");
        html.Should().Contain("Password changed.");
        html.Should().NotContain(Password);
        html.Should().NotContain(NewPassword);

        // The new credential signs in; the old one is refused.
        var fresh = CreateClient();
        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await fresh.GetStringAsync("/Account/Login"));
        var signedIn = await fresh.PostAsync("/Account/Login", AuthenticationScreenTests.Form(
            ("UserName", email),
            ("Password", NewPassword),
            ("__RequestVerificationToken", token)));
        signedIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        signedIn.Headers.Location!.ToString().Should().NotContain("Login");

        var stale = CreateClient();
        token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await stale.GetStringAsync("/Account/Login"));
        var refused = await stale.PostAsync("/Account/Login", AuthenticationScreenTests.Form(
            ("UserName", email),
            ("Password", Password),
            ("__RequestVerificationToken", token)));
        // A refused credential re-renders the sign-in page: no session, no redirect onward.
        refused.StatusCode.Should().Be(HttpStatusCode.OK,
            "the previous password no longer signs in");
        var stillLockedOutOfTheApp = await stale.GetAsync("/settings");
        stillLockedOutOfTheApp.Headers.Location!.AbsolutePath
            .Should().StartWith("/Account/Login");

        var trail = await TrailAsync(userId, AuditActionNames.PasswordChanged);
        var entry = trail.Should().ContainSingle().Subject;
        entry.Old.Should().BeNull();
        entry.New.Should().Be("Password changed by the account owner.");
    }

    [Fact]
    public async Task Security_Post_WithTheWrongCurrentPassword_EchoesNoSecret()
    {
        var email = Email("security.wrongcurrent");
        var client = await SignInAsync(email, Investigator);
        var userId = await UserIdAsync(email);

        var html = await (await PostAsync(client, "/settings/security",
            ("CurrentPassword", "NotMyRealPassword!"),
            ("NewPassword", NewPassword),
            ("ConfirmPassword", NewPassword))).Content.ReadAsStringAsync();

        // Identity supplies the wording; the controller only decides which box carries it.
        html.Should().Contain("Incorrect password.");
        html.Should().Contain("field-validation-error");
        html.Should().NotContain("NotMyRealPassword!");
        html.Should().NotContain(NewPassword);

        (await TrailAsync(userId, AuditActionNames.PasswordChanged)).Should().BeEmpty();

        // The original credential still works, so nothing was half-applied.
        var fresh = CreateClient();
        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await fresh.GetStringAsync("/Account/Login"));
        var response = await fresh.PostAsync("/Account/Login", AuthenticationScreenTests.Form(
            ("UserName", email),
            ("Password", Password),
            ("__RequestVerificationToken", token)));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Security_Post_BlankFieldsAreReportedAgainstEachBox()
    {
        var client = await SignInAsync(Email("security.blank"), Investigator);

        var response = await PostAsync(client, "/settings/security",
            ("CurrentPassword", ""), ("NewPassword", ""), ("ConfirmPassword", ""));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Enter your current password.");
        html.Should().Contain("Enter a new password.");
        html.Should().Contain("Re-enter the new password.");
    }

    [Fact]
    public async Task Security_Post_WeakNewPasswordIsRejectedByIdentitysRules()
    {
        var client = await SignInAsync(Email("security.weak"), Investigator);

        var html = await (await PostAsync(client, "/settings/security",
            ("CurrentPassword", Password),
            ("NewPassword", "weak"),
            ("ConfirmPassword", "weak"))).Content.ReadAsStringAsync();

        html.Should().Contain("at least 8");
        html.Should().NotContain("weak\"");
    }

    [Fact]
    public async Task Security_Post_WithoutAntiForgeryToken_IsRejected()
    {
        var client = await SignInAsync(Email("security.token"), Investigator);

        var response = await client.PostAsync("/settings/security", AuthenticationScreenTests.Form(
            ("CurrentPassword", Password), ("NewPassword", NewPassword), ("ConfirmPassword", NewPassword)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -------------------------------------------------------------------- notifications

    [Fact]
    public async Task Notifications_RenderEveryRealCategoryAsEnabledByDefault()
    {
        var client = await SignInAsync(Email("notifications.default"), Investigator);

        var html = await client.GetStringAsync("/settings/notifications");

        html.Should().Contain("Crime Intelligence Alert");
        html.Should().Contain("Investigation Alert");
        html.Should().Contain("System Notification");
        html.Should().Contain("name=\"EnabledCategories\" value=\"System\"");
        html.Should().Contain("Default");

        (await client.GetStringAsync("/settings")).Should().Contain("3 of 3 enabled");
    }

    [Fact]
    public async Task Notifications_Post_DisablesOneCategoryAndStoresTheChoice()
    {
        var email = Email("notifications.disable");
        var client = await SignInAsync(email, Investigator);
        var userId = await UserIdAsync(email);

        var response = await PostAsync(client, "/settings/notifications",
            ("EnabledCategories", "CrimeIntelligence"),
            ("EnabledCategories", "Investigation"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var html = await client.GetStringAsync("/settings/notifications");
        html.Should().Contain("Notification preferences updated.");
        html.Should().Contain("Your choice");
        Regex.IsMatch(html, "id=\"cat-System\"(?![^>]*checked)").Should()
            .BeTrue("the disabled category must render as an unchecked box");

        await using var context = await ContextAsync();
        var rows = await context.NotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync();
        var row = rows.Should().ContainSingle().Subject;
        row.NotificationType.Should().Be(3);
        row.DeliveryChannel.Should().Be(1);
        row.IsEnabled.Should().BeFalse();

        (await client.GetStringAsync("/settings")).Should().Contain("2 of 3 enabled");

        var trail = await TrailAsync(userId, AuditActionNames.NotificationPreferencesUpdated);
        trail.Should().ContainSingle().Which.Property.Should().Be("Notifications.System");
    }

    [Fact]
    public async Task Notifications_Post_WithNoBoxesCheckedDisablesEveryCategory()
    {
        var email = Email("notifications.none");
        var client = await SignInAsync(email, Investigator);
        var userId = await UserIdAsync(email);

        await PostAsync(client, "/settings/notifications");

        await using var context = await ContextAsync();
        var rows = await context.NotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync();

        rows.Should().HaveCount(3);
        rows.Should().AllSatisfy(row => row.IsEnabled.Should().BeFalse());

        var html = await client.GetStringAsync("/settings/notifications");
        Regex.IsMatch(html, "id=\"cat-System\"(?![^>]*checked)").Should().BeTrue();
    }

    [Fact]
    public async Task Notifications_Post_UnknownCategoryIsRefusedAndWritesNothing()
    {
        var email = Email("notifications.unknown");
        var client = await SignInAsync(email, Investigator);
        var userId = await UserIdAsync(email);

        var response = await PostAsync(client, "/settings/notifications",
            ("EnabledCategories", "CrimeIntelligence"),
            ("EnabledCategories", "NotACategory"));

        // The binder refuses the value, so the post never reaches the store.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("is not valid");

        await using var context = await ContextAsync();
        (await context.NotificationPreferences
            .Where(p => p.UserId == userId)
            .AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Notifications_Post_WithoutAntiForgeryToken_IsRejected()
    {
        var client = await SignInAsync(Email("notifications.token"), Investigator);

        var response = await client.PostAsync("/settings/notifications",
            AuthenticationScreenTests.Form(("EnabledCategories", "System")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------------ appearance

    [Fact]
    public async Task Appearance_Post_AppliesTheStoredChoiceToTheNextRenderedPage()
    {
        var email = Email("appearance.compact");
        var client = await SignInAsync(email, Investigator);
        var userId = await UserIdAsync(email);

        var response = await PostAsync(client, "/settings/appearance",
            ("Density", "Compact"),
            ("ReducedMotion", "true"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var html = await client.GetStringAsync("/settings");
        BodyClass(html).Should().Be("netra-density--compact netra-motion--reduced");

        var appearancePage = await client.GetStringAsync("/settings/appearance");
        appearancePage.Should().Contain("Appearance updated.");
        Regex.Match(appearancePage, @"id=""density-Compact""(?<attrs>[^>]*)").Groups["attrs"].Value
            .Should().Contain("checked=\"checked\"");

        await using var context = await ContextAsync();
        var rows = await context.UserSettings.AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().AllSatisfy(row => row.Category.Should().Be("Appearance"));
        rows.Single(r => r.Key == "ui.density").Value.Should().Be("Compact");
        rows.Single(r => r.Key == "ui.reduced-motion").Value.Should().Be("True");
    }

    [Fact]
    public async Task Appearance_Post_LeavesEveryOtherOfficersPageUntouched()
    {
        var email = Email("appearance.owner");
        var other = Email("appearance.other");
        var client = await SignInAsync(email, Investigator);
        var otherClient = await SignInAsync(other, Analyst);

        await PostAsync(client, "/settings/appearance",
            ("Density", "Compact"), ("ReducedMotion", "true"));

        BodyClass(await otherClient.GetStringAsync("/settings"))
            .Should().Be("netra-density--comfortable");
    }

    [Fact]
    public async Task Appearance_Post_UncheckedMotionReturnsToFullMotion()
    {
        var email = Email("appearance.motion");
        var client = await SignInAsync(email, Investigator);

        await PostAsync(client, "/settings/appearance",
            ("Density", "Compact"), ("ReducedMotion", "true"));
        await PostAsync(client, "/settings/appearance", ("Density", "Compact"));

        BodyClass(await client.GetStringAsync("/settings"))
            .Should().Be("netra-density--compact");
    }

    [Fact]
    public async Task Appearance_Post_UnknownDensityIsRefused()
    {
        var email = Email("appearance.bogus");
        var client = await SignInAsync(email, Investigator);
        var userId = await UserIdAsync(email);

        var response = await PostAsync(client, "/settings/appearance", ("Density", "99"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // The model binder refuses a density outside the enum, so the page explains it in the
        // Density field rather than storing a value the theme cannot apply.
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("is invalid");

        await using var context = await ContextAsync();
        (await context.UserSettings
            .Where(s => s.UserId == userId)
            .AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Appearance_Post_WithoutAntiForgeryToken_IsRejected()
    {
        var client = await SignInAsync(Email("appearance.token"), Investigator);

        var response = await client.PostAsync("/settings/appearance",
            AuthenticationScreenTests.Form(("Density", "Compact")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------- no JS, no GET writes (§13, §20)

    [Fact]
    public async Task EverySettingsPage_RendersWithoutClientSideCode()
    {
        var client = await SignInAsync(Email("noscript.admin"), Admin);

        foreach (var path in PersonalPages.Append("/settings/system-preferences"))
        {
            var html = await client.GetStringAsync(path);

            html.Should().NotContain("<script");
            html.Should().NotContain("onclick=");
            html.Should().NotContain("onchange=");
            html.Should().NotContain("javascript:");
            html.Should().Contain("netra.css");
        }
    }

    [Fact]
    public async Task SystemSettingsPage_OfersNoEditingControlsAtAll()
    {
        var client = await SignInAsync(Email("system.readonly"), Admin);

        var html = await client.GetStringAsync("/settings/system-preferences");

        // Only the shell's sign-out form posts; the settings page itself has no write form.
        html.Should().NotContain("action=\"/settings/system-preferences\"");
        html.Should().NotContain("name=\"Value\"");
        html.Should().NotContain("appsettings");
        html.Should().NotContain("Server:");
    }

    [Fact]
    public async Task ReadingTheSettingsPagesChangesNothingAnywhere()
    {
        var email = Email("get.only");
        var client = await SignInAsync(email, Admin);
        var userId = await UserIdAsync(email);

        foreach (var path in PersonalPages.Append("/settings/system-preferences"))
        {
            (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using var context = await ContextAsync();
        (await context.UserSettings.Where(s => s.UserId == userId).AnyAsync()).Should().BeFalse();
        (await context.NotificationPreferences.Where(p => p.UserId == userId)
            .AnyAsync()).Should().BeFalse();

        (await TrailAsync(userId,
            AuditActionNames.UserEdited,
            AuditActionNames.PasswordChanged,
            AuditActionNames.NotificationPreferencesUpdated,
            AuditActionNames.AppearancePreferencesUpdated)).Should().BeEmpty();
    }

    [Fact]
    public async Task AppearanceIsAppliedToTheShellOnOrdinaryModulePages()
    {
        var email = Email("appearance.shell");
        var client = await SignInAsync(email, Investigator);

        await PostAsync(client, "/settings/appearance", ("Density", "Compact"));

        // The stored choice is read by the layout, so it also applies to a module page.
        BodyClass(await client.GetStringAsync("/Cases")).Should().Be("netra-density--compact");
    }
}
