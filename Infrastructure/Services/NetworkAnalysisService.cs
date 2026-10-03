using Microsoft.EntityFrameworkCore;
using NETRA.Application.Network;
using NETRA.Domain.Cases;
using NETRA.Domain.Enums;
using NETRA.Domain.Intelligence;
using NETRA.Domain.People;
using NETRA.Infrastructure.Data;

namespace NETRA.Infrastructure.Services;

/// <summary>
/// Builds a small relationship graph from existing NETRA relational records.
///
/// Traversal is breadth-first and therefore deterministic: each layer is queried
/// in batches, duplicate/cyclic nodes are tracked in a visited set, and the search
/// ends at <see cref="MaxTraversalDepth"/>. Direct Person–Case, Case–Location and
/// Person–Location associations are projected alongside explicitly recorded
/// <see cref="NetworkRelationship"/> records. No graph data is duplicated or
/// persisted by this service.
/// </summary>
public sealed class NetworkAnalysisService : INetworkAnalysisService
{
    public const int DefaultTraversalDepth = NetworkAnalysisLimits.DefaultTraversalDepth;
    public const int MaxTraversalDepth = NetworkAnalysisLimits.MaxTraversalDepth;
    public const int MaxNodes = 120;
    public const int MaxEdges = 240;
    public const int MaxRelationshipsPerLayer = 320;
    public const int MaxLookupRowsPerType = 80;

    private readonly NetraDbContext _context;

    public NetworkAnalysisService(NetraDbContext context)
    {
        _context = context;
    }

