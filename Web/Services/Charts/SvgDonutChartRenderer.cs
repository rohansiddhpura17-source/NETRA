using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;

namespace NETRA.Web.Services.Charts;

public sealed class SvgDonutChartRenderer : IChartRenderer
{
    private static long _idCounter;

    public ChartKind Kind => ChartKind.Donut;

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
        var total = values.Sum();

        if (total <= 0)
        {
            return HtmlString.Empty;
        }

        const decimal radius = 72;
        var circumference = 2m * (decimal)Math.PI * radius;
        var offset = 0m;
        var id = $"netra-donut-chart-{Interlocked.Increment(ref _idCounter)}";
        var html = new StringBuilder();
        html.Append($"<svg class=\"netra-chart netra-chart--donut\" viewBox=\"0 0 520 260\" role=\"img\" aria-labelledby=\"{id}-title {id}-desc\">" +
                    $"<title id=\"{id}-title\">{E(model.Title)}</title>" +
                    $"<desc id=\"{id}-desc\">{E(model.Description)}</desc>" +
                    "<circle class=\"netra-chart__donut-track\" cx=\"130\" cy=\"126\" r=\"72\"/>");

        const decimal cx = 130;
        const decimal cy = 126;
        const decimal r = 72;
        for (var i = 0; i < values.Length; i++)
        {
            var segment = circumference * values[i] / total;
            var dash = F(segment);
            var gap = F(circumference - segment);
            var segClass = $"netra-chart__series-{i % 6}";
            var segId = $"{id}-seg-{i}";

            // visible segment (keeps original title)
            html.Append($"<circle class=\"netra-chart__donut-segment {segClass}\" cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"{F(r)}\" " +
                        $"stroke-dasharray=\"{dash} {gap}\" stroke-dashoffset=\"-{F(offset)}\">" +
                        $"<title>{E(categories[i])}: {F(values[i])}</title></circle>");

            // larger invisible stroke to act as a hit target and keyboard focus
            html.Append($"<circle class=\"netra-chart__hit {segClass}\" cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"{F(r)}\" " +
                        $"stroke-dasharray=\"{dash} {gap}\" stroke-dashoffset=\"-{F(offset)}\" stroke-width=\"24\" fill=\"none\" tabindex=\"0\" aria-describedby=\"{segId}-tooltip\"/>"
            );

            // calculate midpoint angle for tooltip placement
            var midOffset = offset + segment / 2m;
            var angle = (double)(midOffset / circumference) * Math.PI * 2.0 - Math.PI / 2.0;
            var tx = cx + (decimal)Math.Cos(angle) * (r + 22m);
            var ty = cy + (decimal)Math.Sin(angle) * (r + 22m);
            tx = Math.Max(8, Math.Min(512 - 8, tx));
            ty = Math.Max(8, Math.Min(260 - 8, ty));

            // tooltip group
            html.Append($"<g id=\"{segId}-tooltip\" class=\"netra-chart__tooltip {segClass}\" transform=\"translate({F(tx)},{F(ty)})\" role=\"group\">"
                        + $"<rect x=\"8\" y=\"-22\" width=\"140\" height=\"48\" rx=\"6\" class=\"netra-tooltip__panel\"/>"
                        + $"<rect x=\"10\" y=\"-20\" width=\"6\" height=\"44\" rx=\"2\" class=\"netra-tooltip__accent {segClass}\"/>"
                        + $"<text class=\"netra-tooltip__title\" x=\"24\" y=\"-4\">{E(categories[i])}</text>"
                        + $"<text class=\"netra-tooltip__line\" x=\"24\" y=\"14\">Cases: {F(values[i])}</text>"
                        + $"<text class=\"netra-tooltip__line netra-tooltip__percent\" x=\"24\" y=\"30\">Share: {F(values[i] * 100m / total)}%</text>"
                        + "</g>");

            offset += segment;
        }

        html.Append($"<text class=\"netra-chart__donut-value\" x=\"130\" y=\"122\" text-anchor=\"middle\">{F(total)}</text>" +
                    "<text class=\"netra-chart__donut-label\" x=\"130\" y=\"143\" text-anchor=\"middle\">TOTAL</text>");

        for (var i = 0; i < categories.Length; i++)
        {
            var y = 48 + i * 30;
            html.Append($"<rect class=\"netra-chart__legend-swatch netra-chart__series-{i % 6}\" x=\"264\" y=\"{y - 9}\" width=\"9\" height=\"9\" rx=\"2\"/>" +
                        $"<text class=\"netra-chart__legend-label\" x=\"282\" y=\"{y}\">{E(categories[i])}</text>" +
                        $"<text class=\"netra-chart__legend-value\" x=\"500\" y=\"{y}\" text-anchor=\"end\">{F(values[i])}</text>");
        }

        html.Append("</svg>");
        return new HtmlString(html.ToString());
    }

    private static string E(string value) => HtmlEncoder.Default.Encode(value);
    private static string F(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
