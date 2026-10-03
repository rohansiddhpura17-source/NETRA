using Microsoft.AspNetCore.Html;

namespace NETRA.Web.Services.Charts;

public interface IChartRenderer
{
    ChartKind Kind { get; }
    HtmlString Render(SvgChartModel model);
}
