using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Application.Abstractions;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.People;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// Real MVC integration coverage for /image-investigation: routing, the dedicated
/// authorisation policy, server-side upload validation, multipart analysis flows,
/// match detail viewing, authorised image previews, audit rows and the no-JavaScript
/// guarantee. Hosted with EF Core's in-memory provider by <see cref="NetraWebFactory"/>;
/// these are not live SQL Server tests.
/// </summary>
public sealed class ImageInvestigationIntegrationTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";
    private readonly NetraWebFactory _factory;
    private readonly Guid _caseId;

    public ImageInvestigationIntegrationTests(NetraWebFactory factory)
    {
        _factory = factory;
        _caseId = SeedImageDataAsync().GetAwaiter().GetResult();
    }

    private async Task<Guid> SeedImageDataAsync()
    {
        await _factory.SeedReferenceDataAsync();
        await _factory.SeedUserAsync("image.host.seed@netra.local", Password, "System Administrator");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var existing = await context.CrimeCases.FirstOrDefaultAsync(c => c.CaseNumber == "IMG-WEB-001");
        if (existing is not null)
        {
            return existing.Id;
        }

        var actor = await context.Users.FirstAsync(u => u.Email == "image.host.seed@netra.local");
        var crimeType = await context.CrimeTypes.FirstAsync(type => type.Code == "THF");
        var person = new Person
        {
            FirstName = "Dana",
            LastName = "Websuspect",
            CreatedById = actor.Id,
            IsActive = true
        };
        var location = new Location { Name = "Web Warehouse", Type = "Scene", IsActive = true };
        var crimeCase = new CrimeCase
        {
            CaseNumber = "IMG-WEB-001",
            Title = "Image integration case",
            CrimeTypeId = crimeType.Id,
            Status = CaseStatus.Open,
            Severity = CaseSeverity.Medium,
            IncidentDate = new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc),
            CreatedById = actor.Id
        };

        context.AddRange(person, location, crimeCase);
        await context.SaveChangesAsync();

        context.CasePersons.Add(new CasePerson
        {
            CrimeCaseId = crimeCase.Id,
            PersonId = person.Id,
            Role = PersonRole.Suspect
        });
        context.CaseLocations.Add(new CaseLocation
        {
            CrimeCaseId = crimeCase.Id,
            LocationId = location.Id,
            LocationRole = "Incident"
        });
        await context.SaveChangesAsync();

        return crimeCase.Id;
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

    private static byte[] PngBytes(string tag) =>
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            .Concat(Encoding.UTF8.GetBytes($"IHDR{tag}")).ToArray();

    /// <summary>Uploads an image through the real multipart pipeline and follows the redirect.</summary>
    private async Task<(string Html, Guid RecordId)> UploadAndOpenAsync(
        HttpClient client, string tag, byte[]? content = null, string fileName = "subject.png")
    {
        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/image-investigation"));

        var form = new MultipartFormDataContent
        {
            { new StringContent(_caseId.ToString()), "Upload.CrimeCaseId" },
            { new StringContent(token), "__RequestVerificationToken" }
        };
        var bytes = content ?? PngBytes(tag);
        var filePart = new ByteArrayContent(bytes);
        filePart.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(filePart, "Upload.File", fileName);

        var response = await client.PostAsync("/image-investigation/Analyze", form);
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            var body = await response.Content.ReadAsStringAsync();
            var shown = Regex.Matches(body, "field-validation-error[^>]*>([^<]+)")
                .Select(m => m.Groups[1].Value.Trim())
                .Where(message => message.Length > 0);
            Assert.Fail(
                $"Upload of '{fileName}' returned {response.StatusCode}: " +
                string.Join(" | ", shown));
        }

        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("/image-investigation?record=");

        var recordId = Guid.Parse(location.Split("record=").Last());
        return (await client.GetStringAsync(location), recordId);
    }

    private static Guid ExtractFirstMatchId(string html)
    {
        var match = Regex.Match(html, "/image-investigation/Match/(?<id>[0-9a-fA-F-]{36})");
        match.Success.Should().BeTrue("the result page should link to at least one candidate detail");
        return Guid.Parse(match.Groups["id"].Value);
    }

    [Fact]
    public async Task ImageInvestigation_AnonymousRedirectsToLoginEverywhere()
    {
        var client = AnonymousClient();

        var index = await client.GetAsync("/image-investigation");
        var match = await client.GetAsync($"/image-investigation/Match/{Guid.NewGuid()}");
        var image = await client.GetAsync($"/image-investigation/Image/{Guid.NewGuid()}");

        foreach (var response in new[] { index, match, image })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location!.ToString().Should().Contain("/Account/Login");
        }
    }

    [Fact]
    public async Task ImageInvestigation_SeniorOfficerIsForbidden()
    {
        var client = await SignInAsync("image.senior@netra.local", "Senior Officer");

        var index = await client.GetAsync("/image-investigation");
        var match = await client.GetAsync($"/image-investigation/Match/{Guid.NewGuid()}");

        index.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        match.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Investigator", "image.investigator@netra.local")]
    [InlineData("Police / Intelligence Analyst", "image.analyst@netra.local")]
    [InlineData("System Administrator", "image.admin@netra.local")]
    public async Task ImageInvestigation_ExplicitPolicyRolesAreAuthorised(string role, string email)
    {
        var client = await SignInAsync(email, role);

        var response = await client.GetAsync("/image-investigation");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Subject Input");
        html.Should().Contain("Potential Matches");
        html.Should().Contain("Investigative Lead");
        html.Should().Contain("No image analysed yet");
    }

    [Fact]
    public async Task ImageInvestigation_PageStaysHonestAndEmitsNoJavaScript()
    {
        var client = await SignInAsync("image.honest@netra.local", "Investigator");

        var html = await client.GetStringAsync("/image-investigation");
        var lower = html.ToLowerInvariant();

        lower.Should().NotContain("<script");
        lower.Should().NotContain("onclick");
        lower.Should().NotContain("javascript:");
        lower.Should().NotContain("identity confirmed");
        lower.Should().NotContain("person confirmed");
        lower.Should().NotContain("guilty");
        lower.Should().Contain("verification required");
        lower.Should().Contain("synthetic");
    }

    [Fact]
    public async Task Analyze_InvalidExtension_ShowsServerSideValidationAndStoresNothing()
    {
        var client = await SignInAsync("image.invalid@netra.local", "Investigator");

        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/image-investigation"));
        var form = new MultipartFormDataContent
        {
            { new StringContent(_caseId.ToString()), "Upload.CrimeCaseId" },
            { new StringContent(token), "__RequestVerificationToken" }
        };
        var filePart = new ByteArrayContent(Encoding.UTF8.GetBytes("not an image payload at all"));
        filePart.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(filePart, "Upload.File", "report.pdf");

        var response = await client.PostAsync("/image-investigation/Analyze", form);
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Only .jpg, .jpeg, .png, .webp images are accepted.");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        (await context.ImageRecords.AnyAsync(r => r.FileName == "report.pdf")).Should().BeFalse();
    }

    [Fact]
    public async Task Analyze_WithoutCaseSelection_ShowsFieldValidation()
    {
        var client = await SignInAsync("image.nocase@netra.local", "Investigator");

        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/image-investigation"));
        var form = new MultipartFormDataContent
        {
            { new StringContent(string.Empty), "Upload.CrimeCaseId" },
            { new StringContent(token), "__RequestVerificationToken" }
        };
        var filePart = new ByteArrayContent(PngBytes("nocase"));
        filePart.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(filePart, "Upload.File", "subject.png");

        var response = await client.PostAsync("/image-investigation/Analyze", form);
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Select the case this image belongs to.");
    }

    [Fact]
    public async Task Analyze_PostWithoutAntiForgeryToken_IsRejected()
    {
        var client = await SignInAsync("image.noforgery@netra.local", "Investigator");

        var form = new MultipartFormDataContent
        {
            { new StringContent(_caseId.ToString()), "Upload.CrimeCaseId" }
        };
        var filePart = new ByteArrayContent(PngBytes("noforgery"));
        filePart.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(filePart, "Upload.File", "subject.png");

        var response = await client.PostAsync("/image-investigation/Analyze", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Analyze_ValidUpload_ProducesDeterministicLeadCardsWithContext()
    {
        var client = await SignInAsync("image.flow@netra.local", "Police / Intelligence Analyst");

        // The stored NETRA image set grows across the shared fixture, so this upload
        // must list every candidate with a bounded synthetic score and lead wording.
        var (html, recordId) = await UploadAndOpenAsync(client, "flow-first");
        recordId.Should().NotBe(Guid.Empty);
        html.Should().Contain("Potential Matches");

        // A second, distinct upload compares against the first: the candidate card
        // must resolve to the suspect person recorded in NETRA's own case data.
        var (secondHtml, secondRecordId) = await UploadAndOpenAsync(client, "flow-second");
        secondHtml.Should().Contain("Dana Websuspect");
        secondHtml.Should().Contain("DEMO-CAND-");
        secondHtml.Should().Contain("Associated cases:");
        secondHtml.Should().Contain("Related locations:");
        secondHtml.Should().Contain("View Details");
        secondHtml.Should().Contain("candidate(s)");
        // The uploaded subject is previewed through the authorised endpoint only.
        secondHtml.Should().Contain($"/image-investigation/Image/{secondRecordId}");

        // Deterministic: reopening the same record renders the same candidate links.
        var reopened = await client.GetStringAsync($"/image-investigation?record={recordId}");
        MatchLinks(reopened).Should().Equal(MatchLinks(html));
    }

    private static List<string> MatchLinks(string html) =>
        Regex.Matches(html, "/image-investigation/Match/(?<id>[0-9a-fA-F-]{36})")
            .Select(m => m.Groups["id"].Value)
            .ToList();

    [Fact]
    public async Task MatchDetails_AuthorizedViewShowsContext_AndUnknownIsNotFound()
    {
        var client = await SignInAsync("image.detail@netra.local", "Investigator");

        var (html, _) = await UploadAndOpenAsync(client, "detail-source");
        var (secondHtml, _) = await UploadAndOpenAsync(client, "detail-target");
        var matchId = ExtractFirstMatchId(secondHtml);

        var response = await client.GetAsync($"/image-investigation/Match/{matchId}");
        var detail = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        detail.Should().Contain("Candidate Match Details");
        detail.Should().Contain("Comparison method");
        detail.Should().Contain("Deterministic demo provider (synthetic)");
        detail.Should().Contain("Associated Cases");
        detail.Should().Contain("Related Locations");
        detail.Should().Contain("Warehouse");
        detail.Should().Contain("IMG-WEB-001");
        detail.Should().Contain("Investigative Lead");
        detail.ToLowerInvariant().Should().NotContain("<script");

        (await client.GetAsync($"/image-investigation/Match/{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ImagePreview_ServesStoredBytesToAuthorisedUsersOnly()
    {
        var client = await SignInAsync("image.preview@netra.local", "Investigator");
        var bytes = PngBytes("preview-bytes");

        var (_, recordId) = await UploadAndOpenAsync(client, "preview", bytes);

        var response = await client.GetAsync($"/image-investigation/Image/{recordId}");
        var served = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        served.Should().Equal(bytes);

        (await AnonymousClient().GetAsync($"/image-investigation/Image/{recordId}"))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);

        (await client.GetAsync($"/image-investigation/Image/{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_And_Views_AreWrittenToTheCentralAuditTrail()
    {
        var client = await SignInAsync("image.audit@netra.local", "System Administrator");

        var (_, recordId) = await UploadAndOpenAsync(client, "audit-first");
        var (secondHtml, secondRecordId) = await UploadAndOpenAsync(client, "audit-second");
        var matchId = ExtractFirstMatchId(secondHtml);
        await client.GetAsync($"/image-investigation/Match/{matchId}");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();

        (await context.AuditLogs.AnyAsync(entry =>
            entry.EntityId == recordId && entry.Action == AuditActionNames.EvidenceUploaded))
            .Should().BeTrue();
        (await context.AuditLogs.AnyAsync(entry =>
            entry.EntityId == secondRecordId && entry.Action == AuditActionNames.ImageAnalysisRequested))
            .Should().BeTrue();
        (await context.AuditLogs.AnyAsync(entry =>
            entry.EntityId == secondRecordId && entry.Action == AuditActionNames.ImageResultsViewed))
            .Should().BeTrue();
        (await context.AuditLogs.AnyAsync(entry =>
            entry.EntityId == matchId && entry.Action == AuditActionNames.ImageMatchViewed))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("Investigator", "image.nav.investigator@netra.local", true)]
    [InlineData("Police / Intelligence Analyst", "image.nav.analyst@netra.local", true)]
    [InlineData("System Administrator", "image.nav.admin@netra.local", true)]
    [InlineData("Senior Officer", "image.nav.senior@netra.local", false)]
    public async Task Sidebar_LinkVisibilityFollowsThePolicyRoles(
        string role, string email, bool visible)
    {
        var client = await SignInAsync(email, role);

        var html = await client.GetStringAsync("/Dashboard");

        if (visible)
        {
            html.Should().Contain("href=\"/image-investigation\"");
        }
        else
        {
            html.Should().NotContain("href=\"/image-investigation\"");
        }
    }
}
