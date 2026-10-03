using System.Globalization;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Html;

namespace NETRA.Web.Services.Charts;

/// <summary>
/// Compact weekly-trend sparkline for the Area Risk Analysis table. Server-side
/// SVG only; the accessible summary lives in the element's aria-label because the
/// underlying counts are also printed in the adjacent table cells.
/// </summary>
public sealed class SvgSparklineRenderer
{
    private const double Width = 96;
    private const double Height = 28;

    private static long _idCounter;

    public HtmlString Render(IReadOnlyList<int> weeklyCounts, string label)
    {
        ArgumentNullException.ThrowIfNull(weeklyCounts);

        if (weeklyCounts.Count < 2)
        {
            return HtmlString.Empty;
        }

        var id = $"netra-sparkline-{Interlocked.Increment(ref _idCounter)}";
        var max = Math.Max(1, weeklyCounts.Max());
        var stepX = Width / (weeklyCounts.Count - 1);

        var points = new StringBuilder();
        for (var i = 0; i < weeklyCounts.Count; i++)
        {
            var x = i * stepX;
            var y = Height - 3 - (weeklyCounts[i] / (double)max * (Height - 6));
            points.Append(CultureInfo.InvariantCulture, $"{x:0.##},{y:0.##} ");
        }

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg class=\"netra-sparkline\" viewBox=\"0 0 {Width:0} {Height:0}\" width=\"{Width:0}\" " +
            $"height=\"{Height:0}\" role=\"img\" aria-labelledby=\"{id}\">");
        svg.Append(CultureInfo.InvariantCulture,
            $"<title id=\"{id}\">{Encode(label)}: {string.Join(", ", weeklyCounts)}</title>");
        svg.Append(CultureInfo.InvariantCulture,
            $"<polyline points=\"{points.ToString().Trim()}\" class=\"netra-sparkline__line\" />");
        svg.Append("</svg>");

        return new HtmlString(svg.ToString());
    }

    private static string Encode(string value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}
