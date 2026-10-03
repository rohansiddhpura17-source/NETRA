namespace NETRA.Web.Services.Charts;

public enum ChartKind
{
    Line,
    Bar,
    Donut
}

public sealed class SvgChartModel
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SvgChartSeries> Series { get; init; } = Array.Empty<SvgChartSeries>();
}

public sealed class SvgChartSeries
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<decimal> Values { get; init; } = Array.Empty<decimal>();
}
