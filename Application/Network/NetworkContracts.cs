using NETRA.Domain.Enums;

namespace NETRA.Application.Network;

/// <summary>Real NETRA records that can be represented by a network node.</summary>
public enum NetworkEntityType
{
    Person = 1,
    Case = 2,
    Location = 3
}

public enum NetworkGraphStatus
{
    AwaitingSelection,
    SelectedEntityNotFound,
    FilteredOut,
    NoRelationships,
    Ready
}

/// <summary>Hard limits shared by the controller and network service.</summary>
public static class NetworkAnalysisLimits
{
    public const int DefaultTraversalDepth = 2;
    public const int MaxTraversalDepth = 3;
}

/// <summary>Validated input for one bounded server-side graph traversal.</summary>
public sealed class NetworkAnalysisQuery
{
    public NetworkEntityType? EntityType { get; init; }

    public Guid? EntityId { get; init; }

    public RelationshipType? RelationshipType { get; init; }

    public int Depth { get; init; } = 2;

    public DateTime? DateFrom { get; init; }

    public DateTime? DateTo { get; init; }
}

/// <summary>One safe, display-oriented entry in the entity selector.</summary>
public sealed class NetworkEntityLookupDto
{
    public Guid Id { get; init; }

    public NetworkEntityType EntityType { get; init; }

    public string Name { get; init; } = string.Empty;

    public string SecondaryLabel { get; init; } = string.Empty;
}

/// <summary>Everything required to render one network investigation result.</summary>
public sealed class NetworkGraphDto
{
    public NetworkGraphStatus Status { get; init; }

    public int Depth { get; init; }

    public bool IsResultLimited { get; init; }

    public NetworkNodeDto? SelectedNode { get; init; }

    public IReadOnlyList<NetworkNodeDto> Nodes { get; init; } = Array.Empty<NetworkNodeDto>();

    public IReadOnlyList<NetworkRelationshipDto> Relationships { get; init; } =
        Array.Empty<NetworkRelationshipDto>();

    public NetworkMetricsDto Metrics { get; init; } = new();

    public NetworkDetailDto? Detail { get; init; }
}

/// <summary>A graph node backed by one Person, CrimeCase or Location record.</summary>
public sealed class NetworkNodeDto
{
    /// <summary>Stable composite key, for example <c>Person:guid</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public Guid Id { get; init; }

    public NetworkEntityType EntityType { get; init; }

    public string Name { get; init; } = string.Empty;

    public string SecondaryLabel { get; init; } = string.Empty;

    /// <summary>Shortest discovered hop distance from the selected record.</summary>
    public int Depth { get; set; }

    /// <summary>Only non-sensitive metadata required by the investigation panel.</summary>
    public IReadOnlyList<NetworkMetadataDto> Metadata { get; init; } = Array.Empty<NetworkMetadataDto>();

    /// <summary>Set for case nodes so date filters use the stored incident date.</summary>
    public DateTime? CaseIncidentDate { get; init; }
}

public sealed class NetworkMetadataDto
{
    public string Label { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;
}

/// <summary>One relation projected from an existing NETRA association record.</summary>
public sealed class NetworkRelationshipDto
{
    public string Id { get; init; } = string.Empty;

    public string SourceKey { get; init; } = string.Empty;

    public string TargetKey { get; init; } = string.Empty;

    public RelationshipType RelationshipType { get; init; }

    public string Label { get; init; } = string.Empty;

    public DateTime OccurredAt { get; init; }

    /// <summary>True only for a stored <c>NetworkRelationship</c> record.</summary>
    public bool IsExplicitNetworkRelationship { get; init; }
}

/// <summary>Deterministic, descriptive graph metrics—not investigative conclusions.</summary>
public sealed class NetworkMetricsDto
{
    public int ConnectionCount { get; init; }

    public int ConnectedPersonCount { get; init; }

    public int ConnectedCaseCount { get; init; }

    public int ConnectedLocationCount { get; init; }

    public IReadOnlyList<NetworkNodeMetricDto> NodeDegrees { get; init; } =
        Array.Empty<NetworkNodeMetricDto>();
}

public sealed class NetworkNodeMetricDto
{
    public string NodeKey { get; init; } = string.Empty;

    public int Degree { get; init; }
}

public sealed class NetworkDetailDto
{
    public NetworkNodeDto Entity { get; init; } = null!;

    public IReadOnlyList<NetworkNodeDto> ConnectedEntities { get; init; } =
        Array.Empty<NetworkNodeDto>();

    public IReadOnlyList<NetworkRelationshipSummaryDto> RelationshipSummary { get; init; } =
        Array.Empty<NetworkRelationshipSummaryDto>();

    public IReadOnlyList<NetworkNodeDto> AssociatedCases { get; init; } =
        Array.Empty<NetworkNodeDto>();
}

public sealed class NetworkRelationshipSummaryDto
{
    public RelationshipType RelationshipType { get; init; }

    public int Count { get; init; }
}
