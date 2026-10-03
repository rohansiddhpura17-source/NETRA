using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Network;
using NETRA.Domain.Enums;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.Network;

namespace NETRA.Web.Controllers;

/// <summary>
/// Network &amp; Link Analysis is intentionally a GET-only, server-round-trip
/// investigation surface. The controller validates request shape then delegates all
/// relational retrieval and traversal to <see cref="INetworkAnalysisService"/>.
/// </summary>
[Authorize(Policy = NetraPolicies.NetworkRead)]
public sealed class NetworkController : Controller
{
    private readonly INetworkAnalysisService _network;

    public NetworkController(INetworkAnalysisService network)
    {
        _network = network;
    }

    [HttpGet("/network")]
    public async Task<IActionResult> Index(NetworkFilterViewModel filter, CancellationToken cancellationToken)
    {
        NetworkEntityType? entityType = null;
        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            if (Enum.TryParse<NetworkEntityType>(filter.EntityType, true, out var parsedType)
                && Enum.IsDefined(parsedType))
            {
                entityType = parsedType;
            }
            else
            {
                ModelState.AddModelError(nameof(filter.EntityType),
                    "Choose Person, Case, or Location as the starting entity type.");
            }
        }

        if (filter.EntityId is not null && entityType is null)
        {
            ModelState.AddModelError(nameof(filter.EntityType),
                "Choose an entity type for the selected starting entity.");
        }

        if (filter.RelationshipType is { } relationshipType && !Enum.IsDefined(relationshipType))
        {
            ModelState.AddModelError(nameof(filter.RelationshipType), "Choose a valid relationship type.");
            filter.RelationshipType = null;
        }

        var depth = filter.Depth ?? NetworkAnalysisLimits.DefaultTraversalDepth;
        if (depth < 1)
        {
            ModelState.AddModelError(nameof(filter.Depth), "Traversal depth must be at least 1.");
            depth = NetworkAnalysisLimits.DefaultTraversalDepth;
        }
        else if (depth > NetworkAnalysisLimits.MaxTraversalDepth)
        {
            ModelState.AddModelError(nameof(filter.Depth),
                $"Traversal depth is limited to {NetworkAnalysisLimits.MaxTraversalDepth}.");
            depth = NetworkAnalysisLimits.MaxTraversalDepth;
        }

        // Render the effective safe value after validation instead of preserving an
        // unbounded request in the selector.
        filter.Depth = depth;

        var dateFrom = filter.DateFrom?.Date;
        var dateTo = filter.DateTo?.Date;
        if (dateFrom is not null && dateTo is not null && dateFrom > dateTo)
        {
            ModelState.AddModelError(nameof(filter.DateTo),
                "The start date must be on or before the end date.");
            dateFrom = null;
            dateTo = null;
        }

        var graph = await _network.GetGraphAsync(new NetworkAnalysisQuery
        {
            EntityType = entityType,
            EntityId = filter.EntityId,
            RelationshipType = filter.RelationshipType,
            Depth = depth,
            DateFrom = dateFrom,
            DateTo = dateTo
        }, cancellationToken);
        var startingEntities = await _network.SearchEntitiesAsync(entityType, filter.Search, cancellationToken);

        return View(NetworkViewModel.Create(graph, filter, startingEntities));
    }
}
