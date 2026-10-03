using System.Text.RegularExpressions;
using FluentAssertions;
using NETRA.Web.Services.Charts;
using NETRA.Web.Services.Maps;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// Tests for the Phase 6 server-side SVG renderers: geometry is deterministic and
/// correct, every user-derived string is HTML-encoded, and accessible metadata is
/// present. These renderers are the only place coordinates become pixels.
/// </summary>
public sealed class MapRendererTests
{
    private readonly SvgMapRenderer _renderer = new();
    private readonly SvgSparklineRenderer _sparklines = new();

    private static SvgMapModel Model(
        IReadOnlyList<SvgMapPoint>? points = null,
        IReadOnlyList<SvgMapHotspot>? hotspots = null,
        IReadOnlyList<SvgMapDistrictLabel>? districts = null) => new()
        {
            Title = "Crime incident map",
            Description = "A test map.",
            MinLatitude = 51,
            MaxLatitude = 52,
            MinLongitude = -1,
            MaxLongitude = 0,
            Points = points ?? [],
            Hotspots = hotspots ?? [],
            Districts = districts ?? []
        };

    private static string NormalizeIds(string svg) =>
        Regex.Replace(svg, "netra-map-\\d+", "netra-map-ID");

    // ------------------------------------------------------------ map renderer

    [Fact]
    public void Render_EmitsAccessibleSvgFrame()
    {
        var svg = _renderer.Render(Model()).Value!;

        svg.Should().StartWith("<svg class=\"netra-map\"");
        svg.Should().Contain("viewBox=\"0 0 1000 640\"");
        svg.Should().Contain("role=\"img\"");
        svg.Should().MatchRegex("aria-labelledby=\"netra-map-\\d+-t netra-map-\\d+-d\"");
        svg.Should().Contain("<title");
        svg.Should().Contain("A test map.");
        svg.Should().Contain("class=\"netra-map__bg\"");
        // Static schematic grid: 9 vertical + 7 horizontal lines.
        Regex.Matches(svg, "class=\"netra-map__grid\"").Should().HaveCount(16);
    }

    [Fact]
    public void Render_ProjectsCoordinatesEquirectangularly()
    {
        // minLon -> x=24 (pad), maxLon -> x=976; minLat -> y=616 (bottom), maxLat -> y=24.
        var points = new[]
        {
            new SvgMapPoint { Latitude = 51, Longitude = -1, SeverityClass = "high", Label = "SW" },
            new SvgMapPoint { Latitude = 52, Longitude = 0, SeverityClass = "critical", Label = "NE" }
        };

        var svg = _renderer.Render(Model(points: points)).Value;

        svg.Should().Contain("cx=\"24\" cy=\"616\"");
        svg.Should().Contain("cx=\"976\" cy=\"24\"");
        svg.Should().Contain("netra-map__point--high");
        svg.Should().Contain("netra-map__point--critical");
    }

    [Fact]
    public void Render_HotspotSizeGrowsWithCount_AndCarriesAccessibleTitle()
    {
        var hotspots = new[]
        {
            new SvgMapHotspot { Latitude = 51.5, Longitude = -0.5, Count = 4 },
            new SvgMapHotspot { Latitude = 51.6, Longitude = -0.4, Count = 8 }
        };

        var svg = _renderer.Render(Model(hotspots: hotspots)).Value;

        svg.Should().Contain("r=\"49\""); // ratio 0.5 -> 26 + 23
        svg.Should().Contain("r=\"72\""); // ratio 1.0 -> 26 + 46
        svg.Should().Contain("4 incidents in this hotspot");
        svg.Should().Contain("8 incidents in this hotspot");
    }

    [Fact]
    public void Render_DistrictLabelsAreDrawnAtTheirAnchors()
    {
        var districts = new[]
        {
            new SvgMapDistrictLabel { Latitude = 51.5, Longitude = -0.5, Name = "Central" }
        };

        var svg = _renderer.Render(Model(districts: districts)).Value;

        svg.Should().Contain("class=\"netra-map__district\"");
        svg.Should().Contain(">Central</text>");
    }

    [Fact]
    public void Render_EncodesEveryUserString()
    {
        var points = new[]
        {
            new SvgMapPoint
            {
                Latitude = 51.5,
                Longitude = -0.5,
                SeverityClass = "high",
                Label = "\"><script>alert(\"xss\")</script>"
            }
        };
        var districts = new[]
        {
            new SvgMapDistrictLabel { Latitude = 51.2, Longitude = -0.6, Name = "Tom & Jerry <St>" }
        };

        var svg = _renderer.Render(Model(points: points, districts: districts)).Value;

        svg.Should().NotContain("<script");
        svg.Should().Contain("&lt;script&gt;");
        svg.Should().Contain("Tom &amp; Jerry &lt;St&gt;");
    }

    [Fact]
    public void Render_SameModelProducesSameGeometry()
    {
        var model = Model(
            points:
            [
                new SvgMapPoint { Latitude = 51.4, Longitude = -0.6, SeverityClass = "low", Label = "A" },
                new SvgMapPoint { Latitude = 51.9, Longitude = -0.2, SeverityClass = "critical", Label = "B" }
            ],
            hotspots: [new SvgMapHotspot { Latitude = 51.5, Longitude = -0.5, Count = 5 }],
            districts: [new SvgMapDistrictLabel { Latitude = 51.7, Longitude = -0.4, Name = "North" }]);

        var first = NormalizeIds(_renderer.Render(model).Value!);
        var second = NormalizeIds(_renderer.Render(model).Value!);

        // Element ids rotate for uniqueness; the drawing itself is identical.
        second.Should().Be(first);
    }

    [Fact]
    public void Render_NoIncidents_StillValidFrameWithoutMarkers()
    {
        var svg = _renderer.Render(Model()).Value;

        svg.Should().Contain("</svg>");
        svg.Should().NotContain("netra-map__point");
        svg.Should().NotContain("netra-map__hotspot-group");
    }

    // ------------------------------------------------------------ sparkline renderer

    [Fact]
    public void Sparkline_ShortSeries_RendersNothingRatherThanAMisleadingTrend()
    {
        _sparklines.Render([], "Weekly case counts").Value.Should().BeEmpty();
        _sparklines.Render([4], "Weekly case counts").Value.Should().BeEmpty();
    }

    [Fact]
    public void Sparkline_TwoOrMorePoints_RendersAccessiblePolyline()
    {
        var svg = _sparklines.Render([2, 5, 3], "Weekly case counts").Value!;

        svg.Should().StartWith("<svg class=\"netra-sparkline\"");
        svg.Should().Contain("role=\"img\"");
        svg.Should().Contain("Weekly case counts: 2, 5, 3");
        // One vertex per week.
        Regex.Matches(svg, @"polyline points=""([^""]+)""").Should().ContainSingle()
            .Which.Groups[1].Value.Split(' ').Should().HaveCount(3);
    }

    [Fact]
    public void Sparkline_EncodesLabel()
    {
        var svg = _sparklines.Render([1, 2], "<script>x</script>").Value;

        svg.Should().NotContain("<script");
        svg.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void Sparkline_SameSeriesProducesSameShape()
    {
        var first = Regex.Replace(
            _sparklines.Render([1, 4, 2], "W").Value!, "netra-sparkline-\\d+", "ID");
        var second = Regex.Replace(
            _sparklines.Render([1, 4, 2], "W").Value!, "netra-sparkline-\\d+", "ID");

        second.Should().Be(first);
    }
}
