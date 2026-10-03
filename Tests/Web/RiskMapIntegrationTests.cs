using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// End-to-end tests for the Phase 6 screens (/Risk and /Map) against the real MVC
/// application with the in-memory provider: authorization matrix, server-rendered
/// filters and results, honest empty states, the POST-only assessment run and the
/// no-JavaScript guarantee.
/// </summary>
public class RiskMapIntegrationTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";

    // Explicit window used by every request so the real system clock is irrelevant.
    private const string WindowQuery = "DateFrom=2026-05-17&DateTo=2026-06-15";

    private readonly NetraWebFactory _factory;
    private readonly Guid _centralId;
    private readonly Guid _firstCaseId;

    public RiskMapIntegrationTests(NetraWebFactory factory)
    {
        _factory = factory;
        (_centralId, _firstCaseId) = SeedPhaseSixDataAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Six scored Central cases (four in the window clustered at one scene area, two
    /// Low-severity cases in the comparison period), two window cases without usable
    /// coordinates, and one North case too small to score.
    /// </summary>
    private async Task<(Guid CentralId, Guid FirstCaseId)> SeedPhaseSixDataAsync()
    {
        await _factory.SeedReferenceDataAsync();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();

        var central = await context.Districts.FirstAsync(d => d.Code == "CD");
        var north = await context.Districts.FirstAsync(d => d.Code == "ND");
        var theft = await context.CrimeTypes.FirstAsync(t => t.Code == "THF");

        if (!await context.CrimeCases.AnyAsync(c => c.CaseNumber == "CC-P6-1"))
        {
            var actor = new ApplicationUser
            {
                UserName = "phase6.seed@netra.local",
                Email = "phase6.seed@netra.local",
                FirstName = "Phase",
                LastName = "Six",
                EmailConfirmed = true,
                IsActive = true
            };
            context.Users.Add(actor);

            void Add(
                string number,
                CaseSeverity severity,
                DateTime date,
                District district,
                double? lat,
                double? lon)
            {
                var crimeCase = new CrimeCase
                {
                    CaseNumber = number,
                    Title = $"Phase 6 case {number}",
                    CrimeTypeId = theft.Id,
                    Status = CaseStatus.Open,
                    Severity = severity,
                    IncidentDate = date,
                    CreatedById = actor.Id,
                    CreatedAt = date
                };
                context.CrimeCases.Add(crimeCase);

                var location = new Location
                {
                    Name = $"Phase 6 scene {number}",
                    DistrictId = district.Id,
                    Latitude = lat,
                    Longitude = lon,
                    CreatedAt = date
                };
                context.Locations.Add(location);
                context.CaseLocations.Add(new CaseLocation
                {
                    CrimeCaseId = crimeCase.Id,
                    LocationId = location.Id,
                    LocationRole = "Incident",
                    AssociatedAt = date
                });
            }

            var d = (int day) => new DateTime(2026, 6, day, 9, 0, 0, DateTimeKind.Utc);

            // Central window (6 cases) versus 2 in the comparison period:
            // volume 24 + severity 12 (2 High, 1 Critical) + change 15 (+200% capped)
            // + concentration 10 => score 61 (High). CC-P6-7/8 have unusable coordinates
            // for the map but still count for risk.
            Add("CC-P6-1", CaseSeverity.High, d(1), central, 51.5072, -0.1276);
            Add("CC-P6-2", CaseSeverity.High, d(2), central, 51.5073, -0.1275);
            Add("CC-P6-3", CaseSeverity.Critical, d(8), central, 51.50725, -0.12765);
            Add("CC-P6-4", CaseSeverity.Medium, d(10), central, 51.50728, -0.12758);
            Add("CC-P6-7", CaseSeverity.Medium, d(4), central, null, null);
            Add("CC-P6-8", CaseSeverity.Medium, d(6), central, 0, 0);

            // Central comparison period.
            Add("CC-P6-5", CaseSeverity.Low, new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc),
                central, 51.51, -0.13);
            Add("CC-P6-6", CaseSeverity.Low, new DateTime(2026, 5, 10, 9, 0, 0, DateTimeKind.Utc),
                central, 51.512, -0.132);

            // North: a single case is not enough to score honestly.
            Add("CC-P6-9", CaseSeverity.Medium, d(5), north, 51.5231, -0.1425);

            await context.SaveChangesAsync();
        }

        var first = await context.CrimeCases.FirstAsync(c => c.CaseNumber == "CC-P6-1");
        return (central.Id, first.Id);
    }

    private async Task<HttpClient> SignInAsync(string email, params string[] roles)
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
        return client;
    }

    private HttpClient AnonymousClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    /// <summary>
    /// Razor's default HTML encoder renders typographic glyphs (– · ↗ +) from
    /// bound expressions as numeric entities; tests assert the human-visible text.
    /// </summary>
    private static string DecodeHtml(string html) => System.Net.WebUtility.HtmlDecode(html);

    // ------------------------------------------------------------ authorization

    [Theory]
    [InlineData("/Risk")]
    [InlineData("/Map")]
    public async Task RiskAndMap_Anonymous_RedirectToLogin(string path)
    {
        var response = await AnonymousClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Risk_Investigator_IsForbiddenByTheRiskPolicy()
    {
        var client = await SignInAsync("p6.risk.inv@netra.local", "Investigator");

        var response = await client.GetAsync($"/Risk?{WindowQuery}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Risk_Analyst_IsAllowed()
    {
        var client = await SignInAsync("p6.risk.analyst@netra.local", "Police / Intelligence Analyst");

        var response = await client.GetAsync($"/Risk?{WindowQuery}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Risk_SeniorOfficer_IsAllowed()
    {
        var client = await SignInAsync("p6.risk.senior@netra.local", "Senior Officer");

        var response = await client.GetAsync($"/Risk?{WindowQuery}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Map_Investigator_IsAllowed()
    {
        var client = await SignInAsync("p6.map.inv@netra.local", "Investigator");

        var response = await client.GetAsync($"/Map?{WindowQuery}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------ risk screen

    [Fact]
    public async Task Risk_RendersAreaTableWithScoresTrendsAndExplanations()
    {
        var client = await SignInAsync("p6.risk.page@netra.local", "Police / Intelligence Analyst");

        var html = DecodeHtml(await client.GetStringAsync($"/Risk?{WindowQuery}"));

        html.Should().Contain("Risk & Anomaly Analysis");
        html.Should().Contain("Area Risk Analysis");
        html.Should().Contain("Window 17 May 2026 – 15 Jun 2026 · vs 17 Apr 2026 – 16 May 2026");
        html.Should().Contain("Central District");
        html.Should().Contain("Historical Trend");
        // Central: 6 window cases vs 2 -> 24 + 12 + 15 + 10 = 61 (High).
        html.Should().Contain("<strong>61</strong>");
        html.Should().Contain("↗ +200%");
        // Explanations are part of the table, not an afterthought.
        html.Should().Contain("Components: volume +24, severity +12, change +15, concentration +10");
        // The Run Assessment form must carry a real antiforgery token, not type-name garbage.
        html.Should().Contain("name=\"__RequestVerificationToken\"");
        html.Should().NotContain("AntiforgeryExtensions");
    }

    [Fact]
    public async Task Risk_StatesThatScoresAreNotPredictions()
    {
        var client = await SignInAsync("p6.risk.disclaimer@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync($"/Risk?{WindowQuery}");

        html.Should().Contain(
            "Risk scores are analytical indicators and do not represent guaranteed predictions.");
        html.Should().Contain("Analytical indicators, not predictions",
            "the terminology constraint is honoured");
    }

    [Fact]
    public async Task Risk_InsufficientSample_IsShownAsInsufficientNotScored()
    {
        var client = await SignInAsync("p6.risk.insufficient@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync($"/Risk?{WindowQuery}");

        html.Should().Contain("North District");
        html.Should().Contain("Insufficient data");
        html.Should().Contain("below the 3-case minimum");
    }

    [Fact]
    public async Task Risk_DetectsAnomaliesWithExplanations()
    {
        var client = await SignInAsync("p6.risk.anomalies@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync($"/Risk?{WindowQuery}");

        html.Should().Contain("Detected Anomalies");
        html.Should().Contain("Crime volume up 200% in Central District");
        html.Should().Contain(
            "Crime volume in Central District increased by 200% compared with the previous comparison period (6 cases versus 2).");
        html.Should().Contain("Severity shift in Central District");
        html.Should().Contain("Change vs comparison period");
    }

    [Fact]
    public async Task Risk_DistrictFilter_IsAppliedAndEchoedBack()
    {
        var client = await SignInAsync("p6.risk.filter@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync($"/Risk?{WindowQuery}&DistrictId={_centralId}");

        html.Should().Contain($"value=\"{_centralId}\" selected=\"selected\"");
        html.Should().Contain("Central District");
        // The filter dropdown always lists every district; the results table must not.
        html.Should().NotContain("North District</th>");
    }

    [Fact]
    public async Task Risk_SeverityFilter_EchoesSelectedOption()
    {
        var client = await SignInAsync("p6.risk.sev@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync($"/Risk?{WindowQuery}&Severity=High");

        html.Should().Contain("value=\"High\" selected=\"selected\"");
    }

    [Fact]
    public async Task Risk_NoDataForPeriod_HonestEmptyStateNotInventedIndicators()
    {
        var client = await SignInAsync("p6.risk.empty@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/Risk?DateFrom=2000-01-01&DateTo=2000-01-31");

        html.Should().Contain("No risk assessments are available for the selected period");
        html.Should().Contain("Nothing to compare");
    }

    [Fact]
    public async Task Risk_InvertedDates_WarnsAndStillRendersTheDefaultWindow()
    {
        var client = await SignInAsync("p6.risk.dates@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/Risk?DateFrom=2026-06-10&DateTo=2026-06-01");

        html.Should().Contain("The start date must be on or before the end date.");
        html.Should().Contain("Area Risk Analysis");
    }

    // ------------------------------------------------------------ assessment POST

    [Fact]
    public async Task Assess_WithoutAntiForgeryToken_IsRejected()
    {
        var client = await SignInAsync("p6.risk.post@netra.local", "Police / Intelligence Analyst");

        var response = await client.PostAsync(
            "/risk/assess",
            AuthenticationScreenTests.Form(("DateFrom", "2026-05-17"), ("DateTo", "2026-06-15")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Assess_WithToken_PersistsRunAndFlashesHonestSummary()
    {
        var client = await SignInAsync("p6.risk.assess@netra.local", "Police / Intelligence Analyst");

        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync($"/Risk?{WindowQuery}"));

        var post = await client.PostAsync("/risk/assess", AuthenticationScreenTests.Form(
            ("DateFrom", "2026-05-17"),
            ("DateTo", "2026-06-15"),
            ("__RequestVerificationToken", token)));

        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        // Attribute routing emits the lowercase route pattern in the redirect target.
        post.Headers.Location!.ToString().Should().Contain("/risk?DateFrom=2026-05-17&DateTo=2026-06-15");

        var html = await client.GetStringAsync($"/Risk?{WindowQuery}");
        html.Should().Contain("Risk assessment run: 1 area indicator recorded and 3 new anomalies detected.");

        // The run persisted explainable area assessments, not bare numbers.
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var assessment = await context.RiskAssessments.AsNoTracking()
            .SingleAsync(r => r.DistrictId != null);
        assessment.RiskScore.Should().Be(61m);
        assessment.Factors.Should().Contain("Components:");
        assessment.Assessment.Should().Contain("not a prediction of future crime");
        (await context.Anomalies.AsNoTracking()
            .CountAsync(a => a.RelatedEntityType == "District")).Should().Be(3);
    }

    [Fact]
    public async Task Risk_GetIsReadOnly_AndSidebarLinksArePresent()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var before = await context.RiskAssessments.CountAsync(r => r.DistrictId != null);

        var client = await SignInAsync("p6.risk.nav@netra.local", "System Administrator");
        await client.GetStringAsync($"/Risk?{WindowQuery}");

        // The sidebar exposes both Phase 6 modules to an administrator.
        var dashboard = await client.GetStringAsync("/Dashboard");
        dashboard.Should().Contain("/risk");
        dashboard.Should().Contain("/map");

        var after = await context.RiskAssessments.CountAsync(r => r.DistrictId != null);
        after.Should().Be(before, "GET requests must never persist assessments");
    }

    // ------------------------------------------------------------ map screen

    [Fact]
    public async Task Map_RendersServerSideSvgWithLegendAndHonestFooter()
    {
        var client = await SignInAsync("p6.map.page@netra.local", "Investigator");

        var html = await client.GetStringAsync($"/Map?{WindowQuery}");

        html.Should().Contain("Crime Intelligence Map");
        html.Should().Contain("<svg class=\"netra-map\"");
        html.Should().Contain("netra-map__point--critical");
        html.Should().Contain("Hotspot (3+ incidents)");
        html.Should().Contain(
            "Schematic projection of recorded coordinates — not a live interactive GIS system.");

        // Five plotted points (4 clustered Central + 1 North); the May comparison-period
        // cases fall outside the map window and the two unusable-coordinate cases are hidden.
        System.Text.RegularExpressions.Regex
            .Matches(html, "class=\"netra-map__point ")
            .Should().HaveCount(5);
    }

    [Fact]
    public async Task Map_InvalidCoordinates_ReportedNotPlotted()
    {
        var client = await SignInAsync("p6.map.hidden@netra.local", "Investigator");

        var html = await client.GetStringAsync($"/Map?{WindowQuery}");

        html.Should().Contain("2 records without usable coordinates");
        html.Should().Contain("Records Without Coordinates");
    }

    [Fact]
    public async Task Map_SeverityFilter_LimitsPlottedPointsAndEchoesSelection()
    {
        var client = await SignInAsync("p6.map.sev@netra.local", "Investigator");

        var html = await client.GetStringAsync($"/Map?{WindowQuery}&Severity=Critical");

        html.Should().Contain("value=\"Critical\" selected=\"selected\"");
        html.Should().Contain("netra-map__point--critical");
        html.Should().NotContain("netra-map__point--high");
        html.Should().NotContain("netra-map__point--medium");
    }

    [Fact]
    public async Task Map_CaseIdParameter_RendersServerSideQuickView()
    {
        var client = await SignInAsync("p6.map.select@netra.local", "Investigator");

        var html = await client.GetStringAsync($"/Map?{WindowQuery}&caseId={_firstCaseId}");

        html.Should().Contain("Selected Incident");
        html.Should().Contain("CC-P6-1");
        html.Should().Contain("Open case record");
        html.Should().Contain("Phase 6 scene CC-P6-1");
    }

    [Fact]
    public async Task Map_NoIncidentsForPeriod_HonestEmptyState()
    {
        var client = await SignInAsync("p6.map.empty@netra.local", "Investigator");

        var html = await client.GetStringAsync("/Map?DateFrom=2000-01-01&DateTo=2000-01-31");

        html.Should().Contain("No incidents to plot");
        html.Should().NotContain("<svg class=\"netra-map\"");
    }

    [Fact]
    public async Task Map_FiltersPreserveAcrossQuickViewLinks()
    {
        var client = await SignInAsync("p6.map.preserve@netra.local", "Investigator");

        var html = await client.GetStringAsync($"/Map?{WindowQuery}&Severity=High");

        // The View link for a row must carry the active filters forward.
        html.Should().Contain("Severity=High");
        html.Should().Contain("value=\"High\" selected=\"selected\"");
    }

    // ------------------------------------------------------------ no JavaScript

    [Theory]
    [InlineData("/Risk")]
    [InlineData("/Map")]
    public async Task RiskAndMap_ContainNoJavaScript(string path)
    {
        var client = await SignInAsync($"p6.nojs{(path == "/Risk" ? "risk" : "map")}@netra.local",
            "System Administrator");

        var html = await client.GetStringAsync($"{path}?{WindowQuery}");

        html.Should().NotContain("<script");
        html.Should().NotContain("javascript:");
        html.Should().NotContain("onclick");
        html.Should().NotContain("Chart.js");
        html.Should().NotContain("leaflet");
    }
}
