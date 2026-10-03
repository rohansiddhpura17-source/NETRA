using System.Globalization;
using NETRA.Application.Network;
using NETRA.Web.Models.Navigation;
using NETRA.Web.Models.Ui;
using NETRA.Web.Services.Network;

namespace NETRA.Web.Models.ViewModels.Network;

/// <summary>
/// Display model for Network &amp; Link Analysis. It formats DTOs from the service and
/// constructs renderer input; it does not expose EF entities to the Razor view.
/// </summary>
public sealed class NetworkViewModel
{
    public required NetworkGraphDto Data { get; init; }

    public required NetworkFilterViewModel Filter { get; init; }

    public IReadOnlyList<NetworkEntityLookupDto> StartingEntities { get; init; } =
        Array.Empty<NetworkEntityLookupDto>();

    public NetworkGraphRenderModel? Graph { get; init; }

    public IReadOnlyList<StatCardModel> SummaryCards { get; init; } = Array.Empty<StatCardModel>();

    public string StatusText => Data.Status switch
    {
        NetworkGraphStatus.AwaitingSelection => "Awaiting starting entity",
        NetworkGraphStatus.SelectedEntityNotFound => "Selected entity not found",
        NetworkGraphStatus.FilteredOut => "Selected case is outside the selected period",
        NetworkGraphStatus.NoRelationships => "No matching relationships",
        _ => $"Depth {Data.Depth} investigation"
    };

    public static NetworkViewModel Create(
        NetworkGraphDto data,
        NetworkFilterViewModel filter,
        IReadOnlyList<NetworkEntityLookupDto> startingEntities)
    {
        NetworkGraphRenderModel? graph = null;
        if (data.Status == NetworkGraphStatus.Ready && data.SelectedNode is not null)
        {
            graph = new NetworkGraphRenderModel
            {
                Title = "Network relationship graph",
                Description =
                    $"Server-rendered relationship graph centred on {data.SelectedNode.Name}. " +
                    $"{data.Nodes.Count} nodes and {data.Relationships.Count} connections are shown to depth {data.Depth}. " +
                    "Person nodes are circular, case nodes are rectangular, and location nodes are diamond-shaped.",
                SelectedNodeKey = data.SelectedNode.Key,
                Nodes = data.Nodes.Select(node => new NetworkGraphRenderNode
                {
                    Key = node.Key,
                    EntityType = node.EntityType,
                    Name = node.Name,
                    SecondaryLabel = node.SecondaryLabel,
                    Depth = node.Depth
                }).ToList(),
                Relationships = data.Relationships.Select(relationship => new NetworkGraphRenderRelationship
                {
                    SourceKey = relationship.SourceKey,
                    TargetKey = relationship.TargetKey,
                    Label = NetraDisplay.Words(relationship.RelationshipType.ToString())
                }).ToList()
            };
        }

        return new NetworkViewModel
        {
            Data = data,
            Filter = filter,
            StartingEntities = startingEntities,
            Graph = graph,
            SummaryCards =
            [
                new StatCardModel
                {
                    Label = "Connections",
                    Value = data.Metrics.ConnectionCount.ToString(CultureInfo.InvariantCulture),
                    Meta = "Relationship records in view",
                    Tone = NetraTone.Primary,
                    Icon = NetraIcon.Network
                },
                new StatCardModel
                {
                    Label = "People",
                    Value = data.Metrics.ConnectedPersonCount.ToString(CultureInfo.InvariantCulture),
                    Meta = "Connected person records",
                    Tone = NetraTone.Info,
                    Icon = NetraIcon.Persons
                },
                new StatCardModel
                {
                    Label = "Cases",
                    Value = data.Metrics.ConnectedCaseCount.ToString(CultureInfo.InvariantCulture),
                    Meta = "Connected case records",
                    Tone = NetraTone.Warning,
                    Icon = NetraIcon.Cases
                },
                new StatCardModel
                {
                    Label = "Locations",
                    Value = data.Metrics.ConnectedLocationCount.ToString(CultureInfo.InvariantCulture),
                    Meta = "Connected location records",
                    Tone = NetraTone.Success,
                    Icon = NetraIcon.Map
                }
            ]
        };
    }
}
