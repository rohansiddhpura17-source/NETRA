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
            var y = 34 + categoryIndex * 38;
            var x = left;
            html.Append($"<text class=\"netra-chart__bar-label\" x=\"{F(left - 10)}\" y=\"{y + 12}\" text-anchor=\"end\">{E(model.Categories[categoryIndex])}</text>" +
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

                html.Append($"<rect class=\"netra-chart__bar netra-chart__series-{seriesIndex % 6}\" x=\"{F(x)}\" y=\"{y}\" width=\"{F(segmentWidth)}\" height=\"16\">" +
                            $"<title>{E(model.Categories[categoryIndex])}, {E(series.Name)}: {F(value)}</title></rect>");
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
