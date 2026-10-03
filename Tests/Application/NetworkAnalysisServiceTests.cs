using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Network;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Geography;
using NETRA.Domain.Intelligence;
using NETRA.Domain.People;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;
using Xunit;

namespace NETRA.Tests.Application;

/// <summary>
/// Verifies bounded relational traversal, filters, metrics and cyclic explicit
/// network links against EF Core's in-memory provider. These are not live SQL Server
/// tests; SQL-translatable query shape is kept in the production service.
/// </summary>
public sealed class NetworkAnalysisServiceTests : IDisposable
{
    private readonly NetraDbContext _context;
    private readonly NetworkAnalysisService _service;
    private readonly Guid _rootId;
    private readonly Guid _secondPersonId;
    private readonly Guid _firstCaseId;
    private readonly Guid _secondLocationId;

    public NetworkAnalysisServiceTests()
    {
        _context = new NetraDbContext(
            new DbContextOptionsBuilder<NetraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var actor = new ApplicationUser
        {
            UserName = "network.seed@netra.local",
            Email = "network.seed@netra.local",
            FirstName = "Network",
            LastName = "Seed",
            EmailConfirmed = true,
            IsActive = true
        };
        var type = new CrimeType { Name = "Theft", Code = "THF", IsActive = true };
        var root = new Person { FirstName = "Avery", LastName = "Root", CreatedById = actor.Id, IsActive = true };
        var second = new Person { FirstName = "Bailey", LastName = "Second", CreatedById = actor.Id, IsActive = true };
        var third = new Person { FirstName = "Casey", LastName = "Third", CreatedById = actor.Id, IsActive = true };
        var firstLocation = new Location { Name = "Root Residence", Type = "Address", IsActive = true };
        var secondLocation = new Location { Name = "Case Scene", Type = "Scene", IsActive = true };
        var firstCase = new CrimeCase
        {
            CaseNumber = "NET-001",
            Title = "Network traversal case",
            CrimeTypeId = type.Id,
            Status = CaseStatus.Open,
            Severity = CaseSeverity.High,
            IncidentDate = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
            CreatedById = actor.Id
        };

        _context.AddRange(actor, type, root, second, third, firstLocation, secondLocation, firstCase);
        _context.SaveChanges();

        _context.CasePersons.AddRange(
            new CasePerson
            {
                PersonId = root.Id,
                CrimeCaseId = firstCase.Id,
                Role = PersonRole.Witness,
                AssociatedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc)
            },
            new CasePerson
            {
                PersonId = second.Id,
                CrimeCaseId = firstCase.Id,
                Role = PersonRole.Suspect,
                AssociatedAt = new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc)
            });
        _context.CaseLocations.Add(new CaseLocation
        {
            CrimeCaseId = firstCase.Id,
            LocationId = secondLocation.Id,
            LocationRole = "Incident",
            AssociatedAt = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc)
        });
        _context.PersonLocations.Add(new PersonLocation
        {
            PersonId = root.Id,
            LocationId = firstLocation.Id,
            LocationType = "Residence",
            FromDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var rootEntity = new NetworkEntity { Name = "Avery Root", PersonId = root.Id, IsActive = true };
        var secondEntity = new NetworkEntity { Name = "Bailey Second", PersonId = second.Id, IsActive = true };
        var thirdEntity = new NetworkEntity { Name = "Casey Third", PersonId = third.Id, IsActive = true };
        _context.NetworkEntities.AddRange(rootEntity, secondEntity, thirdEntity);
        _context.SaveChanges();
        _context.NetworkRelationships.AddRange(
            new NetworkRelationship
            {
                SourceEntityId = rootEntity.Id,
                TargetEntityId = secondEntity.Id,
                RelationshipType = RelationshipType.Family,
                EstablishedAt = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc),
                CreatedById = actor.Id,
                IsActive = true
            },
            new NetworkRelationship
            {
                SourceEntityId = secondEntity.Id,
                TargetEntityId = thirdEntity.Id,
                RelationshipType = RelationshipType.Colleague,
                EstablishedAt = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc),
                CreatedById = actor.Id,
                IsActive = true
            },
            new NetworkRelationship
            {
                SourceEntityId = thirdEntity.Id,
                TargetEntityId = rootEntity.Id,
                RelationshipType = RelationshipType.KnownAssociate,
                EstablishedAt = new DateTime(2026, 5, 12, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 5, 12, 0, 0, 0, DateTimeKind.Utc),
                CreatedById = actor.Id,
                IsActive = true
            });
        _context.SaveChanges();

        _rootId = root.Id;
        _secondPersonId = second.Id;
        _firstCaseId = firstCase.Id;
        _secondLocationId = secondLocation.Id;
        _service = new NetworkAnalysisService(_context);
    }

    public void Dispose() => _context.Dispose();

    private NetworkAnalysisQuery Query(int depth = 2) => new()
    {
        EntityType = NetworkEntityType.Person,
        EntityId = _rootId,
        Depth = depth
    };

    [Fact]
    public async Task SearchEntitiesAsync_FindsOnlyRequestedRealEntityType()
    {
        var results = await _service.SearchEntitiesAsync(NetworkEntityType.Person, "Avery");

        results.Should().ContainSingle(result => result.Id == _rootId);
        results.Should().OnlyContain(result => result.EntityType == NetworkEntityType.Person);
    }

    [Fact]
    public async Task GetGraphAsync_DepthOneDoesNotExpandSecondHop()
    {
        var graph = await _service.GetGraphAsync(Query(depth: 1));

        graph.Status.Should().Be(NetworkGraphStatus.Ready);
        graph.Nodes.Should().OnlyContain(node => node.Depth <= 1);
        graph.Nodes.Should().NotContain(node => node.Id == _secondLocationId);
        graph.Relationships.Should().Contain(relationship => relationship.RelationshipType == RelationshipType.WitnessTo);
    }

    [Fact]
    public async Task GetGraphAsync_DepthTwoAddsCaseLocationFromSecondHop()
    {
        var graph = await _service.GetGraphAsync(Query(depth: 2));

        graph.Nodes.Should().Contain(node => node.Id == _firstCaseId && node.EntityType == NetworkEntityType.Case);
        graph.Nodes.Should().Contain(node => node.Id == _secondLocationId && node.Depth == 2);
        graph.Relationships.Should().Contain(relationship => relationship.IsExplicitNetworkRelationship);
    }

    [Fact]
    public async Task GetGraphAsync_CycleIsVisitedOnceAndTerminates()
    {
        var graph = await _service.GetGraphAsync(Query(depth: 3));

        graph.Nodes.Select(node => node.Key).Should().OnlyHaveUniqueItems();
        graph.Nodes.Should().Contain(node => node.Id == _secondPersonId);
        graph.Nodes.Count.Should().BeLessThanOrEqualTo(NetworkAnalysisService.MaxNodes);
        graph.Relationships.Count.Should().BeLessThanOrEqualTo(NetworkAnalysisService.MaxEdges);
    }

    [Fact]
    public async Task GetGraphAsync_RelationshipFilterUsesRelationshipTypeEnum()
    {
        var graph = await _service.GetGraphAsync(new NetworkAnalysisQuery
        {
            EntityType = NetworkEntityType.Person,
            EntityId = _rootId,
            Depth = 2,
            RelationshipType = RelationshipType.Family
        });

        graph.Relationships.Should().ContainSingle();
        graph.Relationships[0].RelationshipType.Should().Be(RelationshipType.Family);
        graph.Nodes.Select(node => node.Id).Should().BeEquivalentTo([_rootId, _secondPersonId]);
    }

    [Fact]
    public async Task GetGraphAsync_DateFilterUsesStoredRelationshipAndIncidentDates()
    {
        var graph = await _service.GetGraphAsync(new NetworkAnalysisQuery
        {
            EntityType = NetworkEntityType.Person,
            EntityId = _rootId,
            Depth = 2,
            DateFrom = new DateTime(2026, 6, 1),
            DateTo = new DateTime(2026, 6, 3)
        });

        graph.Relationships.Should().NotContain(relationship =>
            relationship.RelationshipType == RelationshipType.Family
            || relationship.RelationshipType == RelationshipType.KnownAssociate);
        graph.Relationships.Should().OnlyContain(relationship =>
            relationship.OccurredAt >= new DateTime(2026, 6, 1)
            && relationship.OccurredAt < new DateTime(2026, 6, 4));
    }

    [Fact]
    public async Task GetGraphAsync_ConnectedMetricsExcludeIsolatedSelectedEntity()
    {
        var graph = await _service.GetGraphAsync(new NetworkAnalysisQuery
        {
            EntityType = NetworkEntityType.Person,
            EntityId = _rootId,
            Depth = 2,
            DateFrom = new DateTime(2020, 1, 1),
            DateTo = new DateTime(2020, 1, 31)
        });

        graph.Status.Should().Be(NetworkGraphStatus.NoRelationships);
        graph.Metrics.ConnectionCount.Should().Be(0);
        graph.Metrics.ConnectedPersonCount.Should().Be(0);
        graph.Metrics.ConnectedCaseCount.Should().Be(0);
        graph.Metrics.ConnectedLocationCount.Should().Be(0);
    }

    [Fact]
    public async Task GetGraphAsync_MetricsReportDeterministicConnectionAndDegreeCounts()
    {
        var graph = await _service.GetGraphAsync(new NetworkAnalysisQuery
        {
            EntityType = NetworkEntityType.Person,
            EntityId = _rootId,
            Depth = 2,
            RelationshipType = RelationshipType.Family
        });

        graph.Metrics.ConnectionCount.Should().Be(1);
        graph.Metrics.ConnectedPersonCount.Should().Be(2);
        graph.Metrics.ConnectedCaseCount.Should().Be(0);
        graph.Metrics.NodeDegrees.Should().OnlyContain(metric => metric.Degree == 1);
        graph.Detail!.ConnectedEntities.Should().ContainSingle(entity => entity.Id == _secondPersonId);
    }

    [Fact]
    public async Task GetGraphAsync_HandlesAwaitingAndNotFoundStates()
    {
        var awaiting = await _service.GetGraphAsync(new NetworkAnalysisQuery { Depth = 1 });
        var notFound = await _service.GetGraphAsync(new NetworkAnalysisQuery
        {
            EntityType = NetworkEntityType.Person,
            EntityId = Guid.NewGuid(),
            Depth = 1
        });

        awaiting.Status.Should().Be(NetworkGraphStatus.AwaitingSelection);
        notFound.Status.Should().Be(NetworkGraphStatus.SelectedEntityNotFound);
    }

    [Fact]
    public async Task GetGraphAsync_RejectsExcessiveDepthAndInvertedDates()
    {
        var invalidDepth = () => _service.GetGraphAsync(Query(depth: 4));
        var invertedDates = () => _service.GetGraphAsync(new NetworkAnalysisQuery
        {
            EntityType = NetworkEntityType.Person,
            EntityId = _rootId,
            Depth = 1,
            DateFrom = new DateTime(2026, 6, 4),
            DateTo = new DateTime(2026, 6, 1)
        });

        await invalidDepth.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await invertedDates.Should().ThrowAsync<ArgumentException>().WithMessage("*on or before*");
    }
}