    public async Task<NetworkGraphDto> GetGraphAsync(
        NetworkAnalysisQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Depth is < 1 or > MaxTraversalDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(query),
                $"Traversal depth must be between 1 and {MaxTraversalDepth}.");
        }

        var dateFrom = query.DateFrom?.Date;
        var dateTo = query.DateTo?.Date;
        if (dateFrom is not null && dateTo is not null && dateFrom > dateTo)
        {
            throw new ArgumentException("Date From must be on or before Date To.", nameof(query));
        }

        if (query.EntityType is null || query.EntityId is null)
        {
            return new NetworkGraphDto
            {
                Status = NetworkGraphStatus.AwaitingSelection,
                Depth = query.Depth
            };
        }

        var selectedKey = new NodeKey(query.EntityType.Value, query.EntityId.Value);
        var selectedNodes = await LoadNodesAsync([selectedKey], null, null, cancellationToken);
        if (!selectedNodes.TryGetValue(selectedKey, out var selected))
        {
            return new NetworkGraphDto
            {
                Status = NetworkGraphStatus.SelectedEntityNotFound,
                Depth = query.Depth
            };
        }

        // Date filters apply to stored incident dates for case nodes. A selected case
        // outside the period remains identifiable but is deliberately not expanded.
        if (selected.EntityType == NetworkEntityType.Case
            && !IsCaseInRange(selected.CaseIncidentDate, dateFrom, dateTo))
        {
            return BuildGraph(
                NetworkGraphStatus.FilteredOut,
                query.Depth,
                false,
                selected,
                new Dictionary<NodeKey, NetworkNodeDto>(),
                new Dictionary<string, RawRelationship>());
        }

        var nodes = new Dictionary<NodeKey, NetworkNodeDto> { [selectedKey] = selected };
        var relationships = new Dictionary<string, RawRelationship>(StringComparer.Ordinal);
        var frontier = new List<NodeKey> { selectedKey };
        var resultLimited = false;

        for (var layer = 1; layer <= query.Depth && frontier.Count > 0; layer++)
        {
            var layerRelationships = await LoadRelationshipsAsync(
                frontier, query.RelationshipType, dateFrom, dateTo, cancellationToken);

            var candidates = layerRelationships
                .SelectMany(edge => new[] { edge.Source, edge.Target })
                .Where(key => !nodes.ContainsKey(key))
                .Distinct()
                .OrderBy(key => key, NodeKeyComparer.Instance)
                .ToList();

            if (candidates.Count > MaxNodes - nodes.Count)
            {
                candidates = candidates.Take(Math.Max(0, MaxNodes - nodes.Count)).ToList();
                resultLimited = true;
            }

            var loaded = candidates.Count == 0
                ? new Dictionary<NodeKey, NetworkNodeDto>()
                : await LoadNodesAsync(candidates, dateFrom, dateTo, cancellationToken);

            var nextFrontier = new List<NodeKey>();
            foreach (var key in candidates)
            {
                if (!loaded.TryGetValue(key, out var node))
                {
                    continue;
                }

                node.Depth = layer;
                nodes[key] = node;
                nextFrontier.Add(key);
            }

            foreach (var relationship in layerRelationships)
            {
                if (!nodes.ContainsKey(relationship.Source) || !nodes.ContainsKey(relationship.Target))
                {
                    continue;
                }

                if (relationships.Count >= MaxEdges && !relationships.ContainsKey(relationship.Id))
                {
                    resultLimited = true;
                    continue;
                }

                relationships.TryAdd(relationship.Id, relationship);
            }

            frontier = nextFrontier;
        }

        var status = relationships.Count == 0
            ? NetworkGraphStatus.NoRelationships
            : NetworkGraphStatus.Ready;

        return BuildGraph(status, query.Depth, resultLimited, selected, nodes, relationships);
    }

    public async Task<IReadOnlyList<NetworkEntityLookupDto>> SearchEntitiesAsync(
        NetworkEntityType? entityType,
        string? searchTerm,
        CancellationToken cancellationToken = default)
    {
        var term = searchTerm?.Trim();
        var people = new List<NetworkEntityLookupDto>();
        var cases = new List<NetworkEntityLookupDto>();
        var locations = new List<NetworkEntityLookupDto>();

        if (entityType is null or NetworkEntityType.Person)
        {
            var query = _context.Persons.AsNoTracking().Where(person => person.IsActive);
            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(person =>
                    person.FirstName.Contains(term) || person.LastName.Contains(term)
                    || (person.MiddleName != null && person.MiddleName.Contains(term)));
            }

            people = (await query
                .OrderBy(person => person.LastName)
                .ThenBy(person => person.FirstName)
                .ThenBy(person => person.Id)
                .Take(MaxLookupRowsPerType)
                .Select(person => new { person.Id, person.FirstName, person.MiddleName, person.LastName })
                .ToListAsync(cancellationToken))
                .Select(person => new NetworkEntityLookupDto
                {
                    Id = person.Id,
                    EntityType = NetworkEntityType.Person,
                    Name = PersonName(person.FirstName, person.MiddleName, person.LastName),
                    SecondaryLabel = "Person"
                })
                .ToList();
        }

        if (entityType is null or NetworkEntityType.Case)
        {
            var query = _context.CrimeCases.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(crimeCase =>
                    crimeCase.CaseNumber.Contains(term) || crimeCase.Title.Contains(term));
            }

            cases = (await query
                .OrderByDescending(crimeCase => crimeCase.IncidentDate)
                .ThenBy(crimeCase => crimeCase.CaseNumber)
                .ThenBy(crimeCase => crimeCase.Id)
                .Take(MaxLookupRowsPerType)
                .Select(crimeCase => new { crimeCase.Id, crimeCase.CaseNumber, crimeCase.Title })
                .ToListAsync(cancellationToken))
                .Select(crimeCase => new NetworkEntityLookupDto
                {
                    Id = crimeCase.Id,
                    EntityType = NetworkEntityType.Case,
                    Name = crimeCase.CaseNumber,
                    SecondaryLabel = crimeCase.Title
                })
                .ToList();
        }

        if (entityType is null or NetworkEntityType.Location)
        {
            var query = _context.Locations.AsNoTracking().Where(location => location.IsActive);
            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(location =>
                    location.Name.Contains(term) || (location.Type != null && location.Type.Contains(term)));
            }

            locations = (await query
                .OrderBy(location => location.Name)
                .ThenBy(location => location.Id)
                .Take(MaxLookupRowsPerType)
                .Select(location => new { location.Id, location.Name, location.Type })
                .ToListAsync(cancellationToken))
                .Select(location => new NetworkEntityLookupDto
                {
                    Id = location.Id,
                    EntityType = NetworkEntityType.Location,
                    Name = location.Name,
                    SecondaryLabel = string.IsNullOrWhiteSpace(location.Type) ? "Location" : location.Type
                })
                .ToList();
        }

        return people.Concat(cases).Concat(locations)
            .OrderBy(item => item.EntityType)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Id)
            .ToList();
    }

    private async Task<Dictionary<NodeKey, NetworkNodeDto>> LoadNodesAsync(
        IReadOnlyCollection<NodeKey> keys,
        DateTime? dateFrom,
        DateTime? dateTo,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<NodeKey, NetworkNodeDto>();
        var personIds = keys.Where(key => key.Type == NetworkEntityType.Person).Select(key => key.Id).ToArray();
        var caseIds = keys.Where(key => key.Type == NetworkEntityType.Case).Select(key => key.Id).ToArray();
        var locationIds = keys.Where(key => key.Type == NetworkEntityType.Location).Select(key => key.Id).ToArray();

        if (personIds.Length > 0)
        {
            var people = await _context.Persons.AsNoTracking()
                .Where(person => personIds.Contains(person.Id) && person.IsActive)
                .Select(person => new
                {
                    person.Id,
                    person.FirstName,
                    person.MiddleName,
                    person.LastName,
                    person.Gender,
                    person.CreatedAt
                })
                .ToListAsync(cancellationToken);

            foreach (var person in people)
            {
                var key = new NodeKey(NetworkEntityType.Person, person.Id);
                result[key] = new NetworkNodeDto
                {
                    Key = key.ToString(),
                    Id = person.Id,
                    EntityType = NetworkEntityType.Person,
                    Name = PersonName(person.FirstName, person.MiddleName, person.LastName),
                    SecondaryLabel = "Person",
                    Metadata =
                    [
                        new NetworkMetadataDto { Label = "Record type", Value = "Person" },
                        new NetworkMetadataDto { Label = "Gender", Value = person.Gender ?? "Not recorded" },
                        new NetworkMetadataDto { Label = "Record created", Value = person.CreatedAt.ToString("dd MMM yyyy") }
                    ]
                };
            }
        }

        if (caseIds.Length > 0)
        {
            var query = _context.CrimeCases.AsNoTracking().Where(crimeCase => caseIds.Contains(crimeCase.Id));
            if (dateFrom is not null && dateTo is not null)
            {
                var dateToExclusive = dateTo.Value.AddDays(1);
                query = query.Where(crimeCase =>
                    crimeCase.IncidentDate >= dateFrom.Value && crimeCase.IncidentDate < dateToExclusive);
            }

            var cases = await query
                .Select(crimeCase => new
                {
                    crimeCase.Id,
                    crimeCase.CaseNumber,
                    crimeCase.Title,
                    CrimeTypeName = crimeCase.CrimeType.Name,
                    crimeCase.Status,
                    crimeCase.Severity,
                    crimeCase.IncidentDate
                })
                .ToListAsync(cancellationToken);

            foreach (var crimeCase in cases)
            {
                var key = new NodeKey(NetworkEntityType.Case, crimeCase.Id);
                result[key] = new NetworkNodeDto
                {
                    Key = key.ToString(),
                    Id = crimeCase.Id,
                    EntityType = NetworkEntityType.Case,
                    Name = crimeCase.CaseNumber,
                    SecondaryLabel = crimeCase.Title,
                    CaseIncidentDate = crimeCase.IncidentDate,
                    Metadata =
                    [
                        new NetworkMetadataDto { Label = "Record type", Value = "Case" },
                        new NetworkMetadataDto { Label = "Crime type", Value = crimeCase.CrimeTypeName },
                        new NetworkMetadataDto { Label = "Status", Value = DisplayWords(crimeCase.Status.ToString()) },
                        new NetworkMetadataDto { Label = "Incident date", Value = crimeCase.IncidentDate.ToString("dd MMM yyyy") },
                        new NetworkMetadataDto { Label = "Severity", Value = crimeCase.Severity.ToString() }
                    ]
                };
            }
        }

        if (locationIds.Length > 0)
        {
            var locations = await _context.Locations.AsNoTracking()
                .Where(location => locationIds.Contains(location.Id) && location.IsActive)
                .Select(location => new
                {
                    location.Id,
                    location.Name,
                    location.Type,
                    DistrictName = location.District != null ? location.District.Name : null
                })
                .ToListAsync(cancellationToken);

            foreach (var location in locations)
            {
                var key = new NodeKey(NetworkEntityType.Location, location.Id);
                result[key] = new NetworkNodeDto
                {
                    Key = key.ToString(),
                    Id = location.Id,
                    EntityType = NetworkEntityType.Location,
                    Name = location.Name,
                    SecondaryLabel = string.IsNullOrWhiteSpace(location.Type) ? "Location" : location.Type,
                    Metadata =
                    [
                        new NetworkMetadataDto { Label = "Record type", Value = "Location" },
                        new NetworkMetadataDto { Label = "Type", Value = location.Type ?? "Not recorded" },
                        new NetworkMetadataDto { Label = "District", Value = location.DistrictName ?? "Not recorded" }
                    ]
                };
            }
        }

        return result;
    }

    private async Task<List<RawRelationship>> LoadRelationshipsAsync(
        IReadOnlyCollection<NodeKey> frontier,
        RelationshipType? relationshipType,
        DateTime? dateFrom,
        DateTime? dateTo,
        CancellationToken cancellationToken)
    {
        var personIds = frontier.Where(key => key.Type == NetworkEntityType.Person).Select(key => key.Id).ToArray();
        var caseIds = frontier.Where(key => key.Type == NetworkEntityType.Case).Select(key => key.Id).ToArray();
        var locationIds = frontier.Where(key => key.Type == NetworkEntityType.Location).Select(key => key.Id).ToArray();
        var all = new List<RawRelationship>();

        if (personIds.Length > 0 || caseIds.Length > 0)
        {
            var query = _context.CasePersons.AsNoTracking()
                .Where(association => personIds.Contains(association.PersonId) || caseIds.Contains(association.CrimeCaseId));
            query = ApplyCasePersonTypeFilter(query, relationshipType);
            query = ApplyCasePersonDateFilter(query, dateFrom, dateTo);

            var associations = await query
                .OrderBy(association => association.AssociatedAt)
                .ThenBy(association => association.Id)
                .Take(MaxRelationshipsPerLayer)
                .Select(association => new
                {
                    association.Id,
                    association.PersonId,
                    association.CrimeCaseId,
                    association.Role,
                    association.AssociatedAt
                })
                .ToListAsync(cancellationToken);

            all.AddRange(associations.Select(association => new RawRelationship(
                $"case-person:{association.Id:D}",
                new NodeKey(NetworkEntityType.Person, association.PersonId),
                new NodeKey(NetworkEntityType.Case, association.CrimeCaseId),
                RelationshipFor(association.Role),
                $"Case association: {DisplayWords(association.Role.ToString())}",
                association.AssociatedAt,
                false)));
        }

        if ((caseIds.Length > 0 || locationIds.Length > 0)
            && (relationshipType is null or RelationshipType.Associate))
        {
            var query = _context.CaseLocations.AsNoTracking()
                .Where(association => caseIds.Contains(association.CrimeCaseId) || locationIds.Contains(association.LocationId));
            query = ApplyCaseLocationDateFilter(query, dateFrom, dateTo);

            var associations = await query
                .OrderBy(association => association.AssociatedAt)
                .ThenBy(association => association.Id)
                .Take(MaxRelationshipsPerLayer)
                .Select(association => new
                {
                    association.Id,
                    association.CrimeCaseId,
                    association.LocationId,
                    association.LocationRole,
                    association.AssociatedAt
                })
                .ToListAsync(cancellationToken);

            all.AddRange(associations.Select(association => new RawRelationship(
                $"case-location:{association.Id:D}",
                new NodeKey(NetworkEntityType.Case, association.CrimeCaseId),
                new NodeKey(NetworkEntityType.Location, association.LocationId),
                RelationshipType.Associate,
                $"Case location: {association.LocationRole ?? "Associated"}",
                association.AssociatedAt,
                false)));
        }

        if ((personIds.Length > 0 || locationIds.Length > 0)
            && (relationshipType is null or RelationshipType.Associate))
        {
            var query = _context.PersonLocations.AsNoTracking()
                .Where(association => personIds.Contains(association.PersonId) || locationIds.Contains(association.LocationId));
            query = ApplyPersonLocationDateFilter(query, dateFrom, dateTo);

            var associations = await query
                .OrderBy(association => association.CreatedAt)
                .ThenBy(association => association.Id)
                .Take(MaxRelationshipsPerLayer)
                .Select(association => new
                {
                    association.Id,
                    association.PersonId,
                    association.LocationId,
                    association.LocationType,
                    association.FromDate,
                    association.CreatedAt
                })
                .ToListAsync(cancellationToken);

            all.AddRange(associations.Select(association => new RawRelationship(
                $"person-location:{association.Id:D}",
                new NodeKey(NetworkEntityType.Person, association.PersonId),
                new NodeKey(NetworkEntityType.Location, association.LocationId),
                RelationshipType.Associate,
                $"Person location: {association.LocationType ?? "Associated"}",
                association.FromDate ?? association.CreatedAt,
                false)));
        }

        if (frontier.Count > 0)
        {
            var networkEntityIds = await _context.NetworkEntities.AsNoTracking()
                .Where(entity => entity.IsActive &&
                    (entity.PersonId != null && personIds.Contains(entity.PersonId.Value)
                     || entity.CrimeCaseId != null && caseIds.Contains(entity.CrimeCaseId.Value)
                     || entity.LocationId != null && locationIds.Contains(entity.LocationId.Value)))
                .Select(entity => entity.Id)
                .ToListAsync(cancellationToken);

            if (networkEntityIds.Count > 0)
            {
                var query = _context.NetworkRelationships.AsNoTracking()
                    .Where(relationship => relationship.IsActive
                        && (networkEntityIds.Contains(relationship.SourceEntityId)
                            || networkEntityIds.Contains(relationship.TargetEntityId))
                        && relationship.SourceEntity.IsActive && relationship.TargetEntity.IsActive);
                query = ApplyNetworkRelationshipDateFilter(query, dateFrom, dateTo);
                if (relationshipType is { } type)
                {
                    query = query.Where(relationship => relationship.RelationshipType == type);
                }

                var relationships = await query
                    .OrderBy(relationship => relationship.EstablishedAt ?? relationship.CreatedAt)
                    .ThenBy(relationship => relationship.Id)
                    .Take(MaxRelationshipsPerLayer)
                    .Select(relationship => new
                    {
                        relationship.Id,
                        relationship.RelationshipType,
                        relationship.EstablishedAt,
                        relationship.CreatedAt,
                        SourcePersonId = relationship.SourceEntity.PersonId,
                        SourceCaseId = relationship.SourceEntity.CrimeCaseId,
                        SourceLocationId = relationship.SourceEntity.LocationId,
                        TargetPersonId = relationship.TargetEntity.PersonId,
                        TargetCaseId = relationship.TargetEntity.CrimeCaseId,
                        TargetLocationId = relationship.TargetEntity.LocationId
                    })
                    .ToListAsync(cancellationToken);

                foreach (var relationship in relationships)
                {
                    var source = NodeKey.FromReference(
                        relationship.SourcePersonId, relationship.SourceCaseId, relationship.SourceLocationId);
                    var target = NodeKey.FromReference(
                        relationship.TargetPersonId, relationship.TargetCaseId, relationship.TargetLocationId);

                    // A malformed legacy NetworkEntity is ignored rather than being
                    // allowed to appear as a disconnected, fabricated graph node.
                    if (source is null || target is null)
                    {
                        continue;
                    }

                    all.Add(new RawRelationship(
                        $"network:{relationship.Id:D}",
                        source.Value,
                        target.Value,
                        relationship.RelationshipType,
                        DisplayWords(relationship.RelationshipType.ToString()),
                        relationship.EstablishedAt ?? relationship.CreatedAt,
                        true));
                }
            }
        }

        return all
            .OrderBy(relationship => relationship.Source, NodeKeyComparer.Instance)
            .ThenBy(relationship => relationship.Target, NodeKeyComparer.Instance)
            .ThenBy(relationship => relationship.RelationshipType)
            .ThenBy(relationship => relationship.Id, StringComparer.Ordinal)
            .Take(MaxRelationshipsPerLayer)
            .ToList();
    }

    private static IQueryable<CasePerson> ApplyCasePersonTypeFilter(
        IQueryable<CasePerson> query,
        RelationshipType? relationshipType) => relationshipType switch
        {
            null => query,
            RelationshipType.SuspectOf => query.Where(association => association.Role == PersonRole.Suspect),
            RelationshipType.VictimOf => query.Where(association => association.Role == PersonRole.Victim),
            RelationshipType.WitnessTo => query.Where(association => association.Role == PersonRole.Witness),
            RelationshipType.Associate => query.Where(association =>
                association.Role != PersonRole.Suspect
                && association.Role != PersonRole.Victim
                && association.Role != PersonRole.Witness),
            _ => query.Where(_ => false)
        };

    private static IQueryable<CasePerson> ApplyCasePersonDateFilter(
        IQueryable<CasePerson> query,
        DateTime? dateFrom,
        DateTime? dateTo)
    {
        if (dateFrom is null || dateTo is null)
        {
            return query;
        }

        var dateToExclusive = dateTo.Value.AddDays(1);
        return query.Where(association =>
            association.AssociatedAt >= dateFrom.Value && association.AssociatedAt < dateToExclusive
            && association.CrimeCase.IncidentDate >= dateFrom.Value
            && association.CrimeCase.IncidentDate < dateToExclusive);
    }

    private static IQueryable<CaseLocation> ApplyCaseLocationDateFilter(
        IQueryable<CaseLocation> query,
        DateTime? dateFrom,
        DateTime? dateTo)
    {
        if (dateFrom is null || dateTo is null)
        {
            return query;
        }

        var dateToExclusive = dateTo.Value.AddDays(1);
        return query.Where(association =>
            association.AssociatedAt >= dateFrom.Value && association.AssociatedAt < dateToExclusive
            && association.CrimeCase.IncidentDate >= dateFrom.Value
            && association.CrimeCase.IncidentDate < dateToExclusive);
    }

    private static IQueryable<PersonLocation> ApplyPersonLocationDateFilter(
        IQueryable<PersonLocation> query,
        DateTime? dateFrom,
        DateTime? dateTo)
    {
        if (dateFrom is null || dateTo is null)
        {
            return query;
        }

        var dateToExclusive = dateTo.Value.AddDays(1);
        return query.Where(association =>
            (association.FromDate ?? association.CreatedAt) >= dateFrom.Value
            && (association.FromDate ?? association.CreatedAt) < dateToExclusive);
    }

    private static IQueryable<NetworkRelationship> ApplyNetworkRelationshipDateFilter(
        IQueryable<NetworkRelationship> query,
        DateTime? dateFrom,
        DateTime? dateTo)
    {
        if (dateFrom is null || dateTo is null)
        {
            return query;
        }

        var dateToExclusive = dateTo.Value.AddDays(1);
        return query.Where(relationship =>
            (relationship.EstablishedAt ?? relationship.CreatedAt) >= dateFrom.Value
            && (relationship.EstablishedAt ?? relationship.CreatedAt) < dateToExclusive);
    }

    private static RelationshipType RelationshipFor(PersonRole role) => role switch
    {
        PersonRole.Suspect => RelationshipType.SuspectOf,
        PersonRole.Victim => RelationshipType.VictimOf,
        PersonRole.Witness => RelationshipType.WitnessTo,
        _ => RelationshipType.Associate
    };

    private static NetworkGraphDto BuildGraph(
        NetworkGraphStatus status,
        int depth,
        bool isResultLimited,
        NetworkNodeDto selected,
        IReadOnlyDictionary<NodeKey, NetworkNodeDto> nodes,
        IReadOnlyDictionary<string, RawRelationship> relationships)
    {
        var orderedNodes = nodes.Values
            .OrderBy(node => node.Depth)
            .ThenBy(node => node.EntityType)
            .ThenBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Id)
            .ToList();
        var orderedRelationships = relationships.Values
            .OrderBy(relationship => relationship.Source, NodeKeyComparer.Instance)
            .ThenBy(relationship => relationship.Target, NodeKeyComparer.Instance)
            .ThenBy(relationship => relationship.RelationshipType)
            .ThenBy(relationship => relationship.Id, StringComparer.Ordinal)
            .Select(relationship => relationship.ToDto())
            .ToList();
        var metrics = BuildMetrics(orderedNodes, orderedRelationships);

        return new NetworkGraphDto
        {
            Status = status,
            Depth = depth,
            IsResultLimited = isResultLimited,
            SelectedNode = selected,
            Nodes = orderedNodes,
            Relationships = orderedRelationships,
            Metrics = metrics,
            Detail = BuildDetail(selected, orderedNodes, orderedRelationships)
        };
    }

    private static NetworkMetricsDto BuildMetrics(
        IReadOnlyList<NetworkNodeDto> nodes,
        IReadOnlyList<NetworkRelationshipDto> relationships)
    {
        var degrees = nodes.ToDictionary(node => node.Key, _ => 0, StringComparer.Ordinal);
        foreach (var relationship in relationships)
        {
            if (degrees.ContainsKey(relationship.SourceKey))
            {
                degrees[relationship.SourceKey]++;
            }

            if (relationship.TargetKey != relationship.SourceKey && degrees.ContainsKey(relationship.TargetKey))
            {
                degrees[relationship.TargetKey]++;
            }
        }

        // "Connected" counts only records that participate in at least one rendered
        // relationship, so an isolated selected entity never inflates the metrics.
        var connectedNodes = nodes
            .Where(node => degrees.TryGetValue(node.Key, out var degree) && degree > 0)
            .ToList();

        return new NetworkMetricsDto
        {
            ConnectionCount = relationships.Count,
            ConnectedPersonCount = connectedNodes.Count(node => node.EntityType == NetworkEntityType.Person),
            ConnectedCaseCount = connectedNodes.Count(node => node.EntityType == NetworkEntityType.Case),
            ConnectedLocationCount = connectedNodes.Count(node => node.EntityType == NetworkEntityType.Location),
            NodeDegrees = degrees
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new NetworkNodeMetricDto { NodeKey = pair.Key, Degree = pair.Value })
                .ToList()
        };
    }

    private static NetworkDetailDto BuildDetail(
        NetworkNodeDto selected,
        IReadOnlyList<NetworkNodeDto> nodes,
        IReadOnlyList<NetworkRelationshipDto> relationships)
    {
        var nodeByKey = nodes.ToDictionary(node => node.Key, StringComparer.Ordinal);
        var connectedKeys = relationships
            .Where(relationship => relationship.SourceKey == selected.Key || relationship.TargetKey == selected.Key)
            .Select(relationship => relationship.SourceKey == selected.Key
                ? relationship.TargetKey
                : relationship.SourceKey)
            .Where(key => key != selected.Key)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var connected = connectedKeys
            .Where(nodeByKey.ContainsKey)
            .Select(key => nodeByKey[key])
            .OrderBy(node => node.EntityType)
            .ThenBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Id)
            .ToList();

        return new NetworkDetailDto
        {
            Entity = selected,
            ConnectedEntities = connected,
            AssociatedCases = connected.Where(node => node.EntityType == NetworkEntityType.Case).ToList(),
            RelationshipSummary = relationships
                .Where(relationship => relationship.SourceKey == selected.Key || relationship.TargetKey == selected.Key)
                .GroupBy(relationship => relationship.RelationshipType)
                .OrderBy(group => group.Key)
                .Select(group => new NetworkRelationshipSummaryDto
                {
                    RelationshipType = group.Key,
                    Count = group.Count()
                })
                .ToList()
        };
    }

    private static bool IsCaseInRange(DateTime? incidentDate, DateTime? dateFrom, DateTime? dateTo) =>
        dateFrom is null || dateTo is null
        || (incidentDate is not null && incidentDate >= dateFrom && incidentDate < dateTo.Value.AddDays(1));

    private static string PersonName(string firstName, string? middleName, string lastName) =>
        string.Join(" ", new[] { firstName, middleName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string DisplayWords(string value) =>
        string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));

    private readonly record struct NodeKey(NetworkEntityType Type, Guid Id)
    {
        public static NodeKey? FromReference(Guid? personId, Guid? caseId, Guid? locationId)
        {
            var count = (personId is null ? 0 : 1) + (caseId is null ? 0 : 1) + (locationId is null ? 0 : 1);
            return count != 1
                ? null
                : personId is { } person
                    ? new NodeKey(NetworkEntityType.Person, person)
                    : caseId is { } crimeCase
                        ? new NodeKey(NetworkEntityType.Case, crimeCase)
                        : new NodeKey(NetworkEntityType.Location, locationId!.Value);
        }

        public override string ToString() => $"{Type}:{Id:D}";
    }

    private sealed class NodeKeyComparer : IComparer<NodeKey>
    {
        public static readonly NodeKeyComparer Instance = new();

        public int Compare(NodeKey x, NodeKey y)
        {
            var type = x.Type.CompareTo(y.Type);
            return type != 0 ? type : x.Id.CompareTo(y.Id);
        }
    }

    private sealed record RawRelationship(
        string Id,
        NodeKey Source,
        NodeKey Target,
        RelationshipType RelationshipType,
        string Label,
        DateTime OccurredAt,
        bool IsExplicitNetworkRelationship)
    {
        public NetworkRelationshipDto ToDto() => new()
        {
            Id = Id,
            SourceKey = Source.ToString(),
            TargetKey = Target.ToString(),
            RelationshipType = RelationshipType,
            Label = Label,
            OccurredAt = OccurredAt,
            IsExplicitNetworkRelationship = IsExplicitNetworkRelationship
        };
    }
}
