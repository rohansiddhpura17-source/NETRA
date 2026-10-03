using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Application.Abstractions;
using NETRA.Application.Reports;
using NETRA.Domain.Alerts;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Investigation;
using NETRA.Domain.People;
using NETRA.Domain.Reports;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// Real MVC integration coverage for /reports (netra.pdf page 10): the three report
/// policies, the analytical content gate that hides risk/trend/intelligence documents
/// from readers who may not see that information, server-side filter and tab
/// persistence, generation being an explicit authorised POST, the server-rendered CSV
/// download, print styling and the no-JavaScript guarantee.
///
/// Hosted with EF Core's in-memory provider by <see cref="NetraWebFactory"/> — these
/// are not live SQL Server tests.
/// </summary>
public sealed class ReportsIntegrationTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";
    private const string HostEmail = "reports.it.host@netra.local";
    private const string CaseNumber = "ITRP-2026-0001";
    private const string LinkedCaseNumber = "ITRP-2026-0002";
    private const string SensitiveNationalId = "NID-IT-SECRET-9000";
    private const string SensitivePhone = "+1 555 0911";
    private const string SensitiveEmail = "dana.private@invalid.test";
    private const string SensitiveAddress = "12 Private Lane Ranelagh";
    private const string PrivateNote = "Restricted note: informant payment arrangement.";
    private const string PublicNote = "Public observation: the unit operator supplied the access log.";

    private readonly NetraWebFactory _factory;

    private Guid _caseId;
    private Guid _trendReportId;
    private Guid _caseReportId;
    private Guid _investigationReportId;
    private Guid _authoredReportId;

    public ReportsIntegrationTests(NetraWebFactory factory)
    {
        _factory = factory;
        SeedAsync().GetAwaiter().GetResult();
    }

    private static DateTime Now => DateTime.UtcNow;

    // ------------------------------------------------------------------
    // Fixture data: one real case with people, evidence, notes and timeline,
    // plus one generated trend report, one generated case report and one
    // analyst-authored draft. Reports are produced by ReportService itself,
    // so the stored parameters are exactly what the application writes.
    // ------------------------------------------------------------------

    private async Task SeedAsync()
    {
        await _factory.SeedReferenceDataAsync();
        await _factory.SeedUserAsync(HostEmail, Password, "System Administrator");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var hostId = (await context.Users.FirstAsync(u => u.Email == HostEmail)).Id;
        var crimeType = await context.CrimeTypes.FirstAsync(c => c.Code == "THF");
        var district = await context.Districts.FirstAsync(d => d.Code == "CD");

        var focusCase = await context.CrimeCases.FirstOrDefaultAsync(c => c.CaseNumber == CaseNumber);
        if (focusCase is null)
        {
            await _factory.SeedUserAsync("reports.it.lead@netra.local", Password, "Investigator");
            var leadId = (await context.Users.FirstAsync(u => u.Email == "reports.it.lead@netra.local")).Id;

            focusCase = new CrimeCase
            {
                CaseNumber = CaseNumber,
                Title = "Storage unit break-in, Central HQ",
                Description = "Synthetic integration fixture. No real incident.",
                CrimeTypeId = crimeType.Id,
                Status = CaseStatus.Open,
                Severity = CaseSeverity.High,
                IncidentDate = Now.AddDays(-5),
                ReportedDate = Now.AddDays(-4),
                CreatedById = hostId,
                CreatedAt = Now.AddDays(-5),
                UpdatedAt = Now.AddDays(-1)
            };
            var linkedCase = new CrimeCase
            {
                CaseNumber = LinkedCaseNumber,
                Title = "Van theft, North Gate",
                Description = "Synthetic integration fixture. Shares one recorded person.",
                CrimeTypeId = crimeType.Id,
                Status = CaseStatus.Closed,
                Severity = CaseSeverity.Critical,
                IncidentDate = Now.AddDays(-9),
                ClosedDate = Now.AddDays(-2),
                CreatedById = hostId,
                CreatedAt = Now.AddDays(-9),
                UpdatedAt = Now.AddDays(-2)
            };
            context.CrimeCases.AddRange(focusCase, linkedCase);

            var scene = new Location
            {
                Name = "Central HQ storage yard",
                Type = "Incident",
                Address = "Dock Road",
                DistrictId = district.Id,
                Latitude = 6.9,
                Longitude = 79.84
            };
            context.Locations.Add(scene);
            await context.SaveChangesAsync();

            context.CaseLocations.AddRange(
                new CaseLocation
                {
                    CrimeCaseId = focusCase.Id,
                    LocationId = scene.Id,
                    LocationRole = "Incident",
                    AssociatedAt = Now.AddDays(-5)
                },
                new CaseLocation
                {
                    CrimeCaseId = linkedCase.Id,
                    LocationId = scene.Id,
                    LocationRole = "Incident",
                    AssociatedAt = Now.AddDays(-9)
                });

            var suspect = new Person
            {
                FirstName = "Dana",
                LastName = "Test",
                NationalIdNumber = SensitiveNationalId,
                PhoneNumber = SensitivePhone,
                Email = SensitiveEmail,
                Address = SensitiveAddress,
                DateOfBirth = new DateTime(1988, 4, 3),
                CreatedById = hostId
            };
            context.Persons.Add(suspect);
            await context.SaveChangesAsync();

            context.CasePersons.AddRange(
                new CasePerson
                {
                    CrimeCaseId = focusCase.Id,
                    PersonId = suspect.Id,
                    Role = PersonRole.Suspect,
                    Notes = "Recorded association in the fixture.",
                    AssociatedAt = Now.AddDays(-4),
                    AssociatedById = hostId
                },
                new CasePerson
                {
                    CrimeCaseId = linkedCase.Id,
                    PersonId = suspect.Id,
                    Role = PersonRole.Witness,
                    AssociatedAt = Now.AddDays(-8),
                    AssociatedById = hostId
                });

            var investigation = new Investigation
            {
                CrimeCaseId = focusCase.Id,
                Title = "Investigation — " + CaseNumber,
                Summary = "Synthetic fixture. Reviewing access logs.",
                StartedAt = Now.AddDays(-4),
                LeadInvestigatorId = leadId,
                Status = 1,
                CreatedAt = Now.AddDays(-4),
                UpdatedAt = Now.AddDays(-1)
            };
            context.Investigations.Add(investigation);
            await context.SaveChangesAsync();

            var accessLog = new Evidence
            {
                InvestigationId = investigation.Id,
                Title = "Storage unit access log export",
                Description = "Synthetic fixture evidence.",
                EvidenceType = 3,
                EvidenceStatus = 1,
                CollectedAt = Now.AddDays(-3),
                CollectedByUserId = leadId,
                CreatedAt = Now.AddDays(-3)
            };
            context.Evidence.Add(accessLog);
            context.EvidenceFiles.Add(new EvidenceFile
            {
                EvidenceId = accessLog.Id,
                FileName = "raw-fixture-footage.mp4",
                FileExtension = "mp4",
                ContentType = "video/mp4",
                FileSizeBytes = 2048,
                StoragePath = "cases/raw-fixture-footage.mp4",
                ChecksumSha256 = "deadbeeffixture",
                UploadedById = hostId
            });
            context.TimelineEvents.Add(new TimelineEvent
            {
                InvestigationId = investigation.Id,
                Title = "Case opened",
                EventDateTime = Now.AddDays(-5),
                EventType = 1,
                CreatedById = hostId,
                CreatedAt = Now.AddDays(-5)
            });
            context.InvestigationNotes.AddRange(
                new InvestigationNote
                {
                    InvestigationId = investigation.Id,
                    Content = PublicNote,
                    NoteType = 1,
                    CreatedById = leadId,
                    IsPrivate = false,
                    CreatedAt = Now.AddDays(-3)
                },
                new InvestigationNote
                {
                    InvestigationId = investigation.Id,
                    Content = PrivateNote,
                    NoteType = 1,
                    CreatedById = leadId,
                    IsPrivate = true,
                    CreatedAt = Now.AddDays(-3)
                });

            // Thirteen extra authored rows so the list needs a second page.
            for (var i = 1; i <= 13; i++)
            {
                context.Reports.Add(new Report
                {
                    Title = $"ITRP paged report {i:00}",
                    Description = "Synthetic fixture row used only to exercise pagination.",
                    ReportType = ReportType.Custom,
                    Source = ReportSource.AnalystAuthored,
                    Status = ReportStatus.Draft,
                    RequestedById = hostId,
                    RequestedAt = Now.AddDays(-1),
                    CreatedAt = Now.AddMinutes(-i)
                });
            }

            await context.SaveChangesAsync();
        }

        _caseId = focusCase.Id;

        // Documents are produced by the real service so their stored parameters,
        // source and status are exactly what the application writes.
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();

        var trend = await context.Reports.FirstOrDefaultAsync(r =>
            r.ReportType == ReportType.StatisticalAnalysis && r.RequestedById == hostId);
        if (trend is null)
        {
            var generated = await reports.GenerateAsync(
                new GenerateReportRequest { Type = ReportType.StatisticalAnalysis }, hostId, true);
            generated.Succeeded.Should().BeTrue();
            trend = await context.Reports.FirstAsync(r => r.Id == generated.Value);
        }

        _trendReportId = trend.Id;

        var caseReport = await context.Reports.FirstOrDefaultAsync(r =>
            r.ReportType == ReportType.CaseSummary && r.CrimeCaseId == _caseId);
        if (caseReport is null)
        {
            var generated = await reports.GenerateAsync(
                new GenerateReportRequest { Type = ReportType.CaseSummary, CaseId = _caseId }, hostId, true);
            generated.Succeeded.Should().BeTrue();
            caseReport = await context.Reports.FirstAsync(r => r.Id == generated.Value);
        }

        _caseReportId = caseReport.Id;

        var investigationReport = await context.Reports.FirstOrDefaultAsync(r =>
            r.ReportType == ReportType.InvestigationProgress && r.CrimeCaseId == _caseId);
        if (investigationReport is null)
        {
            var generated = await reports.GenerateAsync(
                new GenerateReportRequest
                {
                    Type = ReportType.InvestigationProgress, CaseId = _caseId
                },
                hostId, true);
            generated.Succeeded.Should().BeTrue();
            investigationReport = await context.Reports.FirstAsync(r => r.Id == generated.Value);
        }

        _investigationReportId = investigationReport.Id;

        var authored = await context.Reports.FirstOrDefaultAsync(r => r.Title == "ITRP analyst digest");
        if (authored is null)
        {
            var created = await reports.CreateAsync(new CreateAnalystReportRequest
            {
                Title = "ITRP analyst digest",
                Type = ReportType.Custom,
                Summary = "Synthetic fixture briefing written by an analyst.",
                Content = "Observed increase in recorded storage-unit incidents.",
                PeriodFrom = Now.AddDays(-29).Date,
                PeriodTo = Now.Date
            }, hostId);
            created.Succeeded.Should().BeTrue();
            authored = await context.Reports.FirstAsync(r => r.Id == created.Value);
        }

        _authoredReportId = authored.Id;
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

    /// <summary>Title the service wrote for the generated trend report.</summary>
    private async Task<string> TrendTitleAsync()
    {
        using var scope = NewScope();
        return (await scope.ServiceProvider.GetRequiredService<NetraDbContext>()
            .Reports.AsNoTracking().FirstAsync(r => r.Id == _trendReportId)).Title;
    }

    // ------------------------------------------------------------------
    // Anonymous access and the read policy
    // ------------------------------------------------------------------

    [Fact]
    public async Task Reports_AnonymousRequests_RedirectToLogin()
    {
        var client = AnonymousClient();

        var responses = await Task.WhenAll(
            client.GetAsync("/reports"),
            client.GetAsync($"/reports/details/{_caseReportId}"),
            client.GetAsync("/reports/generate"),
            client.GetAsync("/reports/create"),
            client.GetAsync($"/reports/export/{_caseReportId}"),
            client.PostAsync("/reports/generate", new FormUrlEncodedContent(new Dictionary<string, string>())));

        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location!.ToString().Should().Contain("/Account/Login");
        }
    }

    [Theory]
    [InlineData("System Administrator", "reports.it.read.admin@netra.local")]
    [InlineData("Senior Officer", "reports.it.read.senior@netra.local")]
    [InlineData("Police / Intelligence Analyst", "reports.it.read.analyst@netra.local")]
    [InlineData("Investigator", "reports.it.read.investigator@netra.local")]
    public async Task Reports_EveryCaseWorkingRoleMayReadTheLibrary(string role, string email)
    {
        var client = await SignInAsync(email, role);

        var response = await client.GetAsync("/reports");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Reports &amp; Intelligence Summaries");
        html.Should().Contain("Report Library");
        html.Should().Contain("ITRP analyst digest");
        html.Should().Contain("Total reports");
        html.Should().NotContain("not yet implemented");
    }

    // ------------------------------------------------------------------
    // Generate / create / edit / archive need the manage policy
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("System Administrator", "reports.it.manage.admin@netra.local")]
    [InlineData("Senior Officer", "reports.it.manage.senior@netra.local")]
    [InlineData("Police / Intelligence Analyst", "reports.it.manage.analyst@netra.local")]
    public async Task Manage_RoutesAreOpenToTheManagingRoles(string role, string email)
    {
        var client = await SignInAsync(email, role);

        var generate = await client.GetAsync("/reports/generate");
        var create = await client.GetAsync("/reports/create");
        var list = await client.GetStringAsync("/reports");

        generate.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        list.Should().Contain("/reports/generate");
    }

    [Fact]
    public async Task Manage_InvestigatorIsForbiddenButMayStillRead()
    {
        var client = await SignInAsync("reports.it.manage.investigator@netra.local", "Investigator");

        var index = await client.GetAsync("/reports");
        var generate = await client.GetAsync("/reports/generate");
        var create = await client.GetAsync("/reports/create");
        var indexHtml = await index.Content.ReadAsStringAsync();

        index.StatusCode.Should().Be(HttpStatusCode.OK);
        generate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        indexHtml.Should().NotContain("/reports/generate");
        indexHtml.Should().NotContain("/reports/create");

        // A direct POST to the mutating routes is refused before any service call.
        var editForm = AuthenticationScreenTests.Form(("Title", "forged title"));
        var archive = await client.PostAsync($"/reports/archive/{_authoredReportId}", editForm);
        var edit = await client.PostAsync($"/reports/edit/{_authoredReportId}", editForm);

        archive.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        edit.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Manage_PostsWithoutAntiForgeryToken_AreRejected()
    {
        var client = await SignInAsync("reports.it.forgery@netra.local", "Police / Intelligence Analyst");

        var generate = await client.PostAsync("/reports/generate", AuthenticationScreenTests.Form(
            ("Type", "StatisticalAnalysis")));
        var create = await client.PostAsync("/reports/create", AuthenticationScreenTests.Form(
            ("Title", "ITRP forged without token"), ("Type", "Custom")));

        generate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------
    // Analytical content gate: report CONTENT, not just the route
    // ------------------------------------------------------------------

    [Fact]
    public async Task AnalyticalReport_IsInvisibleAndUnopenableWithoutAnalyticalAccess()
    {
        var analyst = await SignInAsync("reports.it.analysis.analyst@netra.local", "Police / Intelligence Analyst");
        var investigator = await SignInAsync("reports.it.analysis.investigator@netra.local", "Investigator");
        var trendTitle = await TrendTitleAsync();

        // The persisted filter band echoes the search term into its own input, so the
        // presence of a row is proven by its details link rather than by title text.
        var trendFragment = trendTitle.Split('—').Last().Trim();
        var trendPath = $"/reports/details/{_trendReportId:D}";
        var probe = "/reports?search=" + Uri.EscapeDataString(trendFragment);

        var analystList = await analyst.GetStringAsync(probe);
        var investigatorList = await investigator.GetStringAsync(probe);

        trendTitle.Should().Contain("Crime Trend Analysis");
        trendFragment.Should().NotBeEmpty();
        analystList.Should().Contain(trendPath);
        investigatorList.Should().NotContain(trendPath,
            "an analytical row must not appear for a reader without analytical access");
        investigatorList.Should().Contain("No reports match the selected filters");

        var analystDocument = await analyst.GetAsync($"/reports/details/{_trendReportId}");
        var investigatorDocument = await investigator.GetAsync($"/reports/details/{_trendReportId}");

        // The same safe 404 as a missing report: existence is never confirmed.
        analystDocument.StatusCode.Should().Be(HttpStatusCode.OK);
        investigatorDocument.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The tab and the type dropdown drop analytical options entirely.
        analystList.Should().Contain("option value=\"StatisticalAnalysis\"");
        investigatorList.Should().NotContain("Risk assessments");
        investigatorList.Should().NotContain("option value=\"StatisticalAnalysis\"");

        using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var investigatorId = (await context.Users.FirstAsync(
            u => u.Email == "reports.it.analysis.investigator@netra.local")).Id;
        (await context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportAccessDenied && l.UserId == investigatorId)).Should().BeTrue();
        (await context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportViewed &&
            l.EntityId == _trendReportId &&
            l.UserId == investigatorId)).Should().BeFalse("a refused read is never logged as a view");
    }

    [Fact]
    public async Task AnalyticalTab_IsNotOfferedAndEmptyForNonAnalyticalReaders()
    {
        var investigator = await SignInAsync("reports.it.tabs.investigator@netra.local", "Investigator");

        var all = await investigator.GetStringAsync("/reports");
        var trendTab = await investigator.GetStringAsync("/reports?group=TrendAnalysis");

        all.Should().Contain("Investigations");
        trendTab.Should().Contain("No reports match the selected filters");
        trendTab.Should().NotContain("Crime Trend Analysis");
    }

    [Fact]
    public async Task CaseScopedReport_IsReadableByAnInvestigator()
    {
        var investigator = await SignInAsync("reports.it.casescope@netra.local", "Investigator");

        var response = await investigator.GetAsync($"/reports/details/{_caseReportId}");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain(CaseNumber);
        html.Should().Contain("Report Document");
    }

    // ------------------------------------------------------------------
    // The document page itself
    // ------------------------------------------------------------------

    [Fact]
    public async Task Details_RendersABrandedDocumentWithFactsAndDisclaimer()
    {
        var client = await SignInAsync("reports.it.details@netra.local", "System Administrator");

        var html = await client.GetStringAsync($"/reports/details/{_caseReportId}");

        html.Should().Contain("National Electronic Tracking &amp; Records Analysis");
        html.Should().Contain("netra-doc__header");
        html.Should().Contain("NETRA System Generated");
        html.Should().Contain("Executive summary");
        html.Should().Contain("Persons of interest");
        html.Should().Contain("Dana Test");
        html.Should().Contain("Linked cases");
        html.Should().Contain(LinkedCaseNumber);
        html.Should().Contain("does not establish guilt");
        html.Should().Contain($"Report reference {_caseReportId:N}");
        html.Should().Contain("Reporting period");
        html.Should().Contain("Timeline summary");
        html.Should().NotContain(PrivateNote, "private investigation notes are restricted content");
    }

    [Fact]
    public async Task InvestigationSummaryDocument_ShowsNonPrivateNotesAndWithholdsPrivateOnes()
    {
        var client = await SignInAsync("reports.it.investigation.doc@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync($"/reports/details/{_investigationReportId}");

        html.Should().Contain("Investigation Summary");
        html.Should().Contain("Investigation notes (non-private)");
        html.Should().Contain(PublicNote);
        html.Should().NotContain(PrivateNote);
        html.Should().Contain("Case " + CaseNumber);
    }

    [Fact]
    public async Task Details_NeverCarrySensitiveIdentityOrContactData()
    {
        var client = await SignInAsync("reports.it.minimise@netra.local", "Police / Intelligence Analyst");

        foreach (var path in new[]
                 {
                     $"/reports/details/{_caseReportId}",
                     $"/reports/details/{_investigationReportId}",
                     $"/reports/details/{_trendReportId}"
                 })
        {
            var html = await client.GetStringAsync(path);

            html.Should().NotContain(SensitiveNationalId, $"{path} must not repeat a national identity number");
            html.Should().NotContain(SensitivePhone);
            html.Should().NotContain(SensitiveEmail);
            html.Should().NotContain(SensitiveAddress);
            html.Should().NotContain("raw-fixture-footage.mp4");
            html.Should().NotContain("deadbeeffixture");
            html.Should().NotContain("reports.it.host@netra.local");
            html.Should().NotContain("ValidPassw0rd");
        }
    }

    [Fact]
    public async Task TrendDocument_ReusesTheAnalyticsServiceAndRendersServerSideSvg()
    {
        var client = await SignInAsync("reports.it.trend@netra.local", "Senior Officer");

        var html = await client.GetStringAsync($"/reports/details/{_trendReportId}");

        html.Should().Contain("analytical indicators");
        html.Should().Contain("<svg", "charts are rendered on the server as SVG");
        html.Should().NotContain("canvas");
        using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var totals = await context.CrimeCases.CountAsync(c => c.IncidentDate >= Now.AddDays(-40));
        totals.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Details_UnknownOrMalformedId_ReturnsNotFound()
    {
        var client = await SignInAsync("reports.it.missing.admin@netra.local", "System Administrator");

        var missing = await client.GetAsync($"/reports/details/{Guid.NewGuid()}");
        var malformed = await client.GetAsync("/reports/details/not-a-guid");

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        malformed.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).Should().NotContain("ITRP");
    }

    // ------------------------------------------------------------------
    // Filters, tabs, search and pagination — all server-side GET
    // ------------------------------------------------------------------

    [Fact]
    public async Task Index_FilterValuesPersistInTheRenderedForm()
    {
        var client = await SignInAsync("reports.it.filters@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync(
            "/reports?search=ITRP&type=CaseSummary&source=SystemGenerated&status=Ready&group=Investigations");

        html.Should().Contain("value=\"ITRP\"");
        html.Should().Contain("value=\"CaseSummary\" selected=\"selected\"");
        html.Should().Contain("value=\"SystemGenerated\" selected=\"selected\"");
        html.Should().Contain("value=\"Ready\" selected=\"selected\"");
        html.Should().Contain("is-active");
        html.Should().Contain("Clear filters");
        html.Should().Contain(CaseNumber);
        html.Should().NotContain("ITRP analyst digest");
    }

    [Fact]
    public async Task Index_BogusFilterValues_RenderSafely()
    {
        var client = await SignInAsync("reports.it.bogus@netra.local", "Police / Intelligence Analyst");

        var type = await client.GetAsync("/reports?type=NotAReportType");
        var source = await client.GetAsync("/reports?source=99");
        var page = await client.GetAsync("/reports?page=-4");
        var search = await client.GetAsync("/reports?search=%3Cscript%3Ealert(1)%3C%2Fscript%3E");

        foreach (var response in new[] { type, source, page, search })
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // The reflected search term is HTML-encoded, never executed.
        var html = await search.Content.ReadAsStringAsync();
        html.Should().NotContain("<script>alert(1)</script>");
    }

    [Fact]
    public async Task Index_ImpossibleSearch_ShowsHonestEmptyState()
    {
        var client = await SignInAsync("reports.it.empty@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync("/reports?search=ITRP-no-such-report-anywhere");

        html.Should().Contain("No reports match the selected filters");
        html.Should().NotContain("netra-report-card");
    }

    [Fact]
    public async Task Index_PagesTheLibraryTwelveRowsAtATime()
    {
        var client = await SignInAsync("reports.it.paging@netra.local", "System Administrator");

        var first = await client.GetStringAsync("/reports?search=ITRP+paged+report");
        var second = await client.GetStringAsync("/reports?search=ITRP+paged+report&page=2");

        // Thirteen fixture rows: the newest twelve on page 1, the oldest on page 2.
        first.Should().Contain("ITRP paged report 01");
        first.Should().Contain("ITRP paged report 12");
        first.Should().NotContain("ITRP paged report 13");
        second.Should().Contain("ITRP paged report 13");
        second.Should().NotContain("ITRP paged report 01");
    }

    // ------------------------------------------------------------------
    // Generation, authoring, editing and archiving through HTTP
    // ------------------------------------------------------------------

    [Fact]
    public async Task Generate_CaseSummaryFromTheForm_PersistsASystemReportAndAuditsIt()
    {
        var client = await SignInAsync("reports.it.generate@netra.local", "Police / Intelligence Analyst");
        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(await client.GetStringAsync("/reports/generate"));

        var response = await client.PostAsync("/reports/generate", AuthenticationScreenTests.Form(
            ("Type", "CaseSummary"),
            ("CaseId", _caseId.ToString()),
            ("DateFrom", Now.AddDays(-29).ToString("yyyy-MM-dd")),
            ("DateTo", Now.ToString("yyyy-MM-dd")),
            ("AnalystObservations", "Synthetic observation recorded by the analyst."),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("/reports/details/");

        var html = await client.GetStringAsync(location);
        html.Should().Contain("NETRA System Generated");
        html.Should().Contain(CaseNumber);
        html.Should().Contain("Analyst-authored observations");
        html.Should().Contain("Synthetic observation recorded by the analyst.");

        using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var reportId = Guid.Parse(location.Split('/').Last());
        var stored = await context.Reports.AsNoTracking().FirstAsync(r => r.Id == reportId);
        stored.Source.Should().Be(ReportSource.SystemGenerated);
        stored.Status.Should().Be(ReportStatus.Ready);
        stored.ReportType.Should().Be(ReportType.CaseSummary);
        stored.GeneratedFileSizeBytes.Should().BeNull("the report stores parameters, not a copy of the data");
        stored.Content.Should().Be("Synthetic observation recorded by the analyst.");

        (await context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportGenerated && l.EntityId == reportId)).Should().BeTrue();
        (await context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportViewed && l.EntityId == reportId)).Should().BeTrue();
    }

    [Fact]
    public async Task Generate_RiskReportForAnInvestigatorPolicyIsRefusedAndNothingIsStored()
    {
        var client = await SignInAsync("reports.it.generatetrend.investigator@netra.local", "Investigator");

        var response = await client.PostAsync("/reports/generate", AuthenticationScreenTests.Form(
            ("Type", "StatisticalAnalysis")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Generate_HandAuthoredTypeIsRefusedByTheForm()
    {
        var client = await SignInAsync("reports.it.generate.custom@netra.local", "System Administrator");
        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(await client.GetStringAsync("/reports/generate"));

        var response = await client.PostAsync("/reports/generate", AuthenticationScreenTests.Form(
            ("Type", "Custom"),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Select a report type that can be generated.");
    }

    [Fact]
    public async Task Create_ThenEdit_ThenArchive_RunThroughTheDocumentedLifecycle()
    {
        var client = await SignInAsync("reports.it.lifecycle@netra.local", "Police / Intelligence Analyst");

        async Task<string> TokenAsync(string path) =>
            AuthenticationScreenTests.ExtractAntiForgeryToken(await client.GetStringAsync(path));

        var created = await client.PostAsync("/reports/create", AuthenticationScreenTests.Form(
            ("Title", "ITRP lifecycle briefing"),
            ("Type", "IntelligenceBrief"),
            ("Summary", "Authored briefing recorded through the analyst form."),
            ("Content", "First authored paragraph.\n\nSecond authored paragraph."),
            ("PeriodFrom", Now.AddDays(-14).ToString("yyyy-MM-dd")),
            ("PeriodTo", Now.ToString("yyyy-MM-dd")),
            ("__RequestVerificationToken", await TokenAsync("/reports/create"))));

        created.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var reportId = Guid.Parse(created.Headers.Location!.ToString().Split('/').Last());

        var document = await client.GetStringAsync($"/reports/details/{reportId}");
        document.Should().Contain("Analyst Authored");
        document.Should().Contain("ITRP lifecycle briefing");
        document.Should().Contain("First authored paragraph.");
        document.Should().Contain("Analyst-authored observations");

        var edited = await client.PostAsync($"/reports/edit/{reportId}", AuthenticationScreenTests.Form(
            ("Title", "ITRP lifecycle briefing v2"),
            ("Type", "IntelligenceBrief"),
            ("Summary", "Revised briefing summary."),
            ("Content", "Revised authored paragraph."),
            ("PeriodFrom", Now.AddDays(-14).ToString("yyyy-MM-dd")),
            ("PeriodTo", Now.ToString("yyyy-MM-dd")),
            ("__RequestVerificationToken", await TokenAsync($"/reports/edit/{reportId}"))));
        edited.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var afterEdit = await client.GetStringAsync($"/reports/details/{reportId}");
        afterEdit.Should().Contain("ITRP lifecycle briefing v2");
        afterEdit.Should().Contain("Revised authored paragraph.");

        var archived = await client.PostAsync($"/reports/archive/{reportId}", AuthenticationScreenTests.Form(
            ("__RequestVerificationToken", await TokenAsync($"/reports/details/{reportId}"))));
        archived.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var archivedPage = await client.GetStringAsync($"/reports/details/{reportId}");
        archivedPage.Should().Contain("Archived");
        archivedPage.Should().NotContain($"/reports/edit/{reportId}", "an archived report offers no edit control");

        using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var stored = await context.Reports.AsNoTracking().FirstAsync(r => r.Id == reportId);
        stored.Status.Should().Be(ReportStatus.Archived);
        stored.ArchivedAt.Should().NotBeNull();
        stored.Source.Should().Be(ReportSource.AnalystAuthored);
        (await context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportCreated && l.EntityId == reportId)).Should().BeTrue();
        (await context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportEdited && l.EntityId == reportId)).Should().BeTrue();
        (await context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportArchived && l.EntityId == reportId)).Should().BeTrue();
    }

    [Fact]
    public async Task Create_InvalidForm_RedisplayErrorsAndStoresNothing()
    {
        var client = await SignInAsync("reports.it.invalid@netra.local", "System Administrator");
        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(await client.GetStringAsync("/reports/create"));
        using var beforeScope = NewScope();
        var before = await beforeScope.ServiceProvider.GetRequiredService<NetraDbContext>().Reports.CountAsync();

        var response = await client.PostAsync("/reports/create", AuthenticationScreenTests.Form(
            ("Title", "tiny"),
            ("Type", "Custom"),
            ("Summary", new string('x', 2001)),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Title must be between 6 and 200 characters.");
        html.Should().Contain("Summary is limited to 2000 characters.");

        using var afterScope = NewScope();
        (await afterScope.ServiceProvider.GetRequiredService<NetraDbContext>().Reports.CountAsync())
            .Should().Be(before);
    }

    [Fact]
    public async Task Edit_SystemGeneratedReport_IsRefusedWithAPolicyMessage()
    {
        var client = await SignInAsync("reports.it.immutable@netra.local", "Senior Officer");

        var response = await client.GetAsync($"/reports/edit/{_caseReportId}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var html = await client.GetStringAsync(response.Headers.Location!.ToString());
        html.Should().Contain("System-generated facts are immutable");

        using var scope = NewScope();
        var stored = await scope.ServiceProvider.GetRequiredService<NetraDbContext>()
            .Reports.AsNoTracking().FirstAsync(r => r.Id == _caseReportId);
        stored.Source.Should().Be(ReportSource.SystemGenerated);
    }

    // ------------------------------------------------------------------
    // Export and print
    // ------------------------------------------------------------------

    [Fact]
    public async Task Export_ServesAServerGeneratedCsvOfTheSameDocument()
    {
        var client = await SignInAsync("reports.it.export@netra.local", "Police / Intelligence Analyst");

        var response = await client.GetAsync($"/reports/export/{_caseReportId}");
        var csv = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".csv");
        csv.Should().Contain("NETRA report export");
        csv.Should().Contain("Report type,Case Summary");
        csv.Should().Contain("Source,NETRA System Generated");
        csv.Should().Contain("Dana Test");
        csv.Should().NotContain(SensitiveNationalId);
        csv.Should().NotContain(PrivateNote);
        csv.Should().NotContain("<script");

        using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        (await context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.ReportExported && l.EntityId == _caseReportId)).Should().BeTrue();
    }

    [Fact]
    public async Task Export_AnalyticalReportIsNotDownloadableWithoutAnalyticalAccess()
    {
        var client = await SignInAsync("reports.it.export.investigator@netra.local", "Investigator");

        var analytical = await client.GetAsync($"/reports/export/{_trendReportId}");
        var readable = await client.GetAsync($"/reports/export/{_caseReportId}");

        analytical.StatusCode.Should().Be(HttpStatusCode.NotFound);
        readable.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PrintStyles_AreServedAndTheDocumentUsesThem()
    {
        var client = await SignInAsync("reports.it.print@netra.local", "System Administrator");

        var css = await client.GetStringAsync("/css/netra.css");
        var html = await client.GetStringAsync($"/reports/details/{_trendReportId}");

        css.Should().Contain("@media print");
        css.Should().Contain(".netra-sidebar");
        css.Should().Contain(".netra-doc");
        html.Should().Contain("browser&#x27;s print command", "the page tells the user how to print");
        html.Should().Contain("netra-doc__footer");
    }

    // ------------------------------------------------------------------
    // No JavaScript, ever
    // ------------------------------------------------------------------

    [Fact]
    public async Task ReportPages_EmitNoJavaScript()
    {
        var client = await SignInAsync("reports.it.nojs@netra.local", "Police / Intelligence Analyst");

        var paths = new[]
        {
            "/reports",
            "/reports?search=ITRP&type=CaseSummary",
            $"/reports/details/{_caseReportId}",
            $"/reports/details/{_trendReportId}",
            "/reports/generate",
            "/reports/create",
            $"/reports/edit/{_authoredReportId}"
        };

        foreach (var path in paths)
        {
            var html = await client.GetStringAsync(path);

            html.Should().NotContain("<script", $"{path} must stay script-free");
            html.Should().NotContain("javascript:", $"{path} must not use javascript: URLs");
            html.Should().NotContain("onclick", $"{path} must not use inline handlers");
            html.Should().NotContain("onchange", $"{path} must not use inline handlers");
            html.Should().NotContain("setTimeout");
            html.Should().NotContain(".js", $"{path} must not reference a script file");
        }
    }

    [Fact]
    public async Task Reports_DoNotDisturbTheRestOfTheShell()
    {
        var client = await SignInAsync("reports.it.regression@netra.local", "Police / Intelligence Analyst");

        var dashboard = await client.GetStringAsync("/Dashboard");
        var alerts = await client.GetStringAsync("/alerts");
        var reports = await client.GetStringAsync("/reports");

        dashboard.Should().Contain("href=\"/reports\"");
        alerts.Should().Contain("Alerts");
        reports.Should().Contain("Report Library");
    }
}
