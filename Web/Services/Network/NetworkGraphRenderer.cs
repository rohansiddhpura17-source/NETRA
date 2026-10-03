using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Html;
using NETRA.Application.Network;

namespace NETRA.Web.Services.Network;

/// <summary>
/// Deterministic concentric-ring layout for a bounded server-rendered network.
/// The selected record is at the centre. Nodes at hop 1, 2 and 3 occupy evenly
/// spaced elliptical rings in stable type/name/key order. This keeps repeated
/// renders geometrically identical and avoids random force-layout movement.
/// </summary>
public sealed class NetworkGraphRenderer : INetworkGraphRenderer
{
    private const double Width = 1000;
    private const double Height = 650;
    private const double CenterX = Width / 2;
    private const double CenterY = Height / 2;

    public HtmlString Render(NetworkGraphRenderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var orderedNodes = model.Nodes
            .OrderBy(node => node.Depth)
            .ThenBy(node => node.EntityType)
            .ThenBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Key, StringComparer.Ordinal)
            .ToList();
        var positions = PositionNodes(orderedNodes, model.SelectedNodeKey);
        var svg = new StringBuilder();

        svg.Append(FormattableString.Invariant(
            $"<svg class=\"netra-network\" viewBox=\"0 0 {Width:0} {Height:0}\" role=\"img\" aria-labelledby=\"netra-network-title netra-network-desc\" preserveAspectRatio=\"xMidYMid meet\">"));
        svg.Append($"<title id=\"netra-network-title\">{Encode(model.Title)}</title>");
        svg.Append($"<desc id=\"netra-network-desc\">{Encode(model.Description)}</desc>");
        svg.Append($"<rect class=\"netra-network__bg\" width=\"{Width:0}\" height=\"{Height:0}\" />");
        RenderBackground(svg);
        RenderLegend(svg);

        foreach (var relationship in model.Relationships
                     .OrderBy(item => item.SourceKey, StringComparer.Ordinal)
                     .ThenBy(item => item.TargetKey, StringComparer.Ordinal)
                     .ThenBy(item => item.Label, StringComparer.Ordinal))
        {
            if (!positions.TryGetValue(relationship.SourceKey, out var source)
                || !positions.TryGetValue(relationship.TargetKey, out var target))
            {
                continue;
            }

            svg.Append(FormattableString.Invariant(
                $"<g class=\"netra-network__relationship\"><line x1=\"{source.X:0.##}\" y1=\"{source.Y:0.##}\" x2=\"{target.X:0.##}\" y2=\"{target.Y:0.##}\" /><title>{Encode(relationship.Label)}</title></g>"));

            // Dense labels become less legible than the accessible SVG title. Show
            // relationship labels only while they can remain spatially useful.
            if (model.Relationships.Count <= 16)
            {
                var labelX = (source.X + target.X) / 2;
                var labelY = (source.Y + target.Y) / 2 - 5;
                svg.Append(FormattableString.Invariant(
                    $"<text class=\"netra-network__relationship-label\" x=\"{labelX:0.##}\" y=\"{labelY:0.##}\" text-anchor=\"middle\">{Encode(Truncate(relationship.Label, 22))}</text>"));
            }
        }

