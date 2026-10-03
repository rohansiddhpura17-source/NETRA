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
        var values = model.Series.FirstOrDefault()?.Values ?? Array.Empty<decimal>();
        var total = values.Sum();
        if (values.Count == 0 || total <= 0)
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

        for (var i = 0; i < values.Count; i++)
        {
            var segment = circumference * values[i] / total;
            html.Append($"<circle class=\"netra-chart__donut-segment netra-chart__series-{i % 6}\" cx=\"130\" cy=\"126\" r=\"72\" " +
                        $"stroke-dasharray=\"{F(segment)} {F(circumference - segment)}\" stroke-dashoffset=\"-{F(offset)}\">" +
                        $"<title>{E(model.Categories[i])}: {F(values[i])}</title></circle>");
            offset += segment;
        }

        html.Append($"<text class=\"netra-chart__donut-value\" x=\"130\" y=\"122\" text-anchor=\"middle\">{F(total)}</text>" +
                    "<text class=\"netra-chart__donut-label\" x=\"130\" y=\"143\" text-anchor=\"middle\">TOTAL</text>");

        for (var i = 0; i < model.Categories.Count; i++)
        {
            var y = 48 + i * 30;
            html.Append($"<rect class=\"netra-chart__legend-swatch netra-chart__series-{i % 6}\" x=\"264\" y=\"{y - 9}\" width=\"9\" height=\"9\" rx=\"2\"/>" +
                        $"<text class=\"netra-chart__legend-label\" x=\"282\" y=\"{y}\">{E(model.Categories[i])}</text>" +
                        $"<text class=\"netra-chart__legend-value\" x=\"500\" y=\"{y}\" text-anchor=\"end\">{F(values[i])}</text>");
        }

        html.Append("</svg>");
        return new HtmlString(html.ToString());
    }

    private static string E(string value) => HtmlEncoder.Default.Encode(value);
    private static string F(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
