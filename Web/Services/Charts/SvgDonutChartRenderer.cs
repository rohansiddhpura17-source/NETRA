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

        for (var i = 0; i < values.Length; i++)
        {
            var segment = circumference * values[i] / total;
            var percentage = values[i] / total * 100m;
            var pctString = percentage.ToString("0.0", CultureInfo.InvariantCulture) + "%";

            var midOffset = offset + segment / 2m;
            var frac = circumference > 0 ? (midOffset / circumference) : 0m;

            const decimal tw = 120;
            const decimal th = 52;
            decimal tx, ty;

            if (frac < 0.25m)
            {
                tx = 135;
                ty = 10;
            }
            else if (frac < 0.50m)
            {
                tx = 135;
                ty = 198;
            }
            else if (frac < 0.75m)
            {
                tx = 10;
                ty = 198;
            }
            else
            {
                tx = 10;
                ty = 10;
            }

            html.Append($"<g class=\"netra-chart__interactive netra-chart__interactive--donut\" tabindex=\"0\" role=\"graphics-symbol\" aria-label=\"{E(categories[i])}: {F(values[i])} ({pctString})\">" +
                        $"<circle class=\"netra-chart__donut-hit netra-chart__hit netra-chart__series-{i % 6}\" cx=\"130\" cy=\"126\" r=\"72\" " +
                        $"stroke-dasharray=\"{F(segment)} {F(circumference - segment)}\" stroke-dashoffset=\"-{F(offset)}\" stroke-width=\"24\" fill=\"none\"/>" +
                        $"<circle class=\"netra-chart__donut-segment netra-chart__series-{i % 6}\" cx=\"130\" cy=\"126\" r=\"72\" " +
                        $"stroke-dasharray=\"{F(segment)} {F(circumference - segment)}\" stroke-dashoffset=\"-{F(offset)}\">" +
                        $"<title>{E(categories[i])}: {F(values[i])} ({pctString})</title></circle>" +
                        $"<g class=\"netra-chart__tooltip netra-chart__series-{i % 6}\" aria-hidden=\"true\" pointer-events=\"none\">" +
                        $"<rect class=\"netra-chart__tooltip-bg netra-tooltip__panel\" x=\"{F(tx)}\" y=\"{F(ty)}\" width=\"{F(tw)}\" height=\"{F(th)}\" rx=\"4\"/>" +
                        $"<rect class=\"netra-chart__tooltip-accent netra-tooltip__accent netra-chart__series-{i % 6}\" x=\"{F(tx)}\" y=\"{F(ty)}\" width=\"3\" height=\"{F(th)}\" rx=\"1\"/>" +
                        $"<text class=\"netra-chart__tooltip-title netra-tooltip__title\" x=\"{F(tx + 10)}\" y=\"{F(ty + 16)}\">{E(categories[i])}</text>" +
                        $"<text class=\"netra-chart__tooltip-text netra-tooltip__line\" x=\"{F(tx + 10)}\" y=\"{F(ty + 30)}\"><tspan class=\"netra-chart__tooltip-label\">Cases: </tspan><tspan class=\"netra-chart__tooltip-value\">{F(values[i])}</tspan></text>" +
                        $"<text class=\"netra-chart__tooltip-text netra-tooltip__line netra-tooltip__percent\" x=\"{F(tx + 10)}\" y=\"{F(ty + 44)}\"><tspan class=\"netra-chart__tooltip-label\">Share: </tspan><tspan class=\"netra-chart__tooltip-value\">{pctString}</tspan></text>" +
                        "</g></g>");

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
