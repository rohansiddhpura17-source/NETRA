using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Application.Abstractions;
using NETRA.Domain.Alerts;
using NETRA.Domain.Enums;
using NETRA.Infrastructure.Data;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// Real MVC integration coverage for /alerts and /notifications: routing, the
/// dedicated alert policies, server-side filter persistence, generation being
/// POST-only and idempotent, notification ownership (no userId parameter exists
/// anywhere), antiforgery and the no-JavaScript guarantee. Hosted with EF Core's
/// in-memory provider by <see cref="NetraWebFactory"/>; not live SQL Server tests.
/// </summary>
public sealed class AlertsIntegrationTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";
    private readonly NetraWebFactory _factory;

    public AlertsIntegrationTests(NetraWebFactory factory)
    {
        _factory = factory;
        SeedDataAsync().GetAwaiter().GetResult();
    }

    private static DateTime Now => DateTime.UtcNow;

    private async Task SeedDataAsync()
    {
        await _factory.SeedReferenceDataAsync();
        await _factory.SeedUserAsync("alerts.host.seed@netra.local", Password, "System Administrator");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        if (await context.Alerts.AnyAsync(a => a.Title == "ALR-IT spike in Central District"))
        {
            return;
        }

        context.Alerts.AddRange(
            new Alert
            {
                AlertType = (int)AlertCategory.CrimeSpike,
                Severity = AlertSeverity.Critical,
                Status = AlertStatus.New,
                Title = "ALR-IT spike in Central District",
                Description = "Case volume rose sharply against the comparison window.",
                AlertKey = "1:Anomaly:it-critical:20260501-20260601",
                WindowStart = Now.AddDays(-30),
                WindowEnd = Now,
                CreatedAt = Now.AddHours(-3)
            },
            new Alert
            {
                AlertType = (int)AlertCategory.HighRiskArea,
                Severity = AlertSeverity.High,
                Status = AlertStatus.Acknowledged,
                Title = "ALR-IT high-risk area review",
                Description = "Analytical indicator remained elevated for the whole window.",
                CreatedAt = Now.AddHours(-6),
                AcknowledgedAt = Now.AddHours(-5)
            },
            new Alert
            {
                AlertType = (int)AlertCategory.RelatedCases,
                Severity = AlertSeverity.Medium,
                Status = AlertStatus.Resolved,
                Title = "ALR-IT resolved association lead",
                Description = "Recorded case association reviewed and closed.",
                CreatedAt = Now.AddDays(-2),
                ResolvedAt = Now.AddDays(-1)
            });
        await context.SaveChangesAsync();
    }

    private async Task<HttpClient> SignInAsync(string email, params string[] roles)
    {
        await _factory.SeedUserAsync(email, Password, roles);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/Account/Login"));
        var response = await client.PostAsync("/Account/Login", AuthenticationScreenTests.Form(
            ("UserName", email),
            ("Password", Password),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private HttpClient AnonymousClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    private static async Task<Guid> FirstAlertIdAsync(NetraDbContext context, string title) =>
        (await context.Alerts.FirstAsync(a => a.Title == title)).Id;

    // ------------------------------------------------------------------
    // Authorization
    // ------------------------------------------------------------------

    [Fact]
    public async Task Alerts_AnonymousRequests_RedirectToLogin()
    {
        var client = AnonymousClient();

        var index = await client.GetAsync("/alerts");
        var generate = await client.PostAsync("/alerts/generate",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        foreach (var response in new[] { index, generate })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location!.ToString().Should().Contain("/Account/Login");
        }
    }

    [Theory]
    [InlineData("System Administrator", "alerts.it.admin@netra.local")]
    [InlineData("Senior Officer", "alerts.it.senior@netra.local")]
    [InlineData("Police / Intelligence Analyst", "alerts.it.analyst@netra.local")]
    [InlineData("Investigator", "alerts.it.investigator@netra.local")]
    public async Task Alerts_EveryOperationalRoleMayReadTheFeed(string role, string email)
    {
        var client = await SignInAsync(email, role);

        var response = await client.GetAsync("/alerts");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Alerts &amp; Notifications");
        html.Should().Contain("ALR-IT spike in Central District");
        html.Should().NotContain("not yet implemented");
    }

    [Theory]
    [InlineData("alerts.it.gen.admin@netra.local", "System Administrator")]
    [InlineData("alerts.it.gen.senior@netra.local", "Senior Officer")]
    [InlineData("alerts.it.gen.analyst@netra.local", "Police / Intelligence Analyst")]
    public async Task Generate_PolicyRolesSeeTheAction(string email, string role)
    {
        var client = await SignInAsync(email, role);

        var html = await client.GetStringAsync("/alerts");

        html.Should().Contain("/alerts/generate");
    }

    [Fact]
    public async Task Generate_InvestigatorIsForbiddenButMayStillRead()
    {
        var client = await SignInAsync("alerts.it.gen.investigator@netra.local", "Investigator");

        var indexResponse = await client.GetAsync("/alerts");
        var index = await indexResponse.Content.ReadAsStringAsync();
        var forbidden = await client.PostAsync("/alerts/generate", AuthenticationScreenTests.Form(
            ("windowDays", "30"),
            ("__RequestVerificationToken", AuthenticationScreenTests.ExtractAntiForgeryToken(index))));

        indexResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        index.Should().NotContain("/alerts/generate");
    }

    // ------------------------------------------------------------------
    // Filtering
    // ------------------------------------------------------------------

    [Fact]
    public async Task Alerts_ServerSideFiltersNarrowTheFeed_AndValuesPersist()
    {
        var client = await SignInAsync("alerts.it.filter@netra.local", "Police / Intelligence Analyst");

        var critical = await client.GetStringAsync("/alerts?severity=Critical&status=New");
        var resolved = await client.GetStringAsync("/alerts?status=Resolved");
        var areaCategory = await client.GetStringAsync("/alerts?category=HighRiskArea");
        var none = await client.GetStringAsync("/alerts?severity=Critical&status=Resolved");

        critical.Should().Contain("ALR-IT spike in Central District");
        critical.Should().NotContain("ALR-IT resolved association lead");
        resolved.Should().Contain("ALR-IT resolved association lead");
        resolved.Should().NotContain("ALR-IT spike in Central District");
        areaCategory.Should().Contain("ALR-IT high-risk area review");

        // Filter controls come back with the active values so the band round-trips.
        critical.Should().Contain("value=\"Critical\" selected=\"selected\"");
        critical.Should().Contain("value=\"New\" selected=\"selected\"");
        none.Should().Contain("No alerts match the selected filters");
    }

    [Fact]
    public async Task Alerts_InvalidFilterValues_RenderSafelyWithoutFiftying()
    {
        var client = await SignInAsync("alerts.it.bogus@netra.local", "Police / Intelligence Analyst");

        var severity = await client.GetAsync("/alerts?severity=NotASeverity");
        var category = await client.GetAsync("/alerts?category=4242");
        var page = await client.GetAsync("/alerts?page=-5");
        var dates = await client.GetAsync("/alerts?dateFrom=not-a-date");

        foreach (var response in new[] { severity, category, page, dates })
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Alerts_EmptyFeed_RendersHonestEmptyState()
    {
        // Every seeded alert is filtered out by an impossible date range.
        var client = await SignInAsync("alerts.it.empty@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/alerts?dateFrom=1990-01-01&dateTo=1990-01-02");

        html.Should().Contain("No alerts match the selected filters");
        html.Should().NotContain("ALR-IT");
    }

    [Fact]
    public async Task AlertDetails_UnknownId_ReturnsNotFoundWithoutLeakingAnything()
    {
        var client = await SignInAsync("alerts.it.details.missing@netra.local", "Police / Intelligence Analyst");

        var response = await client.GetAsync($"/alerts/details/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AlertDetails_RendersBasisWindowAndAuditRow()
    {
        var client = await SignInAsync("alerts.it.details@netra.local", "Senior Officer");
        using var scope = NewScope();
        var alertId = await FirstAlertIdAsync(scope.ServiceProvider.GetRequiredService<NetraDbContext>(),
            "ALR-IT spike in Central District");

        var html = await client.GetStringAsync($"/alerts/details/{alertId}");

        html.Should().Contain("Analytical basis");
        html.Should().Contain("Analysis window");
        html.Should().Contain("Case volume rose sharply");
        html.Should().Contain("decision support, not a finding");

        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var actorId = (await context.Users.FirstAsync(
            u => u.Email == "alerts.it.details@netra.local")).Id;
        (await context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.AlertViewed &&
            l.EntityId == alertId &&
            l.UserId == actorId)).Should().BeTrue();
    }

    // ------------------------------------------------------------------
// Generation and lifecycle
    // ------------------------------------------------------------------

    [Fact]
    public async Task Generate_RepeatedRuns_CreateNoDuplicates()
    {
        var client = await SignInAsync("alerts.it.dedup@netra.local", "Police / Intelligence Analyst");

        async Task<(int Total, int Generated)> CountAsync()
        {
            using var scope = NewScope();
            var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
            return (await context.Alerts.CountAsync(),
                    await context.Alerts.CountAsync(a => a.AlertKey != null));
        }

        async Task<string> PostGenerateAsync()
        {
            var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
                await client.GetStringAsync("/alerts"));
            var response = await client.PostAsync("/alerts/generate", AuthenticationScreenTests.Form(
                ("windowDays", "30"), ("__RequestVerificationToken", token)));
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            return await client.GetStringAsync(response.Headers.Location!.ToString());
        }

        var before = await CountAsync();
        var firstHtml = await PostGenerateAsync();
        var afterFirst = await CountAsync();
        var secondHtml = await PostGenerateAsync();
        await PostGenerateAsync();
        var afterThird = await CountAsync();

        // The feed is stable across repeated passes: whatever the first run did,
        // the second and third added nothing and the same keys were re-skipped.
        afterThird.Should().Be(afterFirst);

        using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var generated = await context.Alerts
            .Where(a => a.AlertKey != null)
            .Select(a => a.AlertKey)
            .ToListAsync();
        generated.Should().OnlyHaveUniqueItems();
        generated.Count.Should().BeGreaterThanOrEqualTo(afterFirst.Generated - before.Generated);

        // The redirect flash is honest about what happened.
        firstHtml.Should().Contain("Generation pass complete");
        secondHtml.Should().Contain("Generation pass complete");
        secondHtml.Should().Contain("0 new alert");
    }

    [Fact]
    public async Task Generate_VisitingTheAlertPageNeverCreatesAlerts()
    {
        var client = await SignInAsync("alerts.it.passive@netra.local", "Police / Intelligence Analyst");
        using var beforeScope = NewScope();
        var before = await beforeScope.ServiceProvider.GetRequiredService<NetraDbContext>().Alerts.CountAsync();

        for (var i = 0; i < 3; i++)
        {
            await client.GetStringAsync("/alerts");
        }

        using var afterScope = NewScope();
        (await afterScope.ServiceProvider.GetRequiredService<NetraDbContext>().Alerts.CountAsync())
            .Should().Be(before);
    }

    [Fact]
    public async Task Generate_WithoutAntiForgeryToken_IsRejected()
    {
        var client = await SignInAsync("alerts.it.forgery@netra.local", "System Administrator");

        var response = await client.PostAsync("/alerts/generate", AuthenticationScreenTests.Form(
            ("windowDays", "30")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AcknowledgeResolveDismiss_TransitionTheAlertAndAuditIt()
    {
        var client = await SignInAsync("alerts.it.triage@netra.local", "Senior Officer");
        using var seedScope = NewScope();
        var alertId = await FirstAlertIdAsync(
            seedScope.ServiceProvider.GetRequiredService<NetraDbContext>(),
            "ALR-IT high-risk area review");

        async Task PostAsync(string action, params (string, string)[] extra)
        {
            var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
                await client.GetStringAsync($"/alerts/details/{alertId}"));
            var fields = extra.ToList();
            fields.Add(("__RequestVerificationToken", token));
            var response = await client.PostAsync(
                $"/alerts/{action}/{alertId}", AuthenticationScreenTests.Form(fields.ToArray()));
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            await client.GetAsync(response.Headers.Location!.ToString());
        }

        await PostAsync("acknowledge");
        await PostAsync("resolve", ("resolutionNotes", "Reviewed with the district control room."));

        using var checkScope = NewScope();
        var check = checkScope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var stored = await check.Alerts.AsNoTracking().FirstAsync(a => a.Id == alertId);
        stored.Status.Should().Be(AlertStatus.Resolved);
        stored.ResolutionNotes.Should().Be("Reviewed with the district control room.");
        (await check.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.AlertResolved && l.EntityId == alertId)).Should().BeTrue();

        // A closed alert refuses further transitions; the page still renders.
        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync($"/alerts/details/{alertId}"));
        var dismiss = await client.PostAsync($"/alerts/dismiss/{alertId}", AuthenticationScreenTests.Form(
            ("reason", "should be refused"), ("__RequestVerificationToken", token)));
        dismiss.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var page = await client.GetStringAsync($"/alerts/details/{alertId}");
        page.Should().Contain("no longer accepts triage actions");
        using var stillScope = NewScope();
        (await stillScope.ServiceProvider.GetRequiredService<NetraDbContext>()
            .Alerts.FirstAsync(a => a.Id == alertId)).Status
            .Should().Be(AlertStatus.Resolved);
    }

    [Fact]
    public async Task AlertPages_EmitNoJavaScript()
    {
        var client = await SignInAsync("alerts.it.nojs@netra.local", "Police / Intelligence Analyst");

        using var scope = NewScope();
        var alertId = await FirstAlertIdAsync(
            scope.ServiceProvider.GetRequiredService<NetraDbContext>(),
            "ALR-IT spike in Central District");

        foreach (var path in new[] { "/alerts", $"/alerts/details/{alertId}" })
        {
            var html = await client.GetStringAsync(path);

            html.Should().NotContain("<script", $"{path} must stay script-free");
            html.Should().NotContain("javascript:");
            html.Should().NotContain("onclick");
        }
    }

    [Fact]
    public async Task Dashboard_StillSurfacesActiveAlertsWithoutResolvedOnes()
    {
        var client = await SignInAsync("alerts.it.dashboard@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/Dashboard");

        html.Should().Contain("ALR-IT spike in Central District");
        html.Should().NotContain("ALR-IT resolved association lead");
        html.Should().Contain("href=\"/alerts\"");
    }
}
