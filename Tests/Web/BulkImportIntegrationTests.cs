using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Application.Abstractions;
using NETRA.Domain.Enums;
using NETRA.Domain.Import;
using NETRA.Infrastructure.Data;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// Routing and authorisation coverage for the seven Bulk Data Import endpoints, driven
/// through the real MVC pipeline: anonymous redirects, role refusals, anti-forgery, the
/// upload/validate/preview/import/result sequence, the state machine between stages, job
/// ownership and the audit trail. Hosted with EF Core's in-memory provider by
/// <see cref="NetraWebFactory"/>; these are not live SQL Server tests, and the in-memory
/// provider does not support transactions, so atomicity is asserted as "nothing was
/// written", which is what the operator observes.
/// </summary>
/// <remarks>
/// Every case number is synthetic and unique to the test that uses it, because the class
/// shares one in-memory database.
/// </remarks>
public sealed class BulkImportIntegrationTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";

    private const string Header =
        "CaseNumber,Title,CrimeType,IncidentDate,Severity,Status,Description,District,PoliceStation,Address,Latitude,Longitude";

    private readonly NetraWebFactory _factory;

    public BulkImportIntegrationTests(NetraWebFactory factory)
    {
        _factory = factory;
        factory.SeedReferenceDataAsync().GetAwaiter().GetResult();
    }

    // ------------------------------------------------------------------------------ helpers

    private HttpClient AnonymousClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> SignInAsync(string email, params string[] roles)
    {
        await _factory.SeedUserAsync(email, Password, roles);

        var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/Account/Login"));

        var response = await client.PostAsync("/Account/Login", AuthenticationScreenTests.Form(
            ("UserName", email),
            ("Password", Password),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private async Task<T> WithContextAsync<T>(Func<NetraDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<NetraDbContext>());
    }

    /// <summary>A synthetic batch of valid rows, one per requested count.</summary>
    private static string Batch(string prefix, int rows) =>
        Header + "\n" + string.Concat(Enumerable.Range(1, rows).Select(row =>
            $"{prefix}-{row:00},Synthetic case {row:00},THF,2025-03-04,High,Open," +
            "Created by the web tests,CD,CHQ,,,\n"));

    private static MultipartFormDataContent CsvForm(
        string token,
        string name,
        byte[] csv,
        string fileName = "cases.csv",
        string contentType = "text/csv")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(name), "Name" },
            { new StringContent("Synthetic fixture uploaded by the test suite."), "Description" },
            { new StringContent(token), "__RequestVerificationToken" }
        };

        var file = new ByteArrayContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "File", fileName);

        return form;
    }

    /// <summary>Uploads through the real POST route and returns the job id from the redirect.</summary>
    private async Task<(Guid JobId, HttpClient Client)> UploadAsync(
        HttpClient client, string name, string csv, string fileName = "cases.csv")
    {
        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/BulkImport/Upload"));

        var response = await client.PostAsync(
            "/BulkImport/Upload",
            CsvForm(token, name, Encoding.UTF8.GetBytes(csv), fileName));

        response.StatusCode.Should().Be(
            HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());

        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("/BulkImport/Validate/");

        return (Guid.Parse(location.Split('/').Last()), client);
    }

    private async Task<int> CaseCountAsync(string prefix) =>
        await WithContextAsync(context => context.CrimeCases
            .CountAsync(c => c.CaseNumber.StartsWith(prefix, StringComparison.Ordinal)));

    private async Task<ImportJob> JobAsync(Guid jobId) =>
        await WithContextAsync(context => context.ImportJobs
            .AsNoTracking()
            .SingleAsync(job => job.Id == jobId));

    private static string TokenOf(string html) =>
        AuthenticationScreenTests.ExtractAntiForgeryToken(html);

    private static FormUrlEncodedContent TokenForm(string token) =>
        new([new KeyValuePair<string, string>("__RequestVerificationToken", token)]);

    // ------------------------------------------------------------------ routes: anonymous

    [Fact]
    public async Task EveryRoute_AnonymousRequestRedirectsToLogin()
    {
        var client = AnonymousClient();
        var id = Guid.NewGuid();

        var responses = new List<HttpResponseMessage>
        {
            await client.GetAsync("/BulkImport"),
            await client.GetAsync("/BulkImport/Upload"),
            await client.GetAsync($"/BulkImport/Validate/{id}"),
            await client.GetAsync($"/BulkImport/Preview/{id}"),
            await client.GetAsync($"/BulkImport/Result/{id}"),
            await client.PostAsync("/BulkImport/Upload", new FormUrlEncodedContent([])),
            await client.PostAsync($"/BulkImport/Import/{id}", new FormUrlEncodedContent([])),
            await client.PostAsync($"/BulkImport/Cancel/{id}", new FormUrlEncodedContent([]))
        };

        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location!.ToString().Should().Contain("/Account/Login");
        }
    }

    // --------------------------------------------------------------- routes: authorisation

    [Fact]
    public async Task BulkImport_IsForbiddenForRolesOutsideThePolicy()
    {
        var client = await SignInAsync("import.investigator@netra.local", "Investigator");

        var id = Guid.NewGuid();
        var index = await client.GetAsync("/BulkImport");
        var upload = await client.GetAsync("/BulkImport/Upload");
        var posted = await client.PostAsync($"/BulkImport/Cancel/{id}", new FormUrlEncodedContent([]));

        index.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        posted.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("System Administrator", "import.policy.admin@netra.local")]
    [InlineData("Senior Officer", "import.policy.senior@netra.local")]
    [InlineData("Police / Intelligence Analyst", "import.policy.analyst@netra.local")]
    public async Task BulkImport_IsAuthorisedForThePolicyRoles(string role, string email)
    {
        var client = await SignInAsync(email, role);

        var response = await client.GetAsync("/BulkImport");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Bulk Data Import");
        html.Should().Contain("Nothing is stored until you confirm it.");
        html.Should().Contain("Upload and validate");
        html.Should().Contain("CSV requirements");
    }

    [Theory]
    [InlineData("System Administrator", "import.nav.admin@netra.local", true)]
    [InlineData("Senior Officer", "import.nav.senior@netra.local", true)]
    [InlineData("Police / Intelligence Analyst", "import.nav.analyst@netra.local", true)]
    [InlineData("Investigator", "import.nav.investigator@netra.local", false)]
    public async Task Sidebar_LinkVisibilityFollowsThePolicyRoles(
        string role, string email, bool visible)
    {
        var client = await SignInAsync(email, role);

        var html = await client.GetStringAsync("/Dashboard");

        if (visible)
        {
            html.Should().Contain("href=\"/BulkImport\"");
        }
        else
        {
            html.Should().NotContain("href=\"/BulkImport\"");
        }
    }

    // --------------------------------------------------------------------- upload: §2, §21

    [Fact]
    public async Task UploadPage_ExplainsTheContractAndTheLimits()
    {
        var client = await SignInAsync("import.uploadpage@netra.local", "Senior Officer");

        var html = await client.GetStringAsync("/BulkImport/Upload");

        html.Should().Contain("Upload a case CSV");
        html.Should().Contain("Uploading creates no case records.");
        html.Should().Contain("What the file must contain");
        html.Should().Contain("CaseNumber");
        html.Should().Contain("5,000");
        html.Should().Contain("MB");
    }

    [Fact]
    public async Task Upload_PostWithoutAntiForgeryToken_IsRejectedAndStoresNothing()
    {
        var client = await SignInAsync("import.noforgery@netra.local", "System Administrator");

        var form = new MultipartFormDataContent
        {
            { new StringContent("Unforged batch"), "Name" }
        };
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(Batch("BXNF", 1)));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "File", "cases.csv");

        var response = await client.PostAsync("/BulkImport/Upload", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The class shares one in-memory database, so the assertion is about this upload.
        (await WithContextAsync(context => context.ImportJobs
            .AnyAsync(job => job.Name == "Unforged batch"))).Should().BeFalse();
    }

    [Fact]
    public async Task Upload_WithoutAFile_ShowsServerSideValidation()
    {
        var client = await SignInAsync("import.nofile@netra.local", "System Administrator");

        var token = TokenOf(await client.GetStringAsync("/BulkImport/Upload"));
        var form = new MultipartFormDataContent
        {
            { new StringContent("Empty batch"), "Name" },
            { new StringContent(token), "__RequestVerificationToken" }
        };

        var response = await client.PostAsync("/BulkImport/Upload", form);
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Choose a CSV file to upload.");
        (await WithContextAsync(context => context.ImportJobs
            .AnyAsync(job => job.Name == "Empty batch"))).Should().BeFalse();
    }

    [Fact]
    public async Task Upload_WithoutAName_ShowsServerSideValidation()
    {
        var client = await SignInAsync("import.noname@netra.local", "System Administrator");

        var token = TokenOf(await client.GetStringAsync("/BulkImport/Upload"));
        var form = CsvForm(token, string.Empty, Encoding.UTF8.GetBytes(Batch("BXNN", 1)));

        var response = await client.PostAsync("/BulkImport/Upload", form);
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Give this import a name so you can find it again.");
    }

    [Fact]
    public async Task Upload_RejectsANonCsvFile_AndRegistersNoJob()
    {
        var client = await SignInAsync("import.noncsv@netra.local", "System Administrator");

        var token = TokenOf(await client.GetStringAsync("/BulkImport/Upload"));
        var response = await client.PostAsync(
            "/BulkImport/Upload",
            CsvForm(token, "Not a csv", Encoding.UTF8.GetBytes("CaseNumber\nBX-1\n"), "cases.txt"));
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Only .csv files are accepted.");
        (await WithContextAsync(context => context.ImportJobs
            .AnyAsync(job => job.Name == "Not a csv"))).Should().BeFalse();
    }

    [Fact]
    public async Task Upload_RejectsAPathTraversalStyleFileName_BeforeStorage()
    {
        var client = await SignInAsync("import.traversal@netra.local", "System Administrator");

        var token = TokenOf(await client.GetStringAsync("/BulkImport/Upload"));
        var response = await client.PostAsync(
            "/BulkImport/Upload",
            CsvForm(
                token,
                "Traversal attempt",
                Encoding.UTF8.GetBytes(Batch("BXTV", 1)),
                "../../outside.csv"));
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("The file name is not valid.");
        (await WithContextAsync(context => context.ImportJobs
            .AnyAsync(job => job.Name == "Traversal attempt"))).Should().BeFalse();
    }

    [Fact]
    public async Task Upload_RegistersTheJobWithoutStagingRowsOrCases()
    {
        var client = await SignInAsync("import.stagelayout@netra.local", "System Administrator");

        var (jobId, _) = await UploadAsync(client, "Upload only", Batch("BXONLY", 2));

        var job = await JobAsync(jobId);
        job.Status.Should().Be(ImportStatus.Pending);
        job.TotalRecords.Should().Be(0);
        (await WithContextAsync(context => context.ImportRecords.AnyAsync(r => r.ImportJobId == jobId)))
            .Should().BeFalse("uploading must not insert records");
        (await CaseCountAsync("BXONLY")).Should().Be(0);
    }

    // ------------------------------------------------------- validation + preview: §9, §10

    [Fact]
    public async Task ValidationPage_ReportsTotalsAndOffersThePreviewForACleanFile()
    {
        var client = await SignInAsync("import.clean@netra.local", "System Administrator");

        var (jobId, _) = await UploadAsync(client, "Clean batch", Batch("BXCLN", 3));

        var response = await client.GetAsync($"/BulkImport/Validate/{jobId}");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Validation result");
        html.Should().Contain("Nothing has been imported yet.");
        html.Should().Contain("No problems found");
        html.Should().Contain($"/BulkImport/Preview/{jobId}");

        var job = await JobAsync(jobId);
        job.Status.Should().Be(ImportStatus.Validated);
        job.TotalRecords.Should().Be(3);
        (await CaseCountAsync("BXCLN")).Should().Be(0);
    }

    [Fact]
    public async Task ValidationPage_NamesEachRejectedRowByNumberAndColumn()
    {
        var client = await SignInAsync("import.rows@netra.local", "System Administrator");

        var csv = Header + "\n"
                 + "BXBAD-01,Accepted synthetic row,THF,2025-03-04,High,Open,Test,CD,CHQ,,,\n"
                 + "BXBAD-02,Unknown crime type,ZZZ,2025-03-04,High,Open,Test,CD,CHQ,,,\n"
                 + "BXBAD-03,Future incident date,THF,2099-01-01,High,Open,Test,CD,CHQ,,,\n";

        var (jobId, _) = await UploadAsync(client, "Rejected rows", csv);

        var html = await client.GetStringAsync($"/BulkImport/Validate/{jobId}");

        html.Should().Contain("Problems found");

        // Values are quoted in the message, and the HTML encoder escapes apostrophes, so the
        // assertions below name the text without the quotes around the supplied value.
        html.Should().Contain("No active crime type matches");
        html.Should().Contain("on row 2.");
        html.Should().Contain("The incident date");
        html.Should().Contain("on row 3 is in the future.");
        html.Should().Contain("Rows held back");
        html.Should().Contain("Row contents are not repeated here.");
        html.Should().Contain("Preview 1 accepted row(s)");

        // One accepted row of three: the register still holds nothing.
        (await CaseCountAsync("BXBAD")).Should().Be(0);
    }

    [Fact]
    public async Task PreviewPage_ShowsAcceptedRowsAndWritesNothing()
    {
        var client = await SignInAsync("import.preview@netra.local", "System Administrator");

        var (jobId, _) = await UploadAsync(client, "Preview batch", Batch("BXPVW", 2));
        await client.GetStringAsync($"/BulkImport/Validate/{jobId}");

        var response = await client.GetAsync($"/BulkImport/Preview/{jobId}");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Preview before importing");
        html.Should().Contain("This is a preview, not a record.");
        html.Should().Contain("BXPVW-01");
        html.Should().Contain("Import 2 case(s)");

        (await CaseCountAsync("BXPVW")).Should().Be(0);
        (await WithContextAsync(context => context.ImportJobs.AnyAsync(job => job.Id == jobId
            && job.Status == ImportStatus.Validated))).Should().BeTrue();
    }

    [Fact]
    public async Task Preview_WithoutValidation_RedirectsToTheIndexWithAReason()
    {
        var client = await SignInAsync("import.ordering@netra.local", "System Administrator");

        var (jobId, _) = await UploadAsync(client, "Unvalidated batch", Batch("BXORD", 1));

        var skipped = await client.GetAsync($"/BulkImport/Preview/{jobId}");

        skipped.StatusCode.Should().Be(HttpStatusCode.Redirect);
        skipped.Headers.Location!.ToString().Should().Be("/BulkImport");

        var index = await client.GetStringAsync(skipped.Headers.Location!.ToString());
        index.Should().Contain("Validate the file before previewing it.");

        // TempData is single-use: the message must not appear again.
        (await client.GetStringAsync("/BulkImport")).Should().NotContain("Validate the file before");
    }

    [Fact]
    public async Task UnknownJob_RedirectsToTheIndexAndIsNotFound()
    {
        var client = await SignInAsync("import.missing@netra.local", "System Administrator");

        var missing = Guid.NewGuid();
        var result = await client.GetAsync($"/BulkImport/Result/{missing}");

        result.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var index = await client.GetStringAsync(result.Headers.Location!.ToString());
        index.Should().Contain("That import job no longer exists.");
    }

    [Fact]
    public async Task Import_IsNotReachableByGet()
    {
        var client = await SignInAsync("import.getimport@netra.local", "System Administrator");

        var (jobId, _) = await UploadAsync(client, "Get attempt", Batch("BXGET", 1));
        await client.GetStringAsync($"/BulkImport/Validate/{jobId}");

        var response = await client.GetAsync($"/BulkImport/Import/{jobId}");

        // The route exists for POST only, so a GET cannot be turned into a state change.
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await CaseCountAsync("BXGET")).Should().Be(0);
    }

    // ------------------------------------------------------------------- import: §10, §11

    [Fact]
    public async Task Workflow_ConfirmedImportCreatesCasesAndTheResultPageReportsThem()
    {
        var client = await SignInAsync("import.workflow@netra.local", "System Administrator");

        var prefix = "BXWF1";
        var (jobId, _) = await UploadAsync(client, "October district register", Batch(prefix, 3));

        var validateUrl = $"/BulkImport/Validate/{jobId}";
        await client.GetStringAsync(validateUrl);

        var previewHtml = await client.GetStringAsync($"/BulkImport/Preview/{jobId}");
        (await CaseCountAsync(prefix)).Should().Be(0);

        var imported = await client.PostAsync(
            $"/BulkImport/Import/{jobId}", TokenForm(TokenOf(previewHtml)));

        imported.StatusCode.Should().Be(HttpStatusCode.Redirect);
        imported.Headers.Location!.ToString().Should().Be($"/BulkImport/Result/{jobId}");

        var response = await client.GetAsync(imported.Headers.Location!.ToString());
        var result = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Should().Contain("Import result");
        result.Should().Contain("Cases created");
        result.Should().Contain($"{prefix}-02");
        result.Should().Contain("/Cases/Details/");
        result.Should().Contain("href=\"/BulkImport\"");

        (await CaseCountAsync(prefix)).Should().Be(3);

        var job = await JobAsync(jobId);
        job.Status.Should().Be(ImportStatus.Completed);
        job.SuccessfulRecords.Should().Be(3);
        job.CompletedAt.Should().NotBeNull();

        // A confirmed second submission changes nothing.
        var again = await client.PostAsync(
            $"/BulkImport/Import/{jobId}", TokenForm(TokenOf(result)));
        again.StatusCode.Should().Be(HttpStatusCode.Redirect);
        again.Headers.Location!.ToString().Should().Be($"/BulkImport/Preview/{jobId}");
        (await CaseCountAsync(prefix)).Should().Be(3);
    }

    [Fact]
    public async Task Workflow_DuplicateCaseNumbersAreSkippedAndNeverOverwritten()
    {
        var client = await SignInAsync("import.duplicate@netra.local", "Senior Officer");

        var prefix = "BXDUP";
        var csv = Header + "\n"
                 + $"{prefix}-01,First synthetic row,THF,2025-03-04,Medium,Open,Test,CD,CHQ,,,\n"
                 + $"{prefix}-01,Repeated synthetic row,THF,2025-03-05,Medium,Open,Test,CD,CHQ,,,\n"
                 + $"{prefix}-02,Unique synthetic row,ASL,2025-03-06,High,Open,Test,ND,,,,\n";

        var (jobId, _) = await UploadAsync(client, "Duplicate batch", csv);

        var validation = await client.GetStringAsync($"/BulkImport/Validate/{jobId}");
        validation.Should().Contain("Skipped case numbers");
        validation.Should().Contain("Preview 2 accepted row(s)");

        var previewHtml = await client.GetStringAsync($"/BulkImport/Preview/{jobId}");
        previewHtml.Should().Contain("Rows this import will not create");
        previewHtml.Should().Contain("Duplicate case numbers are skipped, never overwritten");

        await client.PostAsync($"/BulkImport/Import/{jobId}", TokenForm(TokenOf(previewHtml)));

        // Two distinct case numbers were accepted; the repeat was skipped, not updated.
        (await CaseCountAsync(prefix)).Should().Be(2);
        var titles = await WithContextAsync(context => context.CrimeCases
            .Where(c => c.CaseNumber == $"{prefix}-01")
            .Select(c => c.Title)
            .ToListAsync());
        titles.Should().Equal("First synthetic row");

        var job = await JobAsync(jobId);
        job.Status.Should().Be(ImportStatus.CompletedWithErrors);
        job.SuccessfulRecords.Should().Be(2);
        job.FailedRecords.Should().Be(1);
    }

    [Fact]
    public async Task Import_WithoutAntiForgeryToken_IsRejectedAndWritesNothing()
    {
        var client = await SignInAsync("import.token@netra.local", "System Administrator");

        var prefix = "BXTKN";
        var (jobId, _) = await UploadAsync(client, "Token batch", Batch(prefix, 1));
        await client.GetStringAsync($"/BulkImport/Validate/{jobId}");

        var response = await client.PostAsync(
            $"/BulkImport/Import/{jobId}", new FormUrlEncodedContent([]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CaseCountAsync(prefix)).Should().Be(0);
        (await JobAsync(jobId)).Status.Should().Be(ImportStatus.Validated);
    }

    [Fact]
    public async Task Import_WithNothingAccepted_RefusesAndCreatesNoCases()
    {
        var client = await SignInAsync("import.nothingaccepted@netra.local", "System Administrator");

        var csv = Header + "\n"
                 + "BXNAC-01,Unknown crime type,ZZZ,2025-03-04,High,Open,Test,CD,CHQ,,,\n";

        var (jobId, _) = await UploadAsync(client, "All rejected", csv);

        var validation = await client.GetStringAsync($"/BulkImport/Validate/{jobId}");
        validation.Should().Contain("No row in this file passed validation");
        validation.Should().NotContain("Preview 0 accepted");

        var preview = await client.GetAsync($"/BulkImport/Preview/{jobId}");
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewHtml = await preview.Content.ReadAsStringAsync();
        previewHtml.Should().Contain("Nothing to import");
        previewHtml.Should().NotContain("Import 0 case(s)");

        var imported = await client.PostAsync(
            $"/BulkImport/Import/{jobId}", TokenForm(TokenOf(previewHtml)));
        imported.StatusCode.Should().Be(HttpStatusCode.Redirect);
        imported.Headers.Location!.ToString().Should().Be($"/BulkImport/Preview/{jobId}");

        (await CaseCountAsync("BXNAC")).Should().Be(0);
        (await JobAsync(jobId)).Status.Should().Be(ImportStatus.Validated);
    }

    [Fact]
    public async Task Cancel_EndsTheJobWithoutCreatingCases()
    {
        var client = await SignInAsync("import.cancel@netra.local", "Senior Officer");

        var prefix = "BXCXL";
        var (jobId, _) = await UploadAsync(client, "Cancelled batch", Batch(prefix, 2));
        var validation = await client.GetStringAsync($"/BulkImport/Validate/{jobId}");

        var cancelled = await client.PostAsync(
            $"/BulkImport/Cancel/{jobId}", TokenForm(TokenOf(validation)));

        cancelled.StatusCode.Should().Be(HttpStatusCode.Redirect);
        cancelled.Headers.Location!.ToString().Should().Be("/BulkImport");

        (await CaseCountAsync(prefix)).Should().Be(0);
        (await JobAsync(jobId)).Status.Should().Be(ImportStatus.Cancelled);

        // A cancelled import cannot be revived by pressing Import.
        var imported = await client.PostAsync(
            $"/BulkImport/Import/{jobId}", TokenForm(TokenOf(validation)));
        imported.Headers.Location!.ToString().Should().Be($"/BulkImport/Preview/{jobId}");
        (await CaseCountAsync(prefix)).Should().Be(0);
    }

    // -------------------------------------------------------------- ownership: §18, §19, §21

    [Fact]
    public async Task AnotherOperatorsJob_IsInvisibleAndUntouchableThroughTheRoutes()
    {
        var owner = await SignInAsync("import.owner@netra.local", "System Administrator");
        var stranger = await SignInAsync("import.stranger@netra.local", "Police / Intelligence Analyst");

        var prefix = "BXOWN";
        var (jobId, _) = await UploadAsync(owner, "Private batch", Batch(prefix, 1));

        // The stranger may hold the same policy, but the job is not theirs.
        var validate = await stranger.GetAsync($"/BulkImport/Validate/{jobId}");
        validate.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var preview = await stranger.GetAsync($"/BulkImport/Preview/{jobId}");
        preview.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var theirIndex = await stranger.GetStringAsync("/BulkImport");
        theirIndex.Should().NotContain("Private batch");
        theirIndex.Should().Contain("My recent imports");

        var mine = await owner.GetStringAsync("/BulkImport");
        mine.Should().Contain("Private batch");

        // The stranger submits with a token from their own session: the refusal is about
        // ownership, not about anti-forgery.
        var imported = await stranger.PostAsync(
            $"/BulkImport/Import/{jobId}", TokenForm(TokenOf(theirIndex)));
        imported.StatusCode.Should().Be(HttpStatusCode.Redirect);
        imported.Headers.Location!.ToString().Should().Be($"/BulkImport/Preview/{jobId}");
        (await CaseCountAsync(prefix)).Should().Be(0);

        (await WithContextAsync(context => context.AuditLogs.CountAsync(entry =>
            entry.EntityId == jobId && entry.Action == AuditActionNames.ImportAccessDenied)))
            .Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------------- audit: §20

    [Fact]
    public async Task Workflow_AuditsEveryStageAndNeverStoresFileContents()
    {
        var client = await SignInAsync("import.audit@netra.local", "System Administrator");

        var prefix = "BXAUD";
        var (jobId, _) = await UploadAsync(client, "Audited batch", Batch(prefix, 2));

        await client.GetStringAsync($"/BulkImport/Validate/{jobId}");
        var previewHtml = await client.GetStringAsync($"/BulkImport/Preview/{jobId}");
        var imported = await client.PostAsync(
            $"/BulkImport/Import/{jobId}", TokenForm(TokenOf(previewHtml)));
        await client.GetStringAsync(imported.Headers.Location!.ToString());

        var entries = await WithContextAsync(context => context.AuditLogs
            .Where(entry => entry.EntityId == jobId)
            .Select(entry => entry.Action)
            .ToListAsync());

        entries.Should().Contain([
            AuditActionNames.ImportUploaded,
            AuditActionNames.ImportValidated,
            AuditActionNames.ImportPreviewed,
            AuditActionNames.ImportStarted,
            AuditActionNames.ImportCompleted
        ]);

        // Each created case is audited in the register's own trail. The class shares one
        // database, so the assertion is scoped to the cases this test imported.
        var createdCaseIds = await WithContextAsync(context => context.CrimeCases
            .Where(c => c.CaseNumber.StartsWith(prefix, StringComparison.Ordinal))
            .Select(c => c.Id)
            .ToListAsync());

        createdCaseIds.Should().HaveCount(2);

        var auditedCreations = await WithContextAsync(context => context.AuditLogs
            .Where(entry => entry.EntityType == AuditEntityTypeNames.CrimeCase
                            && entry.Action == AuditActionNames.Created
                            && createdCaseIds.Contains(entry.EntityId))
            .Select(entry => entry.EntityId)
            .ToListAsync());

        auditedCreations.Should().BeEquivalentTo(createdCaseIds);

        var payloads = await WithContextAsync(context => context.AuditLogs
            .Where(entry => entry.EntityId == jobId)
            .Select(entry => entry.NewValues)
            .ToListAsync());

        string.Join(" ", payloads.Where(value => value is not null))
            .Should().NotContain("Created by the web tests",
                "audit lines describe the run, they never carry row content");
        string.Join(" ", payloads.Where(value => value is not null))
            .Should().NotContain(prefix);
    }

    // --------------------------------------------------------------------- no JavaScript

    [Fact]
    public async Task WholeWorkflow_EmitsNoJavaScript()
    {
        var client = await SignInAsync("import.nojs@netra.local", "System Administrator");

        var (jobId, _) = await UploadAsync(client, "No script batch", Batch("BXNJS", 2));
        var validateHtml = await client.GetStringAsync($"/BulkImport/Validate/{jobId}");
        var previewHtml = await client.GetStringAsync($"/BulkImport/Preview/{jobId}");
        var imported = await client.PostAsync(
            $"/BulkImport/Import/{jobId}", TokenForm(TokenOf(previewHtml)));

        var pages = new List<string>
        {
            await client.GetStringAsync("/BulkImport"),
            await client.GetStringAsync("/BulkImport/Upload"),
            validateHtml,
            previewHtml,
            await client.GetStringAsync(imported.Headers.Location!.ToString())
        };

        foreach (var page in pages)
        {
            var lower = page.ToLowerInvariant();
            lower.Should().NotContain("<script");
            lower.Should().NotContain("onclick");
            lower.Should().NotContain("onchange");
            lower.Should().NotContain("javascript:");
            lower.Should().NotContain("fetch(");
            lower.Should().NotContain("xmlhttprequest");
        }
    }

    [Fact]
    public async Task RecentImports_ListOnlyThisOperatorsJobs()
    {
        var first = await SignInAsync("import.listing.a@netra.local", "System Administrator");
        var second = await SignInAsync("import.listing.b@netra.local", "System Administrator");

        await UploadAsync(first, "Operator A batch", Batch("BXOPA", 1));
        await UploadAsync(second, "Operator B batch", Batch("BXOPB", 1));

        var firstHtml = await first.GetStringAsync("/BulkImport");
        var secondHtml = await second.GetStringAsync("/BulkImport");

        firstHtml.Should().Contain("Operator A batch");
        firstHtml.Should().NotContain("Operator B batch");
        secondHtml.Should().Contain("Operator B batch");
        secondHtml.Should().NotContain("Operator A batch");
    }
}
