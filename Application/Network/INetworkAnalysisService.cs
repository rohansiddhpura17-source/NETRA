namespace NETRA.Application.Network;

/// <summary>
/// Reads the bounded, authorised network surrounding a person, case or location.
/// The implementation projects existing relational records; it never creates a
/// second graph data store.
/// </summary>
public interface INetworkAnalysisService
{
    Task<NetworkGraphDto> GetGraphAsync(
        NetworkAnalysisQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a deliberately bounded set of active entities for the starting-node
    /// selector. Supplying a term narrows the search before rows are materialised.
    /// </summary>
    Task<IReadOnlyList<NetworkEntityLookupDto>> SearchEntitiesAsync(
        NetworkEntityType? entityType,
        string? searchTerm,
        CancellationToken cancellationToken = default);
}
