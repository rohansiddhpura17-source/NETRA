using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Intelligence;
using NETRA.Domain.People;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// Real MVC integration coverage for /Network. The app is hosted with EF Core's
/// in-memory provider by <see cref="NetraWebFactory"/>; these are not live SQL Server
/// tests.
/// </summary>
public sealed class NetworkIntegrationTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";
    private readonly NetraWebFactory _factory;
    private readonly Guid _rootId;

    public NetworkIntegrationTests(NetraWebFactory factory)
    {
        _factory = factory;
        _rootId = SeedNetworkDataAsync().GetAwaiter().GetResult();
    }

    private async Task<Guid> SeedNetworkDataAsync()
    {
        await _factory.SeedReferenceDataAsync();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var existing = await context.Persons.FirstOrDefaultAsync(person => person.LastName == "WebNetworkRoot");
        if (existing is not null)
        {
            return existing.Id;
        }

        var actor = new ApplicationUser
        {
            UserName = "network.web.seed@netra.local",
            Email = "network.web.seed@netra.local",
            FirstName = "Network",
            LastName = "Seed",
            EmailConfirmed = true,
            IsActive = true
        };
        var crimeType = await context.CrimeTypes.FirstAsync(type => type.Code == "THF");
        var root = new Person
        {
            FirstName = "Morgan",
            LastName = "WebNetworkRoot",
            CreatedById = actor.Id,
            IsActive = true
        };
        var linked = new Person
        {
            FirstName = "Riley",
            LastName = "WebNetworkLinked",
            CreatedById = actor.Id,
            IsActive = true
        };
        var location = new Location { Name = "Network Test Scene", Type = "Scene", IsActive = true };
        var crimeCase = new CrimeCase
        {
            CaseNumber = "NET-WEB-001",
            Title = "Network integration case",
            CrimeTypeId = crimeType.Id,
            Status = CaseStatus.Open,
            Severity = CaseSeverity.Medium,
            IncidentDate = new DateTime(2026, 6, 8, 9, 0, 0, DateTimeKind.Utc),
            CreatedById = actor.Id
        };

        context.AddRange(actor, root, linked, location, crimeCase);
        await context.SaveChangesAsync();

        context.CasePersons.AddRange(
            new CasePerson
            {
                CrimeCaseId = crimeCase.Id,
                PersonId = root.Id,
                Role = PersonRole.Witness,
                AssociatedAt = new DateTime(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc)
            },
            new CasePerson
            {
                CrimeCaseId = crimeCase.Id,
                PersonId = linked.Id,
                Role = PersonRole.Suspect,
                AssociatedAt = new DateTime(2026, 6, 8, 11, 0, 0, DateTimeKind.Utc)
            });
        context.CaseLocations.Add(new CaseLocation
        {
            CrimeCaseId = crimeCase.Id,
            LocationId = location.Id,
            LocationRole = "Incident",
            AssociatedAt = new DateTime(2026, 6, 8, 9, 0, 0, DateTimeKind.Utc)
        });

        var rootEntity = new NetworkEntity { Name = "Morgan Root", PersonId = root.Id, IsActive = true };
        var linkedEntity = new NetworkEntity { Name = "Riley Linked", PersonId = linked.Id, IsActive = true };
        context.NetworkEntities.AddRange(rootEntity, linkedEntity);
        await context.SaveChangesAsync();
        context.NetworkRelationships.Add(new NetworkRelationship
        {
            SourceEntityId = rootEntity.Id,
            TargetEntityId = linkedEntity.Id,
            RelationshipType = RelationshipType.Family,
            EstablishedAt = new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc),
            CreatedById = actor.Id,
            IsActive = true
        });
        await context.SaveChangesAsync();

        return root.Id;
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

    private string RootQuery(int depth = 2) =>
        $"EntityType=Person&EntityId={_rootId}&Depth={depth}";

    [Fact]
    public async Task Network_AnonymousRedirectsToLogin()
    {
        var response = await AnonymousClient().GetAsync("/Network");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Network_InvestigatorIsForbiddenByExplicitNetworkPolicy()
    {
        var client = await SignInAsync("network.investigator@netra.local", "Investigator");

        var response = await client.GetAsync("/Network");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Police / Intelligence Analyst", "network.analyst@netra.local")]
    [InlineData("Senior Officer", "network.senior@netra.local")]
    [InlineData("System Administrator", "network.admin@netra.local")]
    public async Task Network_AnalyticalRolesAreAuthorised(string role, string email)
    {
        var client = await SignInAsync(email, role);

        var response = await client.GetAsync("/Network");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Network_RendersGraphSelectedDetailAndServerRoundTripLinks()
    {
        var client = await SignInAsync("network.page@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync($"/Network?{RootQuery()}");

        html.Should().Contain("Network &amp; Link Analysis");
        html.Should().Contain("<svg class=\"netra-network\"");
        html.Should().Contain("Morgan WebNetworkRoot");
        html.Should().Contain("Selected Entity");
        html.Should().Contain("Relationship Summary");
        html.Should().Contain("Connected Entities");
        html.Should().Contain("Visualization is rendered server-side as SVG.");
        html.Should().Contain("EntityType=Case");
    }

    [Fact]
    public async Task Network_RelationshipFiltersPersistAndLimitResults()
    {
        var client = await SignInAsync("network.filter@netra.local", "Senior Officer");

        var html = await client.GetStringAsync($"/Network?{RootQuery()}&RelationshipType=Family");

        html.Should().Contain("value=\"Family\" selected=\"selected\"");
        // The filter dropdown always lists every relationship type, so assert on the
        // result sections (graph summary onward), not the whole page.
        var results = html[html.IndexOf("Relationship Summary", StringComparison.Ordinal)..];
        results.Should().Contain("Family");
        results.Should().NotContain("Witness To");
    }

    [Fact]
    public async Task Network_HandlesNoSelectionNotFoundNoDataAndExcessiveDepth()
    {
        var client = await SignInAsync("network.states@netra.local", "System Administrator");

        var noSelection = await client.GetStringAsync("/Network");
        var notFound = await client.GetStringAsync($"/Network?EntityType=Person&EntityId={Guid.NewGuid()}");
        var noData = await client.GetStringAsync($"/Network?{RootQuery()}&DateFrom=2000-01-01&DateTo=2000-01-31");
        var excessiveDepth = await client.GetStringAsync($"/Network?{RootQuery(99)}");

        noSelection.Should().Contain("Choose a starting entity");
        notFound.Should().Contain("Selected entity not found");
        noData.Should().Contain("No relationships found");
        excessiveDepth.Should().Contain("Traversal depth is limited to 3.");
        excessiveDepth.Should().Contain("Depth 3 investigation");
    }

    [Fact]
    public async Task Network_InvalidEntityTypeShowsValidationAndEmitsNoJavaScript()
    {
        var client = await SignInAsync("network.nojs@netra.local", "Police / Intelligence Analyst");

        var html = await client.GetStringAsync($"/Network?EntityType=invalid&EntityId={_rootId}");

        html.Should().Contain("Choose Person, Case, or Location as the starting entity type.");

        // The antiforgery field's base64 value is random per request and can contain
        // arbitrary substrings ("D3", "onclick"), so strip it before the no-JS scan.
        var tokenStart = html.IndexOf("__RequestVerificationToken", StringComparison.Ordinal);
        if (tokenStart >= 0)
        {
            var tokenEnd = html.IndexOf("\" />", tokenStart, StringComparison.Ordinal);
            html = html[..tokenStart] + html[tokenEnd..];
        }

        html.Should().NotContain("<script");
        html.Should().NotContain("javascript:");
        html.Should().NotContain("onclick");
        html.Should().NotContain("Cytoscape");
        html.Should().NotContain("D3");
    }

    [Fact]
    public async Task Network_SidebarUsesImplementedProtectedRoute()
    {
        var client = await SignInAsync("network.nav@netra.local", "System Administrator");

        var html = await client.GetStringAsync("/Dashboard");

        html.Should().Contain("href=\"/network\"");
    }
}
