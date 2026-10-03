using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Domain.Alerts;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Intelligence;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// End-to-end rendering tests for the Phase 5 Intelligence Dashboard (/Dashboard)
/// and Crime Analytics (/Analytics) screens against the real MVC application with
/// the in-memory provider. Verifies authorisation, KPI cards, server-side SVG,
/// filters, empty states and the no-JavaScript guarantee.
/// </summary>
public class DashboardAnalyticsIntegrationTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";

    private readonly NetraWebFactory _factory;
    private readonly Guid _districtId;

    public DashboardAnalyticsIntegrationTests(NetraWebFactory factory)
    {
        _factory = factory;
        _districtId = SeedAnalyticsDataAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Adds a case (Central district, recent incident), an active alert and a
    /// high-risk assessment directly to the shared in-memory database so the
    /// dashboard has real rows to aggregate.
    /// </summary>
    private async Task<Guid> SeedAnalyticsDataAsync()
    {
        await _factory.SeedReferenceDataAsync();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();

        if (await context.CrimeCases.AnyAsync(c => c.Title == "Phase 5 integration case"))
        {
            return (await context.Districts.FirstAsync(d => d.Code == "CD")).Id;
        }

        var actor = new ApplicationUser
        {
            UserName = "phase5.seed@netra.local",
            Email = "phase5.seed@netra.local",
            FirstName = "Phase",
            LastName = "Seed",
            EmailConfirmed = true,
            IsActive = true
        };
        context.Users.Add(actor);

        var crimeType = await context.CrimeTypes.FirstAsync();
        var central = await context.Districts.FirstAsync(d => d.Code == "CD");
        var incident = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);

        var crimeCase = new CrimeCase
        {
            CaseNumber = $"CC-P5-{Guid.NewGuid():N}"[..20],
            Title = "Phase 5 integration case",
            CrimeTypeId = crimeType.Id,
            Status = CaseStatus.Open,
            Severity = CaseSeverity.High,
            IncidentDate = incident,
            CreatedById = actor.Id,
            CreatedAt = incident
        };
        context.CrimeCases.Add(crimeCase);

        var location = new Location
        {
            Name = "Phase 5 scene",
            DistrictId = central.Id,
            CreatedAt = incident
        };
        context.Locations.Add(location);
        context.CaseLocations.Add(new CaseLocation
        {
            CrimeCaseId = crimeCase.Id,
            LocationId = location.Id,
            LocationRole = "Incident",
            AssociatedAt = incident
        });

        context.Alerts.Add(new Alert
        {
            Title = "Phase 5 integration alert",
            Severity = AlertSeverity.Critical,
            Status = AlertStatus.New,
            AlertType = 1,
            CrimeCaseId = crimeCase.Id,
            CreatedAt = incident
        });

        context.RiskAssessments.Add(new RiskAssessment
        {
            CrimeCaseId = crimeCase.Id,
            Title = "Phase 5 risk assessment",
            RiskLevel = 4,
            RiskScore = 8.7m,
            AssessedAt = incident,
            AssessedById = actor.Id,
            CreatedAt = incident
        });

        await context.SaveChangesAsync();
        return central.Id;
    }

    private async Task<(HttpClient Client, HttpResponseMessage Login)> SignInAsync(
        string email,
        params string[] roles)
    {
        await _factory.SeedUserAsync(email, Password, roles);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/Account/Login"));

        var response = await client.PostAsync("/Account/Login", AuthenticationScreenTests.Form(
            ("UserName", email),
            ("Password", Password),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return (client, response);
    }

    // ------------------------------------------------------------ dashboard

    [Fact]
    public async Task Dashboard_Anonymous_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Dashboard_SignedInAnalyst_RendersKpiCardsAndCharts()
    {
        var (client, _) = await SignInAsync(
            "phase5.dash@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/Dashboard");

        html.Should().Contain("Intelligence Dashboard");
        html.Should().Contain("Total Cases");
        html.Should().Contain("Active Cases");
        html.Should().Contain("High-Risk Areas");
        html.Should().Contain("Active Alerts");
        html.Should().Contain("Crime Trend Analysis");
        html.Should().Contain("District Comparison");
        html.Should().Contain("<svg");
        html.Should().Contain("role=\"img\"");
        html.Should().Contain("Analytical Indicator");
        html.Should().Contain("Phase 5 integration alert");
    }

    [Fact]
    public async Task Dashboard_Investigator_IsAllowed()
    {
        var (client, _) = await SignInAsync("phase5.dash.inv@netra.local", "Investigator");

        var response = await client.GetAsync("/Dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------ analytics

    [Fact]
    public async Task Analytics_Anonymous_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Analytics");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Analytics_Investigator_IsForbidden()
    {
        var (client, _) = await SignInAsync("phase5.an.inv@netra.local", "Investigator");

        var response = await client.GetAsync("/Analytics");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Analytics_SignedInAnalyst_RendersFilterBandAndCharts()
    {
        var (client, _) = await SignInAsync("phase5.an@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/Analytics");

        html.Should().Contain("Crime Analytics");
        html.Should().Contain("name=\"DateFrom\"");
        html.Should().Contain("name=\"DateTo\"");
        html.Should().Contain("name=\"DistrictId\"");
        html.Should().Contain("name=\"CrimeTypeId\"");
        html.Should().Contain("name=\"Severity\"");
        html.Should().Contain("Apply Filters");
        html.Should().Contain("Clear filters");
        html.Should().Contain("Total Crimes");
        html.Should().Contain("Avg Cases Per Day");
        html.Should().Contain("Hotspot District");
        html.Should().Contain("Data Insights");
        html.Should().Contain("<svg");
    }

    [Fact]
    public async Task Analytics_PreservesSubmittedFilterValues()
    {
        var (client, _) = await SignInAsync("phase5.an2@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync($"/Analytics?Severity=High&DistrictId={_districtId}");

        html.Should().Contain("value=\"High\" selected=\"selected\"");
        html.Should().Contain($"value=\"{_districtId}\" selected=\"selected\"");
    }

    [Fact]
    public async Task Analytics_InvalidDateRange_ShowsFriendlyErrorAndStillRenders()
    {
        var (client, _) = await SignInAsync("phase5.an3@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/Analytics?DateFrom=2026-05-10&DateTo=2026-05-01");

        html.Should().Contain("The start date must be on or before the end date.");
        html.Should().Contain("name=\"DateFrom\"");
    }

    [Fact]
    public async Task Analytics_PeriodWithoutData_ShowsEmptyState()
    {
        var (client, _) = await SignInAsync("phase5.an4@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/Analytics?DateFrom=1900-01-01&DateTo=1900-12-31");

        html.Should().Contain("No crime records are available for the selected period.");
        html.Should().Contain("Nothing to summarise");
    }

    [Fact]
    public async Task Analytics_CtaLinksToTheRealReportsModule()
    {
        // Reports became a real module in Phase 11, so the Analytics footer call-to-action must
        // not resolve to Placeholder ("not yet implemented") for any role that can reach Analytics.
        var (client, _) = await SignInAsync("phase5.cta1@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/Analytics?DateFrom=1900-01-01&DateTo=1900-12-31");

        html.Should().Contain(">Reports module</a>");
        html.Should().Contain("href=\"/reports\">Reports module");

        // The sidebar links the same module, so only an absence of the placeholder route proves
        // that no call-to-action still points at the "not yet implemented" page.
        html.Should().NotContain("Placeholder");
        html.Should().NotContain("not yet implemented");
    }

    [Fact]
    public async Task Analytics_SidebarLinksAnalyticsForAnalystAndHidesItFromInvestigator()
    {
        var (analyst, _) = await SignInAsync("phase5.nav1@netra.local", "Police / Intelligence Analyst");
        var analystHtml = await analyst.GetStringAsync("/Dashboard");
        analystHtml.Should().Contain("Crime Analytics");
        analystHtml.Should().Contain("href=\"/Analytics\"");

        var (investigator, _) = await SignInAsync("phase5.nav2@netra.local", "Investigator");
        var investigatorHtml = await investigator.GetStringAsync("/Dashboard");
        investigatorHtml.Should().NotContain("href=\"/Analytics\"");
    }

    // ------------------------------------------------------------ no-JavaScript

    [Theory]
    [InlineData("/Dashboard")]
    [InlineData("/Analytics")]
    public async Task Phase5Pages_ContainNoJavaScript(string path)
    {
        var (client, _) = await SignInAsync($"phase5.nojs{path.Replace("/", ".")}@netra.local",
            "System Administrator");

        var html = await client.GetStringAsync(path);

        html.Should().NotContain("<script");
        html.Should().NotContain("onclick=");
        html.Should().NotContain("onload=");
        html.Should().NotContain("chart.js", ".NETRA uses server-side SVG only");
    }
}
