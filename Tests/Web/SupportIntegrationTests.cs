using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// End-to-end checks for the Support page. It documents the module set that actually exists in
/// this deployment, so the tests hold it to that: it must render for any signed-in officer, must
/// not pretend a request can be delivered, and must not leak anything about the server.
/// </summary>
public class SupportIntegrationTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";
    private const string Admin = "System Administrator";

    private readonly NetraWebFactory _factory;

    public SupportIntegrationTests(NetraWebFactory factory)
    {
        _factory = factory;
    }

    private static string Email(string label) => $"{label}.{Guid.NewGuid():N}@netra.local";

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

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

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    [Fact]
    public async Task Support_IsSignInProtectedForAnonymousRequests()
    {
        var client = CreateClient();

        var get = await client.GetAsync("/support");
        var post = await client.PostAsync("/support", AuthenticationScreenTests.Form(("Message", "hello")));

        get.StatusCode.Should().Be(HttpStatusCode.Redirect);
        get.Headers.Location!.AbsolutePath.Should().StartWith("/Account/Login");

        // The help page is a read-only route: there is no POST handler for it, so a
        // hand-made request cannot reach anything that could claim to accept one.
        post.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Theory]
    [InlineData("Investigator")]
    [InlineData("Police / Intelligence Analyst")]
    [InlineData("Senior Officer")]
    [InlineData(Admin)]
    public async Task Support_IsReadableByEverySignedInRole(string role)
    {
        var client = await SignInAsync(Email($"support.{role.Split(' ')[0]}"), role);

        var response = await client.GetAsync("/support");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Support_RendersRealProductGuidance()
    {
        var client = await SignInAsync(Email("support.content"), "Investigator");

        var html = await client.GetStringAsync("/support");

        html.Should().Contain("netra-help__section");
        html.Should().Contain("Module guidance");
        html.Should().Contain("Common workflows");
        html.Should().Contain("How access is decided");
        html.Should().Contain("What is recorded");
        html.Should().Contain("Common questions");
        html.Should().Contain("/Cases");
        html.Should().Contain("/settings/notifications");

        // The FAQ is a static HTML disclosure, not a script-driven accordion.
        html.Should().Contain("<details");
        html.Should().Contain("<summary");
    }

    [Fact]
    public async Task Support_TellsTheTruthAboutItsOwnLimits()
    {
        var client = await SignInAsync(Email("support.honest"), "Investigator");

        var html = await client.GetStringAsync("/support");

        // No invented contact channel, and no form that would look like one.
        html.Should().NotContain("mailto:");
        html.Should().NotContain("support@");
        html.Should().NotContain("<textarea");
        html.Should().NotContain("name=\"Message\"");
        html.Should().NotContain("name=\"Recipient\"");
        html.Should().NotContain("action=\"/Support");

        // It says so plainly instead of pretending a request would be delivered.
        html.Should().Contain("no external support channel");
    }

    [Fact]
    public async Task Support_ShowsTheSignedInOfficersOwnAccessState()
    {
        var email = Email("support.self");
        var client = await SignInAsync(email, Admin);

        var html = await client.GetStringAsync("/support");

        html.Should().Contain(Admin);
        html.Should().NotContain("<script");
        html.Should().NotContain("onclick=");
        html.Should().NotContain("javascript:");
    }

    [Fact]
    public async Task Support_ExposesNothingAboutTheServer()
    {
        var client = await SignInAsync(Email("support.secret"), Admin);

        var html = await client.GetStringAsync("/support");

        html.Should().NotContain("Connection");
        html.Should().NotContain("appsettings");
        html.Should().NotContain("PasswordHash");
        html.Should().NotContain("/var/");
        html.Should().NotContain("C:\\");
        html.Should().NotContain("Data Source");
    }

    [Theory]
    [InlineData("/Dashboard")]
    [InlineData("/Cases")]
    [InlineData("/map")]
    [InlineData("/analytics")]
    [InlineData("/risk")]
    [InlineData("/network")]
    [InlineData("/image-investigation")]
    [InlineData("/assistant")]
    [InlineData("/alerts")]
    [InlineData("/notifications")]
    [InlineData("/reports")]
    [InlineData("/BulkImport")]
    [InlineData("/Users")]
    [InlineData("/AuditLogs")]
    [InlineData("/settings")]
    [InlineData("/settings/account")]
    [InlineData("/settings/security")]
    [InlineData("/settings/notifications")]
    [InlineData("/settings/appearance")]
    [InlineData("/settings/system-preferences")]
    [InlineData("/support")]
    public async Task EveryRouteTheSupportPageDocuments_Exists(string path)
    {
        // Identity only accepts alphanumeric user names, so the route becomes one.
        var slug = new string(path.Where(char.IsLetterOrDigit).ToArray());
        var client = await SignInAsync(Email($"support.route.{slug}"), Admin);

        var response = await client.GetAsync(path);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound, $"{path} is linked from the help page");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PlaceholderRoutes_RemainHonestAboutModulesThatDoNotExist()
    {
        var client = await SignInAsync(Email("support.placeholder"), "Investigator");

        var html = await client.GetStringAsync("/support");

        // The help page must not promise the three modules that are still placeholders.
        html.Should().NotContain("<code>/investigations</code>");
        html.Should().NotContain("<code>/persons</code>");
        html.Should().NotContain("<code>/evidence</code>");

        var investigations = await client.GetAsync("/investigations");
        investigations.StatusCode.Should().Be(HttpStatusCode.OK);
        (await investigations.Content.ReadAsStringAsync())
            .Should().Contain("not yet implemented");
    }

    [Fact]
    public async Task SupportPage_IsLinkedFromTheShellForEveryUser()
    {
        var client = await SignInAsync(Email("support.shell"), "Investigator");

        var html = await client.GetStringAsync("/Dashboard");
        html.Should().Contain("href=\"/support\"");
    }
}
