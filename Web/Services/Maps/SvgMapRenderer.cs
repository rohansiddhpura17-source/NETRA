using System.Globalization;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Html;

namespace NETRA.Web.Services.Maps;

/// <summary>
/// Server-generated SVG crime map. No JavaScript and no external tile provider:
/// the background is a deterministic, clearly schematic geographic frame (grid +
/// district context) onto which real coordinates are projected equirectangularly.
/// The same input always renders the same geometry.
/// </summary>
public sealed class SvgMapRenderer : IMapRenderer
{
    private const double Width = 1000;
    private const double Height = 640;
    private const double Pad = 24;

    private static long _idCounter;

    public HtmlString Render(SvgMapModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var id = $"netra-map-{Interlocked.Increment(ref _idCounter)}";
        var spanLat = Math.Max(1e-6, model.MaxLatitude - model.MinLatitude);
        var spanLon = Math.Max(1e-6, model.MaxLongitude - model.MinLongitude);
        var drawWidth = Width - (Pad * 2);
        var drawHeight = Height - (Pad * 2);

        double X(double lon) => Pad + ((lon - model.MinLongitude) / spanLon * drawWidth);
        double Y(double lat) => Height - Pad - ((lat - model.MinLatitude) / spanLat * drawHeight);

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg class=\"netra-map\" viewBox=\"0 0 {Width:0} {Height:0}\" role=\"img\" " +
            $"aria-labelledby=\"{id}-t {id}-d\" preserveAspectRatio=\"xMidYMid meet\">");
        svg.Append(CultureInfo.InvariantCulture,
            $"<title id=\"{id}-t\">{Encode(model.Title)}</title>");
        svg.Append(CultureInfo.InvariantCulture,
            $"<desc id=\"{id}-d\">{Encode(model.Description)}</desc>");

        // Dark geographic frame.
        svg.Append(CultureInfo.InvariantCulture,
            $"<rect x=\"0\" y=\"0\" width=\"{Width:0}\" height=\"{Height:0}\" class=\"netra-map__bg\" />");

        // Deterministic schematic grid (static background geometry, not a real basemap).
        for (var i = 1; i < 10; i++)
        {
            var gx = Pad + (drawWidth * i / 10.0);
            svg.Append(CultureInfo.InvariantCulture,
                $"<line x1=\"{gx:0.##}\" y1=\"{Pad:0}\" x2=\"{gx:0.##}\" y2=\"{Height - Pad:0}\" class=\"netra-map__grid\" />");
        }

        for (var j = 1; j < 8; j++)
        {
            var gy = Pad + (drawHeight * j / 8.0);
            svg.Append(CultureInfo.InvariantCulture,
                $"<line x1=\"{Pad:0}\" y1=\"{gy:0.##}\" x2=\"{Width - Pad:0}\" y2=\"{gy:0.##}\" class=\"netra-map__grid\" />");
        }

        // Hotspot density halos (larger and more opaque with count).
        var maxHotspot = model.Hotspots.Count > 0 ? Math.Max(1, model.Hotspots.Max(h => h.Count)) : 1;
        foreach (var hotspot in model.Hotspots)
        {
            var ratio = hotspot.Count / (double)maxHotspot;
            var radius = 26 + (ratio * 46);
            var opacity = 0.18 + (ratio * 0.30);
            svg.Append(CultureInfo.InvariantCulture,
                $"<g class=\"netra-map__hotspot-group\">" +
                $"<circle cx=\"{X(hotspot.Longitude):0.##}\" cy=\"{Y(hotspot.Latitude):0.##}\" " +
                $"r=\"{radius:0.##}\" class=\"netra-map__hotspot\" fill-opacity=\"{opacity:0.###}\" />" +
                $"<title>{Encode(hotspot.Count + " incidents in this hotspot")}</title></g>");
        }

        // District context labels.
        foreach (var district in model.Districts)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{X(district.Longitude):0.##}\" y=\"{Y(district.Latitude) - 14:0.##}\" " +
                $"class=\"netra-map__district\" text-anchor=\"middle\">{Encode(district.Name)}</text>");
        }

        // Incident markers, severity-coded.
        foreach (var point in model.Points)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"<g class=\"netra-map__point netra-map__point--{Encode(point.SeverityClass)}\">" +
                $"<circle cx=\"{X(point.Longitude):0.##}\" cy=\"{Y(point.Latitude):0.##}\" r=\"5.5\" />" +
                $"<title>{Encode(point.Label)}</title></g>");
        }

        svg.Append("</svg>");
        return new HtmlString(svg.ToString());
    }

    private static string Encode(string value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}