        foreach (var node in orderedNodes)
        {
            var point = positions[node.Key];
            var typeClass = node.EntityType.ToString().ToLowerInvariant();
            var selectedClass = node.Key == model.SelectedNodeKey ? " netra-network__node--selected" : string.Empty;
            svg.Append(FormattableString.Invariant(
                $"<g class=\"netra-network__node netra-network__node--{typeClass}{selectedClass}\" transform=\"translate({point.X:0.##} {point.Y:0.##})\">"));

            switch (node.EntityType)
            {
                case NetworkEntityType.Case:
                    svg.Append("<rect x=\"-34\" y=\"-18\" width=\"68\" height=\"36\" rx=\"7\" />");
                    break;
                case NetworkEntityType.Location:
                    svg.Append("<path d=\"M0 -23 23 0 0 23 -23 0Z\" />");
                    break;
                default:
                    svg.Append("<circle r=\"20\" />");
                    break;
            }

            svg.Append($"<title>{Encode(node.Name)} — {Encode(node.SecondaryLabel)}</title>");
            svg.Append($"<text class=\"netra-network__node-label\" y=\"37\" text-anchor=\"middle\">{Encode(Truncate(node.Name, 23))}</text>");
            svg.Append($"<text class=\"netra-network__node-subtitle\" y=\"52\" text-anchor=\"middle\">{Encode(Truncate(node.SecondaryLabel, 26))}</text>");
            svg.Append("</g>");
        }

        svg.Append("</svg>");
        return new HtmlString(svg.ToString());
    }

    private static Dictionary<string, Point> PositionNodes(
        IReadOnlyList<NetworkGraphRenderNode> nodes,
        string selectedNodeKey)
    {
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        var selected = nodes.FirstOrDefault(node => node.Key == selectedNodeKey) ?? nodes.FirstOrDefault();
        if (selected is not null)
        {
            positions[selected.Key] = new Point(CenterX, CenterY);
        }

        foreach (var layer in nodes.Where(node => node.Key != selected?.Key)
                     .GroupBy(node => Math.Max(1, node.Depth))
                     .OrderBy(group => group.Key))
        {
            var layerNodes = layer.ToList();
            var radiusX = Math.Min(430, 150 + ((layer.Key - 1) * 116));
            var radiusY = Math.Min(250, 100 + ((layer.Key - 1) * 75));

            for (var index = 0; index < layerNodes.Count; index++)
            {
                // Each ring is rotated a quarter turn per hop so sparse rings (one or
                // two nodes) never stack on the same axis and collide with the ring
                // inside them. The rotation is a pure function of depth, so the
                // layout stays fully deterministic.
                var angle = (-Math.PI / 2)
                    + ((layer.Key - 1) * Math.PI / 2)
                    + (2 * Math.PI * index / layerNodes.Count);
                positions[layerNodes[index].Key] = new Point(
                    CenterX + (Math.Cos(angle) * radiusX),
                    CenterY + (Math.Sin(angle) * radiusY));
            }
        }

        return positions;
    }

    private static void RenderBackground(StringBuilder svg)
    {
        for (var x = 40; x < Width; x += 40)
        {
            svg.Append(FormattableString.Invariant(
                $"<line class=\"netra-network__grid\" x1=\"{x}\" y1=\"0\" x2=\"{x}\" y2=\"{Height:0}\" />"));
        }

        for (var y = 40; y < Height; y += 40)
        {
            svg.Append(FormattableString.Invariant(
                $"<line class=\"netra-network__grid\" x1=\"0\" y1=\"{y}\" x2=\"{Width:0}\" y2=\"{y}\" />"));
        }
    }

    private static void RenderLegend(StringBuilder svg)
    {
        svg.Append("<g class=\"netra-network__legend\" transform=\"translate(24 24)\">");
        svg.Append("<text x=\"0\" y=\"0\">NODE TYPE</text>");
        svg.Append("<circle class=\"netra-network__legend-person\" cx=\"8\" cy=\"22\" r=\"6\" /><text x=\"20\" y=\"26\">Person</text>");
        svg.Append("<rect class=\"netra-network__legend-case\" x=\"84\" y=\"16\" width=\"14\" height=\"12\" rx=\"2\" /><text x=\"105\" y=\"26\">Case</text>");
        svg.Append("<path class=\"netra-network__legend-location\" d=\"M180 15 187 22 180 29 173 22Z\" /><text x=\"194\" y=\"26\">Location</text>");
        svg.Append("</g>");
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..Math.Max(1, maxLength - 1)] + "…";

    private static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private readonly record struct Point(double X, double Y);
}
