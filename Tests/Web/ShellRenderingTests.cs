using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NETRA.Tests.Web;

public class ShellRenderingTests : IClassFixture<NetraWebFactory>
{
    private readonly NetraWebFactory _factory;

    public ShellRenderingTests(NetraWebFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, HttpResponseMessage Response)> SignInAsync(
        string email,
        string password,
        string role)
    {
        await _factory.SeedUserAsync(email, password, role);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/Account/Login"));

        var response = await client.PostAsync("/Account/Login", AuthenticationScreenTests.Form(
            ("UserName", email),
            ("Password", password),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return (client, response);
    }

    [Fact]
    public async Task Dashboard_RendersShellWithSidebarAndTopbar()
    {
        var (client, _) = await SignInAsync(
            "shell.admin@netra.local", "ValidPassw0rd!", "System Administrator");

        var html = await client.GetStringAsync("/Dashboard");

        html.Should().Contain("netra-sidebar");
        html.Should().Contain("netra-topbar");
        html.Should().Contain("netra-brand__name");
        html.Should().Contain("netra-content");
    }

    [Theory]
    [InlineData("Intelligence", "Dashboard")]
    [InlineData("Intelligence", "Crime Map")]
    [InlineData("Operations", "Crime Cases")]
    [InlineData("Operations", "Investigations")]
    [InlineData("Operations", "Persons & Suspects")]
    [InlineData("Operations", "Evidence")]
    [InlineData("Analysis", "Network & Link Analysis")]
    [InlineData("Analysis", "Image Investigation")]
    [InlineData("Intelligence", "AI Assistant")]
    [InlineData("Intelligence", "Alerts")]
    [InlineData("Intelligence", "Reports")]
    [InlineData("Data", "Bulk Data Import")]
    [InlineData("Administration", "User Management")]
    [InlineData("Administration", "Audit Logs")]
    [InlineData("Settings", "Account")]
    [InlineData("Settings", "Security")]
    [InlineData("Settings", "Notifications")]
    [InlineData("Settings", "Appearance")]
    [InlineData("Settings", "System Preferences")]
    [InlineData("Support", "Support")]
    public async Task Sidebar_ContainsAllRequiredNavigationEntries(
        string section,
        string item)
    {
        var (client, _) = await SignInAsync(
            "nav.admin@netra.local", "ValidPassw0rd!", "System Administrator");

        var html = await client.GetStringAsync("/Dashboard");

        // Labels such as "Persons & Suspects" are HTML-encoded in the output.
        var decoded = System.Net.WebUtility.HtmlDecode(html);

        decoded.Should().Contain(section);
        decoded.Should().Contain(item);
    }

    [Fact]
    public async Task Sidebar_MarksTheCurrentPageAsActive()
    {
        var (client, _) = await SignInAsync(
            "active.admin@netra.local", "ValidPassw0rd!", "System Administrator");

        var html = await client.GetStringAsync("/Dashboard");

        html.Should().Contain("is-active");
        html.Should().Contain("aria-current=\"page\"");
    }

    [Fact]
    public async Task Sidebar_HidesAdministrationEntriesFromNonAdministrators()
    {
        var (client, _) = await SignInAsync(
            "limited.investigator@netra.local", "ValidPassw0rd!", "Investigator");

        var html = await client.GetStringAsync("/Dashboard");

        html.Should().Contain("Crime Cases");
        html.Should().NotContain("User Management");
        html.Should().NotContain("System Preferences");
    }

    [Fact]
    public async Task Topbar_ShowsSignedInUserNameAndRole()
    {
        var (client, _) = await SignInAsync(
            "topbar.analyst@netra.local", "ValidPassw0rd!", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/Dashboard");

        html.Should().Contain("netra-userchip");
        html.Should().Contain("netra-userchip__name");
        html.Should().Contain("Police / Intelligence Analyst");
    }

    [Fact]
    public async Task Topbar_ProvidesServerRenderedSearchWithoutJavaScript()
    {
        var (client, _) = await SignInAsync(
            "search.analyst@netra.local", "ValidPassw0rd!", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/Dashboard");

        html.Should().Contain("name=\"q\"");
        html.Should().Contain("method=\"get\"");
        html.Should().NotContain("<script");
    }

    [Fact]
    public async Task Shell_ContainsNoJavaScriptAnywhere()
    {
        var (client, _) = await SignInAsync(
            "nojs.analyst@netra.local", "ValidPassw0rd!", "Police / Intelligence Analyst");

        foreach (var path in new[] { "/Dashboard", "/cases", "/alerts", "/settings" })
        {
            var html = await client.GetStringAsync(path);

            html.Should().NotContain("<script", $"{path} must not contain scripts");
            html.Should().NotContain("javascript:");
        }
    }

    [Fact]
    public async Task UnimplementedModule_RendersHonestPlaceholderWithoutFakeData()
    {
        var (client, _) = await SignInAsync(
            "placeholder.analyst@netra.local", "ValidPassw0rd!", "Police / Intelligence Analyst");

        // /persons remains a later-phase module, so it must render an honest placeholder.
        // (Bulk Data Import became a real module in Phase 12 and is covered by
        // BulkImportIntegrationTests.)
        var response = await client.GetAsync("/persons");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("not yet implemented");
        html.Should().Contain("Persons &amp; Suspects");
    }

    [Fact]
    public async Task BulkDataImport_IsNoLongerAPlaceholder()
    {
        var (client, _) = await SignInAsync(
            "import.analyst@netra.local", "ValidPassw0rd!", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/BulkImport");

        html.Should().NotContain("not yet implemented");
        html.Should().Contain("Bulk Data Import");
    }

    [Fact]
    public async Task CrimeCases_IsNowARealModuleNotAPlaceholder()
    {
        var (client, _) = await SignInAsync(
            "cases.analyst@netra.local", "ValidPassw0rd!", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/Cases");

        html.Should().Contain("Crime Cases");
        html.Should().NotContain("not yet implemented");
    }

    [Fact]
    public async Task AdministrationRoute_IsForbiddenForNonAdministrators()
    {
        var (client, _) = await SignInAsync(
            "forbidden.investigator@netra.local", "ValidPassw0rd!", "Investigator");

        // /Users is the real User Management module since Phase 13; the policy still
        // refuses every non-administrative role with a 403 rather than a redirect.
        var response = await client.GetAsync("/Users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdministrationRoute_IsAccessibleToAdministrators()
    {
        var (client, _) = await SignInAsync(
            "allowed.admin@netra.local", "ValidPassw0rd!", "System Administrator");

        var response = await client.GetAsync("/Users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SystemPreferencesRoute_IsForbiddenForNonAdministrators()
    {
        var (client, _) = await SignInAsync(
            "prefs.officer@netra.local", "ValidPassw0rd!", "Senior Officer");

        var response = await client.GetAsync("/settings/system-preferences");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuditLogsRoute_IsAllowedForSeniorOfficer()
    {
        var (client, _) = await SignInAsync(
            "audit.officer@netra.local", "ValidPassw0rd!", "Senior Officer");

        // /AuditLogs became the real audit module in Phase 13, replacing the placeholder
        // route; the read policy still admits Senior Officers and nothing else.
        var response = await client.GetAsync("/AuditLogs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnknownRoute_RendersStyledNotFoundPage()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/no-such-page");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        html.Should().Contain("Page not found");
        html.Should().Contain("netra-errorpage__code");
    }

    [Fact]
    public async Task Layout_LoadsTheNetraStylesheet()
    {
        var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/Account/Login");

        html.Should().Contain("/css/netra.css");
    }

    [Fact]
    public async Task Stylesheet_IsServedWithNetraDesignTokens()
    {
        var client = _factory.CreateClient();

        var css = await client.GetStringAsync("/css/netra.css");

        css.Should().Contain("--netra-background");
        css.Should().Contain("--netra-surface");
        css.Should().Contain("--netra-surface-elevated");
        css.Should().Contain("--netra-border");
        css.Should().Contain("--netra-primary");
        css.Should().Contain("--netra-text");
        css.Should().Contain("--netra-text-muted");
        css.Should().Contain("--netra-success");
        css.Should().Contain("--netra-warning");
        css.Should().Contain("--netra-danger");
        css.Should().Contain("--netra-info");
    }
}