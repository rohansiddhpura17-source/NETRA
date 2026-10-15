using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;

namespace NETRA.Web.Services.Charts;

public sealed class SvgBarChartRenderer : IChartRenderer
{
    private static long _idCounter;

    public ChartKind Kind => ChartKind.Bar;

    public HtmlString Render(SvgChartModel model)
    {
        if (model.Categories.Count == 0 || model.Series.Count == 0)
        {
            return HtmlString.Empty;
        }

        var totals = Enumerable.Range(0, model.Categories.Count)
            .Select(index => model.Series.Sum(series => index < series.Values.Count ? series.Values[index] : 0))
            .ToList();
        var max = Math.Max(1, totals.Max());
        var chartHeight = Math.Max(230, 86 + model.Categories.Count * 38);
        const decimal left = 132;
        const decimal width = 548;
        var id = $"netra-bar-chart-{Interlocked.Increment(ref _idCounter)}";
        var html = new StringBuilder();
        html.Append($"<svg class=\"netra-chart netra-chart--bar\" viewBox=\"0 0 720 {chartHeight}\" role=\"img\" aria-labelledby=\"{id}-title {id}-desc\">" +
                    $"<title id=\"{id}-title\">{E(model.Title)}</title>" +
                    $"<desc id=\"{id}-desc\">{E(model.Description)}</desc>");

        for (var categoryIndex = 0; categoryIndex < model.Categories.Count; categoryIndex++)
        {
            var category = model.Categories[categoryIndex];
            var y = 34 + categoryIndex * 38;
            var x = left;
            html.Append($"<text class=\"netra-chart__bar-label\" x=\"{F(left - 10)}\" y=\"{y + 12}\" text-anchor=\"end\">{E(category)}</text>" +
                        $"<rect class=\"netra-chart__bar-track\" x=\"{F(left)}\" y=\"{y}\" width=\"{F(width)}\" height=\"16\" rx=\"2\"/>");

            for (var seriesIndex = 0; seriesIndex < model.Series.Count; seriesIndex++)
            {
                var series = model.Series[seriesIndex];
                var value = categoryIndex < series.Values.Count ? series.Values[categoryIndex] : 0;
                var segmentWidth = width * value / max;
                if (segmentWidth <= 0)
                {
                    continue;
                }

                var hitWidth = Math.Max(segmentWidth, 14m);
                var hitX = x - (hitWidth - segmentWidth) / 2m;
                if (hitX < left) hitX = left;

                const decimal tw = 160;
                const decimal th = 52;
                var midX = x + segmentWidth / 2m;
                var tx = midX - tw / 2m;
                if (tx < 10m) tx = 10m;
                if (tx + tw > 710m) tx = 710m - tw;

                var ty = y - th - 6m;
                if (ty < 6m)
                {
                    ty = y + 22m;
                }
                if (ty + th > chartHeight - 8m)
                {
                    ty = chartHeight - th - 8m;
                }

                var isRisk = series.Name is "Critical" or "High" or "Medium" or "Low"
                    || model.Title.Contains("Risk", StringComparison.OrdinalIgnoreCase)
                    || model.Title.Contains("Severity", StringComparison.OrdinalIgnoreCase)
                    || model.Description.Contains("Risk", StringComparison.OrdinalIgnoreCase);

                var seriesLabel = isRisk ? $"Risk category: {E(series.Name)}" : E(series.Name);

                html.Append($"<g class=\"netra-chart__interactive netra-chart__interactive--bar\" tabindex=\"0\" role=\"graphics-symbol\" aria-label=\"{E(category)}, {E(series.Name)}: {F(value)}\">" +
                            $"<rect class=\"netra-chart__bar-hit netra-chart__hit\" x=\"{F(hitX)}\" y=\"{F(y - 4)}\" width=\"{F(hitWidth)}\" height=\"24\"/>" +
                            $"<rect class=\"netra-chart__bar netra-chart__series-{seriesIndex % 6}\" x=\"{F(x)}\" y=\"{y}\" width=\"{F(segmentWidth)}\" height=\"16\">" +
                            $"<title>{E(category)}, {E(series.Name)}: {F(value)}</title></rect>" +
                            $"<g class=\"netra-chart__tooltip netra-chart__series-{seriesIndex % 6}\" aria-hidden=\"true\" pointer-events=\"none\">" +
                            $"<rect class=\"netra-chart__tooltip-bg netra-tooltip__panel\" x=\"{F(tx)}\" y=\"{F(ty)}\" width=\"{F(tw)}\" height=\"{F(th)}\" rx=\"4\"/>" +
                            $"<rect class=\"netra-chart__tooltip-accent netra-tooltip__accent netra-chart__series-{seriesIndex % 6}\" x=\"{F(tx)}\" y=\"{F(ty)}\" width=\"3\" height=\"{F(th)}\" rx=\"1\"/>" +
                            $"<text class=\"netra-chart__tooltip-title netra-tooltip__title\" x=\"{F(tx + 10)}\" y=\"{F(ty + 16)}\">{E(category)}</text>" +
                            $"<text class=\"netra-chart__tooltip-text netra-tooltip__line\" x=\"{F(tx + 10)}\" y=\"{F(ty + 30)}\">{seriesLabel}</text>" +
                            $"<text class=\"netra-chart__tooltip-text netra-tooltip__line\" x=\"{F(tx + 10)}\" y=\"{F(ty + 44)}\">Count: {F(value)}</text>" +
                            "</g></g>");

                x += segmentWidth;
            }

            html.Append($"<text class=\"netra-chart__bar-value\" x=\"{F(left + width + 8)}\" y=\"{y + 12}\">{F(totals[categoryIndex])}</text>");
        }

        var legendY = chartHeight - 22;
        for (var i = 0; i < model.Series.Count; i++)
        {
            var x = left + i * 112;
            html.Append($"<rect class=\"netra-chart__legend-swatch netra-chart__series-{i % 6}\" x=\"{F(x)}\" y=\"{legendY - 9}\" width=\"9\" height=\"9\" rx=\"2\"/>" +
                        $"<text class=\"netra-chart__legend-label\" x=\"{F(x + 16)}\" y=\"{legendY}\">{E(model.Series[i].Name)}</text>");
        }

        html.Append("</svg>");
        return new HtmlString(html.ToString());
    }

    private static string E(string value) => HtmlEncoder.Default.Encode(value);
    private static string F(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
