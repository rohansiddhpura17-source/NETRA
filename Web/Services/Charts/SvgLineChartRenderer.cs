using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;

namespace NETRA.Web.Services.Charts;

public sealed class SvgLineChartRenderer : IChartRenderer
{
    private static long _idCounter;

    public ChartKind Kind => ChartKind.Line;

    public HtmlString Render(SvgChartModel model)
    {
        var sourceValues = model.Series.FirstOrDefault()?.Values ?? Array.Empty<decimal>();
        var pairCount = Math.Min(sourceValues.Count, model.Categories.Count);
        if (pairCount == 0)
        {
            return HtmlString.Empty;
        }

        var values = sourceValues.Take(pairCount).ToArray();
        var categories = model.Categories.Take(pairCount).ToArray();

        const decimal left = 46;
        const decimal top = 18;
        const decimal width = 656;
        const decimal height = 168;
        var max = Math.Max(1, values.Max());
        var denominator = Math.Max(1, values.Length - 1);
        var points = values.Select((value, index) => new
        {
            X = left + width * index / denominator,
            Y = top + height - (height * value / max)
        }).ToList();

        var path = string.Join(" ", points.Select((point, index) =>
            $"{(index == 0 ? "M" : "L")} {F(point.X)} {F(point.Y)}"));
        var areaPath = $"{path} L {F(points[^1].X)} {F(top + height)} L {F(points[0].X)} {F(top + height)} Z";
        var id = $"netra-line-chart-{Interlocked.Increment(ref _idCounter)}";
        var html = new StringBuilder();
        html.Append($"<svg class=\"netra-chart netra-chart--line\" viewBox=\"0 0 720 230\" role=\"img\" aria-labelledby=\"{id}-title {id}-desc\">" +
                    $"<title id=\"{id}-title\">{E(model.Title)}</title>" +
                    $"<desc id=\"{id}-desc\">{E(model.Description)}</desc>");

        for (var i = 0; i <= 4; i++)
        {
            var y = top + height * i / 4;
            var label = Math.Round(max * (4 - i) / 4, 0);
            html.Append($"<line class=\"netra-chart__grid\" x1=\"{F(left)}\" y1=\"{F(y)}\" x2=\"{F(left + width)}\" y2=\"{F(y)}\"/>" +
                        $"<text class=\"netra-chart__axis-label\" x=\"{F(left - 8)}\" y=\"{F(y + 4)}\" text-anchor=\"end\">{F(label)}</text>");
        }

        html.Append($"<path class=\"netra-chart__area\" d=\"{areaPath}\"/>" +
                    $"<path class=\"netra-chart__line\" d=\"{path}\"/>");

        for (var i = 0; i < points.Count; i++)
        {
            var px = points[i].X;
            var py = points[i].Y;
            var seriesName = model.Series.FirstOrDefault()?.Name ?? string.Empty;
            var pointId = $"{id}-point-{i}";
            var tooltipId = $"{pointId}-tooltip";

            // clamp tooltip position within viewBox (0..720, 0..230)
            var tx = Math.Max(6, Math.Min(714, px));
            var ty = Math.Max(6, Math.Min(224, py - 14));

            html.Append($"<g class=\"netra-chart__point-wrap\">"
                        // visible point (keeps original title for basic UA tooltips)
                        + $"<circle class=\"netra-chart__point\" cx=\"{F(px)}\" cy=\"{F(py)}\" r=\"3.5\" aria-hidden=\"true\"/>"
                        // larger invisible hit target for hover and keyboard focus
                        + $"<circle class=\"netra-chart__hit\" cx=\"{F(px)}\" cy=\"{F(py)}\" r=\"10\" tabindex=\"0\" aria-describedby=\"{tooltipId}\"/>"
                        // server-rendered tooltip group (hidden by default; shown on hover/focus via CSS)
                        + $"<g id=\"{tooltipId}\" class=\"netra-chart__tooltip netra-chart__series-0\" transform=\"translate({F(tx)},{F(ty)})\" role=\"group\">"
                        + $"<rect x=\"8\" y=\"-22\" width=\"140\" height=\"44\" rx=\"6\" class=\"netra-tooltip__panel\"/>"
                        + $"<rect x=\"10\" y=\"-20\" width=\"6\" height=\"40\" rx=\"2\" class=\"netra-tooltip__accent netra-chart__series-0\"/>"
                        + $"<text class=\"netra-tooltip__title\" x=\"24\" y=\"-6\">{E(categories[i])}</text>"
                        + $"<text class=\"netra-tooltip__line\" x=\"24\" y=\"10\">{E(seriesName)}: {F(values[i])}</text>"
                        + "</g>"
                        // keep axis label
                        + $"<text class=\"netra-chart__axis-label\" x=\"{F(px)}\" y=\"{F(top + height + 24)}\" text-anchor=\"middle\">{E(categories[i])}</text>"
                        + "</g>");
        }

        html.Append("</svg>");
        return new HtmlString(html.ToString());
    }

    private static string E(string value) => HtmlEncoder.Default.Encode(value);
    private static string F(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
