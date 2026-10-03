using Microsoft.AspNetCore.Html;

namespace NETRA.Web.Services.Charts;

/// <summary>
/// Resolves the correct <see cref="IChartRenderer"/> per chart kind so Razor views
/// can emit SVG markup without holding rendering logic themselves.
/// </summary>
public sealed class ChartRendererRegistry
{
    private readonly IReadOnlyDictionary<ChartKind, IChartRenderer> _renderers;

    public ChartRendererRegistry(IEnumerable<IChartRenderer> renderers)
    {
        _renderers = renderers.ToDictionary(renderer => renderer.Kind);
    }

    public HtmlString Render(ChartKind kind, SvgChartModel model)
    {
        return _renderers[kind].Render(model);
    }
}
