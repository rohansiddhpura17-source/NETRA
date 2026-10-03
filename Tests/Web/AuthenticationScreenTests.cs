using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NETRA.Tests.Web;

public class AuthenticationScreenTests : IClassFixture<NetraWebFactory>
{
    private readonly NetraWebFactory _factory;

    public AuthenticationScreenTests(NetraWebFactory factory)
    {
        _factory = factory;
    }

    internal static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"");

        match.Success.Should().BeTrue("the form must contain an anti-forgery token");
        return match.Groups["token"].Value;
    }

    internal static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(field =>
            new KeyValuePair<string, string>(field.Key, field.Value)));

    private HttpClient CreateClient(bool allowRedirect = true) =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowRedirect
        });

    [Fact]
    public async Task Login_Get_RendersNetraBrandingAndForm()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/Account/Login");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("NETRA");
        html.Should().Contain("Neural Threat Intelligence");
        html.Should().Contain("Authorized access only");
        html.Should().Contain("netra-auth__card");
    }

    [Fact]
    public async Task Login_Get_UsesLabelledAccessibleInputs()
    {
        var client = CreateClient();

        var html = await client.GetStringAsync("/Account/Login");

        html.Should().Contain("name=\"UserName\"");
        html.Should().Contain("name=\"Password\"");
        html.Should().Contain("type=\"password\"");
        html.Should().Contain("name=\"RememberMe\"");
        html.Should().Contain("for=\"UserName\"");
        html.Should().Contain("for=\"Password\"");
        html.Should().Contain("autocomplete=\"username\"");
        html.Should().Contain("autocomplete=\"current-password\"");
    }

    [Fact]
    public async Task Login_Get_LinksToForgotPassword()
    {
        var client = CreateClient();

        var html = await client.GetStringAsync("/Account/Login");

        html.Should().Contain("/Account/ForgotPassword");
        html.Should().Contain("Forgot password?");
    }

    [Fact]
    public async Task Login_Post_WithInvalidCredentials_RerendersWithGenericError()
    {
        var client = CreateClient();

        var token = ExtractAntiForgeryToken(await client.GetStringAsync("/Account/Login"));

        var response = await client.PostAsync("/Account/Login", Form(
            ("UserName", "nobody@netra.local"),
            ("Password", "WrongPassword1!"),
            ("__RequestVerificationToken", token)));

        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Invalid username or password");
    }

    [Fact]
    public async Task Login_Post_WithoutAntiForgeryToken_IsRejected()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/Account/Login", Form(
            ("UserName", "someone@netra.local"),
            ("Password", "SomePassword1!")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_Post_WithValidCredentials_SignsInAndReachesDashboard()
    {
        const string email = "auth.flow@netra.local";
        const string password = "ValidPassw0rd!";
        await _factory.SeedUserAsync(email, password, "Investigator");

        var client = CreateClient(allowRedirect: false);

        var token = ExtractAntiForgeryToken(await client.GetStringAsync("/Account/Login"));

        var response = await client.PostAsync("/Account/Login", Form(
            ("UserName", email),
            ("Password", password),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/Dashboard");

        var dashboard = await client.GetAsync("/Dashboard");
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_IsPostOnly_GetsRejectedAndNeverSignsTheUserOut()
    {
        const string email = "logout.postonly@netra.local";
        const string password = "ValidPassw0rd!";
        await _factory.SeedUserAsync(email, password, "Investigator");

        var client = CreateClient(allowRedirect: false);

        var token = ExtractAntiForgeryToken(await client.GetStringAsync("/Account/Login"));
        var login = await client.PostAsync("/Account/Login", Form(
            ("UserName", email),
            ("Password", password),
            ("__RequestVerificationToken", token)));

        login.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // A GET (or any other verb) must not perform the sign-out.
        var getAttempt = await client.GetAsync("/Account/Logout");
        getAttempt.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.MethodNotAllowed);

        // The session must survive the rejected attempt.
        var dashboard = await client.GetAsync("/Dashboard");
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ForgotPassword_Get_RendersFormAndBranding()
    {
        var client = CreateClient();

        var html = await client.GetStringAsync("/Account/ForgotPassword");

        html.Should().Contain("Forgot password?");
        html.Should().Contain("NETRA");
        html.Should().Contain("name=\"Email\"");
        html.Should().Contain("type=\"email\"");
        html.Should().Contain("Send reset link");
        html.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task ForgotPassword_Post_RespondsIdenticallyForKnownAndUnknownAddresses()
    {
        const string known = "known.reset@netra.local";
        const string password = "KnownUserPass1!";
        await _factory.SeedUserAsync(known, password, "Investigator");

        var knownClient = CreateClient(allowRedirect: false);
        var knownToken = ExtractAntiForgeryToken(
            await knownClient.GetStringAsync("/Account/ForgotPassword"));

        var knownResponse = await knownClient.PostAsync("/Account/ForgotPassword", Form(
            ("Email", known),
            ("__RequestVerificationToken", knownToken)));

        var unknownClient = CreateClient(allowRedirect: false);
        var unknownToken = ExtractAntiForgeryToken(
            await unknownClient.GetStringAsync("/Account/ForgotPassword"));

        var unknownResponse = await unknownClient.PostAsync("/Account/ForgotPassword", Form(
            ("Email", "definitely-not-registered@netra.local"),
            ("__RequestVerificationToken", unknownToken)));

        knownResponse.StatusCode.Should().Be(unknownResponse.StatusCode);
        knownResponse.Headers.Location!.OriginalString
            .Should().Be(unknownResponse.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ForgotPassword_Post_WithoutToken_IsRejected()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/Account/ForgotPassword", Form(
            ("Email", "someone@netra.local")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnonymousRequestToProtectedPage_RedirectsToLogin()
    {
        var client = CreateClient(allowRedirect: false);

        var response = await client.GetAsync("/Dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task AuthenticationScreens_ContainNoJavaScript()
    {
        var client = CreateClient();

        foreach (var path in new[] { "/Account/Login", "/Account/ForgotPassword" })
        {
            var html = await client.GetStringAsync(path);

            html.Should().NotContain("<script");
            html.Should().NotContain("javascript:");
            html.Should().NotContain("onclick");
        }
    }
}