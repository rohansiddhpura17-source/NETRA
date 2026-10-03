using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// End-to-end coverage of the case module over HTTP. The application runs with the
/// in-memory EF provider substituted for SQL Server; see the SQL Server verification
/// notes for what that does and does not prove.
/// </summary>
public class CaseOperationsTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";

    private readonly NetraWebFactory _factory;

    public CaseOperationsTests(NetraWebFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    private async Task<HttpClient> SignInAsync(string email, string role)
    {
        await _factory.SeedReferenceDataAsync();
        await _factory.SeedUserAsync(email, Password, role);
        var client = CreateClient();

        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/Account/Login"));

        var response = await client.PostAsync("/Account/Login", Form(
            ("UserName", email),
            ("Password", Password),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    /// <summary>Creates a case through the HTTP endpoint and returns its id.</summary>
    private async Task<Guid> CreateCaseAsync(
        HttpClient client,
        string title = "Integration test case",
        string status = "Open",
        string severity = "Medium")
    {
        var page = await client.GetStringAsync("/Cases/Create");
        var token = ExtractToken(page);

        var response = await client.PostAsync("/Cases/Create", Form(
            ("Title", title),
            ("Description", "Created by an integration test."),
            ("CrimeTypeId", await FirstCrimeTypeIdAsync()),
            ("Status", status),
            ("Severity", severity),
            ("IncidentDate", DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd")),
            ("ModusOperandi", "Under test"),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("/Cases/Details/");

        var location = response.Headers.Location!.OriginalString;
        return Guid.Parse(location["/Cases/Details/".Length..]);
    }

    private static string ExtractToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<t>[^\"]+)\"");
        match.Success.Should().BeTrue();
        return match.Groups["t"].Value;
    }

    private async Task<string> FirstCrimeTypeIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<NETRA.Infrastructure.Data.NetraDbContext>();

        var crimeType = await context.CrimeTypes.FirstAsync();
        return crimeType.Id.ToString();
    }

    private async Task<Guid> FirstPersonIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<NETRA.Infrastructure.Data.NetraDbContext>();

        var person = new NETRA.Domain.People.Person
        {
            FirstName = "Integration",
            LastName = "Person",
            CreatedById = await FirstUserIdAsync(context)
        };

        context.Persons.Add(person);
        await context.SaveChangesAsync();
        return person.Id;
    }

    private Task<Guid> FirstUserIdAsync(NETRA.Infrastructure.Data.NetraDbContext context) =>
        context.Users.Select(u => u.Id).FirstAsync();

    // ------------------------------------------------------ authorization

    [Fact]
    public async Task AnonymousUser_CannotReachCaseList()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/Cases");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task AnonymousUser_CannotCreateCase()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/Cases/Create");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task AuthenticatedInvestigator_CanReadCaseList()
    {
        var client = await SignInAsync("case.read@netra.local", "Investigator");

        var response = await client.GetAsync("/Cases");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Investigator_CanCreateCase()
    {
        var client = await SignInAsync("case.create@netra.local", "Investigator");

        var response = await client.GetAsync("/Cases/Create");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Investigator_CanEditCase()
    {
        var client = await SignInAsync("case.edit@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client);

        var response = await client.GetAsync($"/Cases/Edit/{caseId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -------------------------------------------------------- list screen

    [Fact]
    public async Task CaseList_RendersFiltersAndHeaders()
    {
        var client = await SignInAsync("case.filters@netra.local", "Investigator");

        var html = await client.GetStringAsync("/Cases");

        html.Should().Contain("Crime Cases");
        html.Should().Contain("name=\"Search\"");
        html.Should().Contain("name=\"CrimeTypeId\"");
        html.Should().Contain("name=\"DistrictId\"");
        html.Should().Contain("name=\"Status\"");
        html.Should().Contain("name=\"Severity\"");
        html.Should().Contain("name=\"IncidentDateFrom\"");
        html.Should().Contain("name=\"IncidentDateTo\"");
        html.Should().Contain("Clear filters");
        html.Should().Contain("Register new case");
    }

    [Fact]
    public async Task CaseList_RendersColumnHeaders()
    {
        var client = await SignInAsync("case.columns@netra.local", "Investigator");

        var html = await client.GetStringAsync("/Cases");

        foreach (var column in new[]
                 {
                     "Case number", "Crime type", "Incident date", "District",
                     "Status", "Severity", "Investigator", "Actions"
                 })
        {
            html.Should().Contain(column);
        }
    }

    [Fact]
    public async Task CaseList_ShowsCreatedCase()
    {
        var client = await SignInAsync("case.show@netra.local", "Investigator");
        await CreateCaseAsync(client, "Appears in the list");

        var html = await client.GetStringAsync("/Cases");

        html.Should().Contain("Appears in the list");
    }

    [Fact]
    public async Task CaseList_ServerSideSearchFilterNarrowsResults()
    {
        var client = await SignInAsync("case.search@netra.local", "Investigator");
        await CreateCaseAsync(client, "Highly distinctive marker case");

        var html = await client.GetStringAsync("/Cases?Search=distinctive%20marker");

        html.Should().Contain("Highly distinctive marker case");
    }

    [Fact]
    public async Task CaseList_SeverityFilterIsAppliedServerSide()
    {
        var client = await SignInAsync("case.sevfilter@netra.local", "Investigator");
        await CreateCaseAsync(client, "Low severity sample", severity: "Low");

        var html = await client.GetStringAsync("/Cases?Severity=Critical");

        html.Should().NotContain("Low severity sample");
    }

    [Fact]
    public async Task CaseList_ContainsNoJavaScript()
    {
        var client = await SignInAsync("case.nojs@netra.local", "Investigator");

        var html = await client.GetStringAsync("/Cases");

        html.Should().NotContain("<script");
        html.Should().NotContain("onclick");
    }

    // ------------------------------------------------------- create screen

    [Fact]
    public async Task CreateCaseScreen_RendersAllRequiredSections()
    {
        var client = await SignInAsync("create.sections@netra.local", "Investigator");

        var html = await client.GetStringAsync("/Cases/Create");

        html.Should().Contain("Register New Crime Case");
        html.Should().Contain("Case Information");
        html.Should().Contain("Location Details");
        html.Should().Contain("Investigation");
        html.Should().Contain("Related Persons");
        html.Should().Contain("Evidence Attachments");
        html.Should().Contain("name=\"CaseNumber\"");
        html.Should().Contain("name=\"CrimeTypeId\"");
        html.Should().Contain("name=\"Status\"");
        html.Should().Contain("name=\"Severity\"");
        html.Should().Contain("name=\"IncidentDate\"");
        html.Should().Contain("name=\"Description\"");
        html.Should().Contain("name=\"DistrictId\"");
        html.Should().Contain("name=\"Address\"");
        html.Should().Contain("name=\"Latitude\"");
        html.Should().Contain("name=\"Longitude\"");
        html.Should().Contain("name=\"InvestigatorId\"");
        html.Should().Contain("name=\"ModusOperandi\"");
    }

    [Fact]
    public async Task CreateCase_WithoutAntiForgeryToken_IsRejected()
    {
        var client = await SignInAsync("create.af@netra.local", "Investigator");

        var response = await client.PostAsync("/Cases/Create", Form(
            ("Title", "No token"),
            ("CrimeTypeId", Guid.NewGuid().ToString()),
            ("Status", "Open"),
            ("Severity", "Low"),
            ("IncidentDate", DateTime.Today.ToString("yyyy-MM-dd"))));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateCase_WithMissingTitle_RerendersWithValidationError()
    {
        var client = await SignInAsync("create.invalid@netra.local", "Investigator");
        var token = ExtractToken(await client.GetStringAsync("/Cases/Create"));

        var response = await client.PostAsync("/Cases/Create", Form(
            ("Title", ""),
            ("CrimeTypeId", await FirstCrimeTypeIdAsync()),
            ("Status", "Open"),
            ("Severity", "Low"),
            ("IncidentDate", DateTime.Today.ToString("yyyy-MM-dd")),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("A case title is required");
    }

    [Fact]
    public async Task CreateCase_WithInvalidLatitude_RerendersWithValidationError()
    {
        var client = await SignInAsync("create.coords@netra.local", "Investigator");
        var token = ExtractToken(await client.GetStringAsync("/Cases/Create"));

        var response = await client.PostAsync("/Cases/Create", Form(
            ("Title", "Bad latitude"),
            ("CrimeTypeId", await FirstCrimeTypeIdAsync()),
            ("Status", "Open"),
            ("Severity", "Low"),
            ("IncidentDate", DateTime.Today.ToString("yyyy-MM-dd")),
            ("Latitude", "150"),
            ("__RequestVerificationToken", token)));

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Latitude must be between -90 and 90");
    }

    [Fact]
    public async Task CreateCase_GeneratesCaseNumberAndPersistsCase()
    {
        var client = await SignInAsync("create.happy@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "Persisted case");

        var html = await client.GetStringAsync($"/Cases/Details/{caseId}");

        html.Should().Contain("Persisted case");
        html.Should().MatchRegex("CC-\\d{4}-\\d{4}");
    }

    // ------------------------------------------------------ details screen

    [Fact]
    public async Task CaseDetails_RendersOverviewAndPanels()
    {
        var client = await SignInAsync("details.panels@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "Details panels case");

        var html = await client.GetStringAsync($"/Cases/Details/{caseId}");

        html.Should().Contain("Case Overview");
        html.Should().Contain("Related Persons");
        html.Should().Contain("Locations");
        html.Should().Contain("Evidence");
        html.Should().Contain("Investigation Notes");
        html.Should().Contain("Timeline");
        html.Should().Contain("Association is not evidence of guilt");
    }

    [Fact]
    public async Task CaseDetails_ForUnknownId_ReturnsNotFound()
    {
        var client = await SignInAsync("details.404@netra.local", "Investigator");

        var response = await client.GetAsync($"/Cases/Details/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CaseDetails_ShowsEmptyStateWhenNoPersonsAssociated()
    {
        var client = await SignInAsync("details.empty@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "No persons case");

        var html = await client.GetStringAsync($"/Cases/Details/{caseId}");

        html.Should().Contain("No persons associated");
    }

    // --------------------------------------------------------- edit screen

    [Fact]
    public async Task EditCase_PersistsChangesAndShowsThemInDetails()
    {
        var client = await SignInAsync("edit.happy@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "Original title");

        var token = ExtractToken(await client.GetStringAsync($"/Cases/Edit/{caseId}"));

        var response = await client.PostAsync($"/Cases/Edit/{caseId}", Form(
            ("Title", "Updated title"),
            ("Description", "Updated by test"),
            ("CrimeTypeId", await FirstCrimeTypeIdAsync()),
            ("Status", "Closed"),
            ("Severity", "Critical"),
            ("IncidentDate", DateTime.Today.AddDays(-5).ToString("yyyy-MM-dd")),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var details = await client.GetStringAsync($"/Cases/Details/{caseId}");
        details.Should().Contain("Updated title");
        details.Should().Contain("Case History");
    }

    [Fact]
    public async Task EditCase_PostWithoutToken_IsRejected()
    {
        var client = await SignInAsync("edit.af@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "Edit antiforgery");

        var response = await client.PostAsync($"/Cases/Edit/{caseId}", Form(
            ("Title", "Should not save"),
            ("CrimeTypeId", await FirstCrimeTypeIdAsync()),
            ("Status", "Open"),
            ("Severity", "Low"),
            ("IncidentDate", DateTime.Today.ToString("yyyy-MM-dd"))));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EditCase_WithBlankTitle_RerendersWithValidationError()
    {
        var client = await SignInAsync("edit.invalid@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "Blank title test");
        var token = ExtractToken(await client.GetStringAsync($"/Cases/Edit/{caseId}"));

        var response = await client.PostAsync($"/Cases/Edit/{caseId}", Form(
            ("Title", ""),
            ("CrimeTypeId", await FirstCrimeTypeIdAsync()),
            ("Status", "Open"),
            ("Severity", "Low"),
            ("IncidentDate", DateTime.Today.ToString("yyyy-MM-dd")),
            ("__RequestVerificationToken", token)));

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("A case title is required");
    }

    // ------------------------------------------------------- associations

    [Fact]
    public async Task AddPersonToCase_AppearsOnCaseDetails()
    {
        var client = await SignInAsync("assoc.person@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "Person association case");
        var personId = await FirstPersonIdAsync();

        var token = ExtractToken(await client.GetStringAsync($"/Cases/Details/{caseId}"));

        var response = await client.PostAsync($"/Cases/Details/{caseId}/Persons", Form(
            ("PersonId", personId.ToString()),
            ("Role", "Witness"),
            ("Notes", "Saw the incident."),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var html = await client.GetStringAsync($"/Cases/Details/{caseId}");
        html.Should().Contain("Integration Person");
        html.Should().Contain("Saw the incident.");
    }

    [Fact]
    public async Task AddDuplicatePersonAssociation_IsRejectedWithMessage()
    {
        var client = await SignInAsync("assoc.dupe@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "Duplicate association case");
        var personId = await FirstPersonIdAsync();

        var token = ExtractToken(await client.GetStringAsync($"/Cases/Details/{caseId}"));

        var fields = new (string, string)[]
        {
            ("PersonId", personId.ToString()),
            ("Role", "Witness"),
            ("__RequestVerificationToken", token)
        };

        (await client.PostAsync($"/Cases/Details/{caseId}/Persons", Form(fields)))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);

        var secondToken = ExtractToken(await client.GetStringAsync($"/Cases/Details/{caseId}"));
        await client.PostAsync($"/Cases/Details/{caseId}/Persons", Form(
            ("PersonId", personId.ToString()),
            ("Role", "Witness"),
            ("__RequestVerificationToken", secondToken)));

        var html = await client.GetStringAsync($"/Cases/Details/{caseId}");
        html.Should().Contain("already associated");
    }

    [Fact]
    public async Task AddEvidenceToCase_AppearsOnCaseDetails()
    {
        var client = await SignInAsync("assoc.evidence@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "Evidence case");

        var token = ExtractToken(await client.GetStringAsync($"/Cases/Details/{caseId}"));

        var response = await client.PostAsync($"/Cases/Details/{caseId}/Evidence", Form(
            ("Title", "CCTV segment"),
            ("EvidenceType", "Digital"),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var html = await client.GetStringAsync($"/Cases/Details/{caseId}");
        html.Should().Contain("CCTV segment");
    }

    [Fact]
    public async Task AddInvestigationNote_AppearsOnCaseDetails()
    {
        var client = await SignInAsync("assoc.note@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "Note case");

        var token = ExtractToken(await client.GetStringAsync($"/Cases/Details/{caseId}"));

        var response = await client.PostAsync($"/Cases/Details/{caseId}/Notes", Form(
            ("Content", "Reviewed the access log."),
            ("NoteType", "Observation"),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var html = await client.GetStringAsync($"/Cases/Details/{caseId}");
        html.Should().Contain("Reviewed the access log.");
    }

    [Fact]
    public async Task AddTimelineEvent_AppearsOnCaseDetails()
    {
        var client = await SignInAsync("assoc.timeline@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "Timeline case");

        var token = ExtractToken(await client.GetStringAsync($"/Cases/Details/{caseId}"));

        var response = await client.PostAsync($"/Cases/Details/{caseId}/Timeline", Form(
            ("Title", "Warrant issued"),
            ("EventType", "Warrant"),
            ("EventDateTime", new DateTime(2026, 2, 2, 10, 30, 0).ToString("yyyy-MM-ddTHH:mm")),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var html = await client.GetStringAsync($"/Cases/Details/{caseId}");
        html.Should().Contain("Warrant issued");
    }

    // -------------------------------------------- investigation workspace

    [Fact]
    public async Task InvestigationWorkspace_RendersRequiredPanels()
    {
        var client = await SignInAsync("workspace.panels@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "Workspace case");

        var details = await client.GetStringAsync($"/Cases/Details/{caseId}");
        details.Should().Contain("/Investigations/Details/");

        var investigationId = await InvestigationIdForCaseAsync(caseId);

        var html = await client.GetStringAsync($"/Investigations/Details/{investigationId}");

        html.Should().Contain("Investigation Workspace");
        html.Should().Contain("Case Overview");
        html.Should().Contain("Victim Information");
        html.Should().Contain("Modus Operandi");
        html.Should().Contain("Investigation Notes");
        html.Should().Contain("Suspects");
        html.Should().Contain("Persons of Interest");
        html.Should().Contain("Locations");
        html.Should().Contain("Timeline");
        html.Should().Contain("Evidence");
        html.Should().Contain("Related Information");
    }

    [Fact]
    public async Task InvestigationWorkspace_DoesNotExposeFutureModules()
    {
        var client = await SignInAsync("workspace.scope@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "Scope case");
        var investigationId = await InvestigationIdForCaseAsync(caseId);

        var html = await client.GetStringAsync($"/Investigations/Details/{investigationId}");

        // Later-phase capabilities are acknowledged as unavailable, not presented as working.
        html.Should().Contain("scheduled for later phases");
    }

    [Fact]
    public async Task InvestigationWorkspace_ContainsNoJavaScript()
    {
        var client = await SignInAsync("workspace.nojs@netra.local", "Investigator");
        var caseId = await CreateCaseAsync(client, "No JS workspace");
        var investigationId = await InvestigationIdForCaseAsync(caseId);

        var html = await client.GetStringAsync($"/Investigations/Details/{investigationId}");

        html.Should().NotContain("<script");
    }

    [Fact]
    public async Task InvestigationWorkspace_ForUnknownId_ReturnsNotFound()
    {
        var client = await SignInAsync("workspace.404@netra.local", "Investigator");

        var response = await client.GetAsync($"/Investigations/Details/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Guid> InvestigationIdForCaseAsync(Guid caseId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<NETRA.Infrastructure.Data.NetraDbContext>();

        var investigation = await context.Investigations
            .SingleAsync(i => i.CrimeCaseId == caseId);

        return investigation.Id;
    }
}