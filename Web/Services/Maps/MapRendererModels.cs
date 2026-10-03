using Microsoft.AspNetCore.Html;

namespace NETRA.Web.Services.Maps;

/// <summary>A point to plot on the server-rendered map.</summary>
public sealed class SvgMapPoint
{
    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    /// <summary>critical / high / medium / low — selects the marker colour class.</summary>
    public required string SeverityClass { get; init; }

    /// <summary>Accessible tooltip text rendered inside the SVG title element.</summary>
    public required string Label { get; init; }
}

public sealed class SvgMapHotspot
{
    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public required int Count { get; init; }
}

public sealed class SvgMapDistrictLabel
{
    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public required string Name { get; init; }
}

/// <summary>Input model for <see cref="IMapRenderer"/>. All values are pre-computed
/// by the application service; the renderer only projects and draws.</summary>
public sealed class SvgMapModel
{
    public required string Title { get; init; }

    public required string Description { get; init; }

    public required double MinLatitude { get; init; }

    public required double MaxLatitude { get; init; }

    public required double MinLongitude { get; init; }

    public required double MaxLongitude { get; init; }

    public IReadOnlyList<SvgMapPoint> Points { get; init; } = Array.Empty<SvgMapPoint>();

    public IReadOnlyList<SvgMapHotspot> Hotspots { get; init; } = Array.Empty<SvgMapHotspot>();

    public IReadOnlyList<SvgMapDistrictLabel> Districts { get; init; } = Array.Empty<SvgMapDistrictLabel>();
}

/// <summary>
/// Renders the crime intelligence visualisation as server-generated SVG.
/// Implementations must be deterministic: the same model always produces the
/// same geometry (element ids aside).
/// </summary>
public interface IMapRenderer
{
    HtmlString Render(SvgMapModel model);
}
