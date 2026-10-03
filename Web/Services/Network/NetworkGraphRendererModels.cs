using NETRA.Application.Network;

namespace NETRA.Web.Services.Network;

/// <summary>Pre-computed input for the server-side network SVG renderer.</summary>
public sealed class NetworkGraphRenderModel
{
    public string Title { get; init; } = "Network relationship graph";

    public string Description { get; init; } = string.Empty;

    public string SelectedNodeKey { get; init; } = string.Empty;

    public IReadOnlyList<NetworkGraphRenderNode> Nodes { get; init; } =
        Array.Empty<NetworkGraphRenderNode>();

    public IReadOnlyList<NetworkGraphRenderRelationship> Relationships { get; init; } =
        Array.Empty<NetworkGraphRenderRelationship>();
}

public sealed class NetworkGraphRenderNode
{
    public string Key { get; init; } = string.Empty;

    public NetworkEntityType EntityType { get; init; }

    public string Name { get; init; } = string.Empty;

    public string SecondaryLabel { get; init; } = string.Empty;

    public int Depth { get; init; }
}

public sealed class NetworkGraphRenderRelationship
{
    public string SourceKey { get; init; } = string.Empty;

    public string TargetKey { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;
}

/// <summary>
/// Renders a network graph as accessible, deterministic SVG. The renderer has no
/// data access and emits no browser behaviour; selecting an entity is a normal GET
/// round-trip from the accompanying Razor detail panel.
/// </summary>
public interface INetworkGraphRenderer
{
    Microsoft.AspNetCore.Html.HtmlString Render(NetworkGraphRenderModel model);
}
