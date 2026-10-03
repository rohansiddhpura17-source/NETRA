using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Domain.Audit;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Web.Authorization;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// End-to-end checks for the Audit Log reader. Nothing here mocks the trail: rows are
/// written through <see cref="NetraDbContext"/> exactly as the services write them, then read
/// back through the real controller, query service and Razor views.
/// </summary>
public class AuditLogsIntegrationTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";

    private static readonly DateTime Base = new(2026, 5, 10, 8, 0, 0, DateTimeKind.Utc);

    private readonly NetraWebFactory _factory;

    public AuditLogsIntegrationTests(NetraWebFactory factory)
    {
        _factory = factory;
    }

    // ---------------------------------------------------------------- helpers

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

    private async Task<ApplicationUser> SeedActorAsync(
        string userName, string firstName, string lastName)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = userName,
            NormalizedEmail = userName.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            FirstName = firstName,
            LastName = lastName,
            IsActive = true
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private async Task<Guid> AddTrailEntryAsync(
        string entityType,
        Guid entityId,
        string action,
        Guid actorId,
        DateTime timestamp,
        string? propertyName = null,
        string? oldValue = null,
        string? newValue = null,
        bool isSystemAction = false)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();

        var entry = new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            UserId = actorId,
            Timestamp = timestamp,
            PropertyName = propertyName,
            OldValues = oldValue,
            NewValues = newValue,
            IsSystemAction = isSystemAction,
            IpAddress = "203.0.113.7",
            UserAgent = "NETRA integration test runner",
            CorrelationId = "corr-integration-0001"
        };

        context.AuditLogs.Add(entry);
        await context.SaveChangesAsync();
        return entry.Id;
    }

    private Task<int> TrailCountAsync() => InDbAsync(db => db.AuditLogs.CountAsync());

    private async Task<T> InDbAsync<T>(Func<NetraDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<NetraDbContext>());
    }

    private async Task<string> EntryIdForSubjectAsync(string action, Guid entityId)
    {
        // Keyed on the subject, not on entity type plus action: other tests in this class
        // write the same pair, and the trail is shared for the whole collection.
        var id = await InDbAsync(db => db.AuditLogs
            .Where(a => a.Action == action && a.EntityId == entityId)
            .Select(a => a.Id)
            .FirstAsync());

        return id.ToString();
    }

    private static async Task<string> TokenAsync(HttpClient client, string path) =>
        AuthenticationScreenTests.ExtractAntiForgeryToken(await client.GetStringAsync(path));

    // ---------------------------------------------------------------- access control

    [Theory]
    [InlineData("/AuditLogs")]
    [InlineData("/AuditLogs/Details/11111111-1111-1111-1111-111111111111")]
    public async Task AnonymousGet_RedirectsToSignIn(string path)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
    }

    [Theory]
    [InlineData(NetraRoles.SystemAdministrator, HttpStatusCode.OK)]
    [InlineData(NetraRoles.SeniorOfficer, HttpStatusCode.OK)]
    [InlineData(NetraRoles.PoliceIntelligenceAnalyst, HttpStatusCode.OK)]
    [InlineData(NetraRoles.Investigator, HttpStatusCode.Forbidden)]
    public async Task ReadPolicy_DecidesWhoSeesTheTrail(string role, HttpStatusCode expected)
    {
        // Identity usernames allow letters and digits only, so the role is slugged.
        var client = await SignInAsync(
            $"reader.{role.Replace(" ", "-").Replace("/", string.Empty)}@netra.local", role);

        var response = await client.GetAsync("/AuditLogs");

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Details_ForbiddenRole_CannotReadASingleEntry()
    {
        var actor = await SeedActorAsync("guard.actor@netra.local", "Guard", "Actor");
        var id = await AddTrailEntryAsync("CrimeCase", Guid.NewGuid(), "Created", actor.Id, Base);

        var client = await SignInAsync("guard.investigator@netra.local", NetraRoles.Investigator);

        var response = await client.GetAsync($"/AuditLogs/Details/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Trail_IsReadOnly_NoMutatingRouteAndBrowsingWritesNothing()
    {
        var client = await SignInAsync("readonly.admin@netra.local", NetraRoles.SystemAdministrator);
        var before = await TrailCountAsync();

        var post = await client.PostAsync(
            "/AuditLogs", AuthenticationScreenTests.Form(("Action", "Created")));

        // The module exposes no write verb at all.
        post.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

        await client.GetStringAsync("/AuditLogs");
        await client.GetStringAsync("/AuditLogs?entityType=CrimeCase");

        var after = await TrailCountAsync();
        after.Should().Be(before);
    }

    // ---------------------------------------------------------------- the list

    [Fact]
    public async Task Index_RendersHeaderAndOnlyFiltersForRealColumns()
    {
        var client = await SignInAsync("shell.reader@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync("/AuditLogs");

        html.Should().Contain("Audit Logs");
        html.Should().Contain("The append-only record of what changed in NETRA");
        html.Should().Contain("Trail");
        html.Should().Contain("name=\"actorUserId\"");
        html.Should().Contain("name=\"entityType\"");
        html.Should().Contain("name=\"action\"");
        html.Should().Contain("name=\"dateFrom\"");
        html.Should().Contain("name=\"dateTo\"");
        html.Should().Contain("Any account");
        html.Should().Contain("All record types");
        html.Should().Contain("All actions");
        html.Should().Contain("Apply Filters");

        // The trail has no severity or workflow state, so neither is invented as a filter.
        html.Should().NotContain("name=\"severity\"");
        html.Should().NotContain("name=\"status\"");
    }

    [Fact]
    public async Task Index_WhenNothingMatches_ItSaysSoAndOffersToClear()
    {
        var client = await SignInAsync("nomatch.reader@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync("/AuditLogs?action=NoSuchActionAtAll");

        html.Should().Contain("No audit entries match the selected filters");
        html.Should().Contain("Clear filters");
    }

    [Fact]
    public async Task Index_RendersNewestFirstWithActorActionRecordAndDetailLink()
    {
        var actor = await SeedActorAsync("listing.actor@netra.local", "Listena", "Actor");
        var caseId = Guid.NewGuid();

        await AddTrailEntryAsync("CrimeCase", caseId, "Created", actor.Id, Base,
            propertyName: "Status", newValue: "\"Registered\"");
        var newest = await AddTrailEntryAsync("CrimeCase", caseId, "Edited", actor.Id,
            Base.AddHours(4), propertyName: "CaseType", oldValue: "\"Theft\"", newValue: "\"Assault\"");

        var client = await SignInAsync("listing.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync("/AuditLogs?entityType=CrimeCase");

        html.Should().Contain("<th scope=\"col\">When</th>");
        html.Should().Contain("<th scope=\"col\">Actor</th>");
        html.Should().Contain("<th scope=\"col\">Action</th>");
        html.Should().Contain("<th scope=\"col\">Record</th>");
        html.Should().Contain("<th scope=\"col\">Summary</th>");
        html.Should().Contain("Listena Actor");
        html.Should().Contain("listing.actor@netra.local");
        html.Should().Contain("/AuditLogs/Details/" + newest);

        // Ordered newest first: the 12:00 edit appears above the 08:00 creation.
        var edited = html.IndexOf("10 May 2026 12:00:00", StringComparison.Ordinal);
        var created = html.IndexOf("10 May 2026 08:00:00", StringComparison.Ordinal);
        edited.Should().BeGreaterThan(-1);
        created.Should().BeGreaterThan(-1);
        edited.Should().BeLessThan(created);

        // The summary is composed from the recorded field and values, as plain text.
        html.Should().Contain("CaseType:");
        html.Should().Contain("&quot;Assault&quot;");
    }

    [Fact]
    public async Task Index_MarksEntriesThatOriginateFromTheSystem()
    {
        var actor = await SeedActorAsync("system.actor@netra.local", "System", "Watcher");
        await AddTrailEntryAsync("Anomaly", Guid.NewGuid(), "AlertGenerated", actor.Id, Base,
            newValue: "Threshold crossed by the detector", isSystemAction: true);

        var client = await SignInAsync("system.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync("/AuditLogs?entityType=Anomaly");

        html.Should().Contain("<span class=\"netra-badge netra-badge--info\">System</span>");
        html.Should().Contain("Threshold crossed by the detector");
        html.Should().Contain("Alert Generated");
    }

    [Fact]
    public async Task Index_WhenAnActorIsGone_TheEntryIsStillShown()
    {
        await AddTrailEntryAsync("TimelineEvent", Guid.NewGuid(), "TimelineEventCreated",
            Guid.NewGuid(), Base, newValue: "Entry left behind by a removed account");

        var client = await SignInAsync("orphan.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync("/AuditLogs?entityType=TimelineEvent");

        html.Should().Contain("Deleted account");
        html.Should().Contain("Entry left behind by a removed account");
    }

    // ---------------------------------------------------------------- server-side filters

    [Fact]
    public async Task ActorFilter_ReturnsOnlyThatAccountsEntries()
    {
        var first = await SeedActorAsync("filter.one@netra.local", "Fiona", "First");
        var second = await SeedActorAsync("filter.two@netra.local", "Henry", "Second");
        var subject = Guid.NewGuid();

        await AddTrailEntryAsync("Evidence", subject, "EvidenceUploaded", first.Id, Base,
            newValue: "Fionas uploaded file");
        await AddTrailEntryAsync("Evidence", subject, "EvidenceUploaded", second.Id,
            Base.AddHours(1), newValue: "Henrys uploaded file");

        var client = await SignInAsync("filter.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync($"/AuditLogs?actorUserId={first.Id}");

        html.Should().Contain("Fionas uploaded file");
        html.Should().NotContain("Henrys uploaded file");
        html.Should().Contain("Fiona First");
        html.Should().Contain("Clear filters");
    }

    [Theory]
    [InlineData("NoteRecord", "Noted entry", "Reported entry")]
    [InlineData("ReportRecord", "Reported entry", "Noted entry")]
    public async Task EntityTypeFilter_IsAnExactMatchOnTheStoredColumn(
        string entityType, string expected, string unexpected)
    {
        var actor = await SeedActorAsync("typed.actor@netra.local", "Typa", "Actor");
        await AddTrailEntryAsync("NoteRecord", Guid.NewGuid(), "NoteCreated", actor.Id, Base,
            newValue: "Noted entry");
        await AddTrailEntryAsync("ReportRecord", Guid.NewGuid(), "ReportGenerated", actor.Id,
            Base, newValue: "Reported entry");

        var client = await SignInAsync($"typed.admin.{entityType}@netra.local",
            NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync($"/AuditLogs?entityType={entityType}");

        html.Should().Contain(expected);
        html.Should().NotContain(unexpected);
    }

    [Fact]
    public async Task ActionFilter_UsesTheStoredActionNameAndLabelsItForReading()
    {
        var actor = await SeedActorAsync("action.actor@netra.local", "Acta", "Long");
        await AddTrailEntryAsync("Notification", Guid.NewGuid(), "NotificationMarkedRead",
            actor.Id, Base, newValue: "Marked read by hand");
        await AddTrailEntryAsync("Notification", Guid.NewGuid(), "NotificationCreated", actor.Id,
            Base.AddHours(1), newValue: "Raised by the alert engine");

        var client = await SignInAsync("action.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync("/AuditLogs?action=NotificationMarkedRead");

        html.Should().Contain("Marked read by hand");
        html.Should().NotContain("Raised by the alert engine");
        html.Should().Contain("Notification Marked Read");

        var nothing = await client.GetStringAsync("/AuditLogs?action=NoSuchAction");
        nothing.Should().Contain("No audit entries match the selected filters");
    }

    [Fact]
    public async Task DateRangeFilter_IncludesTheWholeFinalDay()
    {
        var actor = await SeedActorAsync("range.actor@netra.local", "Rana", "Ge");

        await AddTrailEntryAsync("ImportJob", Guid.NewGuid(), "ImportStarted", actor.Id,
            new DateTime(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc),
            newValue: "Last second of April");
        await AddTrailEntryAsync("ImportJob", Guid.NewGuid(), "ImportStarted", actor.Id,
            new DateTime(2026, 5, 1, 0, 0, 1, DateTimeKind.Utc),
            newValue: "First second of May");
        await AddTrailEntryAsync("ImportJob", Guid.NewGuid(), "ImportStarted", actor.Id,
            new DateTime(2026, 5, 14, 23, 59, 59, DateTimeKind.Utc),
            newValue: "Last second of the range");
        await AddTrailEntryAsync("ImportJob", Guid.NewGuid(), "ImportStarted", actor.Id,
            new DateTime(2026, 5, 15, 0, 0, 1, DateTimeKind.Utc),
            newValue: "Just past the range");

        var client = await SignInAsync("range.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync(
            "/AuditLogs?entityType=ImportJob&dateFrom=2026-05-01&dateTo=2026-05-14");

        html.Should().Contain("First second of May");
        html.Should().Contain("Last second of the range");
        html.Should().NotContain("Last second of April");
        html.Should().NotContain("Just past the range");
        html.Should().Contain("value=\"2026-05-01\"");
        html.Should().Contain("value=\"2026-05-14\"");
    }

    [Fact]
    public async Task Filters_CombineIntoOneServerSideQuery()
    {
        var mine = await SeedActorAsync("combo.mine@netra.local", "Coba", "Mine");
        var other = await SeedActorAsync("combo.other@netra.local", "Coba", "Other");
        var subject = Guid.NewGuid();

        await AddTrailEntryAsync("CasePerson", subject, "PersonAssociated", mine.Id, Base,
            newValue: "Wanted combination");
        await AddTrailEntryAsync("CasePerson", subject, "PersonDisassociated", mine.Id, Base,
            newValue: "Wrong action");
        await AddTrailEntryAsync("CasePerson", subject, "PersonAssociated", other.Id, Base,
            newValue: "Wrong actor");
        await AddTrailEntryAsync("Evidence", subject, "PersonAssociated", mine.Id, Base,
            newValue: "Wrong record type");

        var client = await SignInAsync("combo.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync(
            $"/AuditLogs?actorUserId={mine.Id}&entityType=CasePerson&action=PersonAssociated");

        html.Should().Contain("Wanted combination");
        html.Should().NotContain("Wrong action");
        html.Should().NotContain("Wrong actor");
        html.Should().NotContain("Wrong record type");
        html.Should().Contain("1 entry");
    }

    [Fact]
    public async Task Pagination_WalksTheTrailAndKeepsTheFiltersInEveryLink()
    {
        var actor = await SeedActorAsync("paging.actor@netra.local", "Paga", "Long");
        var subject = Guid.NewGuid();

        foreach (var number in Enumerable.Range(1, 30))
        {
            await AddTrailEntryAsync("AssistantSession", subject, "AssistantQueryExecuted",
                actor.Id, Base.AddMinutes(number), newValue: $"Query number {number:D2}");
        }

        var client = await SignInAsync("paging.admin@netra.local", NetraRoles.SystemAdministrator);

        var first = await client.GetStringAsync("/AuditLogs?entityType=AssistantSession");
        first.Should().Contain("30 entries");
        first.Should().Contain("Showing 1&ndash;25 of 30");
        first.Should().Contain("Query number 30");
        first.Should().NotContain("Query number 05");
        first.Should().Contain("entityType=AssistantSession");
        first.Should().Contain("page=2");

        var second = await client.GetStringAsync("/AuditLogs?entityType=AssistantSession&page=2");
        second.Should().Contain("Showing 26&ndash;30 of 30");
        second.Should().Contain("Query number 05");
        second.Should().NotContain("Query number 30");
        second.Should().Contain("entityType=AssistantSession");

        // A page below 1 is lifted to the first page rather than honoured as written.
        var lifted = await client.GetStringAsync("/AuditLogs?entityType=AssistantSession&page=0");
        lifted.Should().Contain("Showing 1&ndash;25 of 30");
    }

    // ---------------------------------------------------------------- the detail page

    [Fact]
    public async Task Details_RendersTheWholeRecordIncludingRequestContext()
    {
        var actor = await SeedActorAsync("detail.actor@netra.local", "Deta", "Ailed");
        var subject = Guid.NewGuid();
        var entryId = await AddTrailEntryAsync(
            "CrimeCase", subject, "InvestigatorAssigned", actor.Id, Base,
            propertyName: "InvestigatorId",
            oldValue: "\"none\"",
            newValue: "\"officer-seven\"");

        var client = await SignInAsync("detail.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync($"/AuditLogs/Details/{entryId}");

        html.Should().Contain("Investigator Assigned");
        html.Should().Contain("Crime Case");
        html.Should().Contain("Deta Ailed");
        html.Should().Contain("detail.actor@netra.local");
        html.Should().Contain(actor.Id.ToString());
        html.Should().Contain(subject.ToString());
        html.Should().Contain(entryId.ToString());
        html.Should().Contain("10 May 2026 08:00:00 UTC");
        html.Should().Contain("InvestigatorId");
        html.Should().Contain("&quot;none&quot;");
        html.Should().Contain("&quot;officer-seven&quot;");
        html.Should().Contain("203.0.113.7");
        html.Should().Contain("NETRA integration test runner");
        html.Should().Contain("corr-integration-0001");
        html.Should().Contain("Session id");
        html.Should().Contain("User action");
        html.Should().Contain("Back to the trail");

        // A case record has no account to open, so that link is not offered.
        html.Should().NotContain("Open this account");
    }

    [Fact]
    public async Task Details_NeverInterpretsStoredPayloadsAsMarkup()
    {
        var actor = await SeedActorAsync("xss.actor@netra.local", "Xss", "Actor");
        var subject = Guid.NewGuid();
        await AddTrailEntryAsync("InvestigationNote", subject, "NoteCreated", actor.Id,
            Base,
            propertyName: "Note",
            oldValue: "<img src=x onerror=alert(2)>",
            newValue: "<script>alert(1)</script>");

        var client = await SignInAsync("xss.admin@netra.local", NetraRoles.SystemAdministrator);

        var listing = await client.GetStringAsync("/AuditLogs?entityType=InvestigationNote");
        listing.Should().NotContain("<script>alert(1)");
        listing.Should().NotContain("<img src=x");

        var entryId = await EntryIdForSubjectAsync("NoteCreated", subject);
        var detail = await client.GetStringAsync($"/AuditLogs/Details/{entryId}");

        // Displayed as text, exactly as stored.
        detail.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
        detail.Should().Contain("&lt;img src=x onerror=alert(2)&gt;");
        detail.Should().NotContain("<script");
        detail.Should().NotContain("<img src=x");
    }

    [Fact]
    public async Task Details_LinksBackToTheAccountWhenTheEntryIsAboutOne()
    {
        var actor = await SeedActorAsync("linked.actor@netra.local", "Lina", "Ked");
        var subject = Guid.NewGuid();
        var entryId = await AddTrailEntryAsync(
            "Account", subject, "UserEdited", actor.Id, Base,
            propertyName: "Rank", newValue: "\"Inspector\"");

        var client = await SignInAsync("linked.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync($"/AuditLogs/Details/{entryId}");

        html.Should().Contain("Open this account");
        html.Should().Contain("/Users/Details/" + subject);
        html.Should().Contain("Rank set to &quot;Inspector&quot;");
        html.Should().Contain("(nothing recorded)");
    }

    [Fact]
    public async Task Details_UnknownEntry_IsNotFound()
    {
        var client = await SignInAsync("unknown.entry@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.GetAsync("/AuditLogs/Details/11111111-1111-1111-1111-111111111111");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Details_MalformedIdentifier_IsNotFound()
    {
        var client = await SignInAsync("malformed.entry@netra.local", NetraRoles.SystemAdministrator);

        var response = await client.GetAsync("/AuditLogs/Details/not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Details_NeverPrintsCredentialMaterial()
    {
        var actor = await SeedActorAsync("secrets.actor@netra.local", "Secreta", "Actor");
        var entryId = await AddTrailEntryAsync("Account", Guid.NewGuid(), "UserCreated",
            actor.Id, Base, propertyName: "Roles", newValue: "Investigator");

        var client = await SignInAsync("secrets.admin@netra.local", NetraRoles.SystemAdministrator);

        var html = await client.GetStringAsync($"/AuditLogs/Details/{entryId}");

        html.Should().Contain("Investigator");
        html.Should().NotContain("PasswordHash");
        html.Should().NotContain("SecurityStamp");
        html.Should().NotContain("ConcurrencyStamp");
    }

    // ---------------------------------------------------------------- written by the module it audits

    [Fact]
    public async Task Trail_ShowsWhatUserAdministrationJustDid()
    {
        var admin = await SignInAsync("trail.witness@netra.local", NetraRoles.SystemAdministrator);

        var response = await admin.PostAsync("/Users/Create", AuthenticationScreenTests.Form(
            ("FirstName", "Witnessed"),
            ("LastName", "Profile"),
            ("UserName", "witnessed.profile@netra.local"),
            ("Email", "witnessed.profile@netra.local"),
            ("Roles", NetraRoles.Investigator),
            ("Password", "WitnessPass1!"),
            ("ConfirmPassword", "WitnessPass1!"),
            ("__RequestVerificationToken", await TokenAsync(admin, "/Users/Create"))));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var createdId = Guid.Parse(response.Headers.Location!.OriginalString.Split('/')[^1]);

        // A Senior Officer may read the trail even though they cannot administer accounts.
        var reader = await SignInAsync("trail.reader@netra.local", NetraRoles.SeniorOfficer);

        var entryId = await EntryIdForSubjectAsync("UserCreated", createdId);
        var listing = await reader.GetStringAsync("/AuditLogs?entityType=Account&action=UserCreated");

        listing.Should().Contain("Test User");
        listing.Should().Contain("User Created");
        listing.Should().Contain("Roles set to Investigator");
        listing.Should().Contain("/AuditLogs/Details/" + entryId);

        var detail = await reader.GetStringAsync($"/AuditLogs/Details/{entryId}");
        detail.Should().Contain(createdId.ToString());
        detail.Should().Contain("Open this account");

        // The entry links to a page only an administrator can open.
        (await reader.GetAsync($"/Users/Details/{createdId}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync($"/Users/Details/{createdId}")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        // No credential material reached the trail.
        var payloads = await InDbAsync(db => db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityId == createdId)
            .Select(a => new { a.OldValues, a.NewValues })
            .ToListAsync());
        payloads.Should().NotBeEmpty();
        payloads.Should().NotContain(p =>
            (p.OldValues ?? string.Empty).Contains("WitnessPass1!")
            || (p.NewValues ?? string.Empty).Contains("WitnessPass1!"));
    }

    [Fact]
    public async Task AuditLogPages_ContainNoJavaScript()
    {
        var actor = await SeedActorAsync("nojs.actor@netra.local", "Noj", "Say");
        var entryId = await AddTrailEntryAsync("CrimeCase", Guid.NewGuid(), "Created",
            actor.Id, Base, newValue: "Nothing to execute here");

        var client = await SignInAsync("nojs.admin@netra.local", NetraRoles.SystemAdministrator);

        foreach (var path in new[] { "/AuditLogs", $"/AuditLogs/Details/{entryId}" })
        {
            var html = await client.GetStringAsync(path);

            html.Should().NotContain("<script");
            html.Should().NotContain("</script");
            html.Should().NotContain("javascript:");
            html.Should().NotContain("onclick");
            html.Should().NotContain("onchange");
            html.Should().NotContain("fetch(");
            html.Should().NotContain("XMLHttpRequest");
            html.Should().NotContain(".js\"");
        }
    }
}
