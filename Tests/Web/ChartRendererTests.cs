using NETRA.Web.Services.Charts;

namespace NETRA.Tests.Web;

/// <summary>
/// Unit tests for the server-side SVG chart renderers: markup shape, accessibility
/// metadata, escaping and no-data behaviour. No JavaScript is expected anywhere.
/// </summary>
public class ChartRendererTests
{
    private static SvgChartModel SingleSeriesModel(string title = "Monthly cases") => new()
    {
        Title = title,
        Description = "A test chart.",
        Categories = ["Jan", "Feb", "Mar"],
        Series = [new SvgChartSeries { Name = "Cases", Values = [1m, 4m, 2m] }]
    };

    // ------------------------------------------------------------- line

    [Fact]
    public void LineChart_RendersAccessibleSvgWithPointsAndAxisLabels()
    {
        var svg = new SvgLineChartRenderer().Render(SingleSeriesModel()).ToString();

        svg.Should().StartWith("<svg");
        svg.Should().Contain("role=\"img\"");
        svg.Should().Contain("aria-labelledby");
        svg.Should().Contain(">Monthly cases</title>");
        svg.Should().Contain(">A test chart.</desc>");
        svg.Should().Contain("Jan");
        svg.Should().Contain("Feb");
        svg.Should().Contain("Mar");
        svg.Should().Contain("<path");
        svg.Should().Contain("<circle");
        svg.Should().NotContain("<script");
    }

    [Fact]
    public void LineChart_EscapesHtmlInTitlesAndCategories()
    {
        var model = new SvgChartModel
        {
            Title = "<script>alert(1)</script>",
            Description = "d",
            Categories = ["<img src=x>"],
            Series = [new SvgChartSeries { Name = "s", Values = [1m] }]
        };

        var svg = new SvgLineChartRenderer().Render(model).ToString();

        svg.Should().NotContain("<script>alert");
        svg.Should().Contain("&lt;script&gt;");
        svg.Should().Contain("&lt;img");
    }

    private static string FirstAriaId(string svg)
    {
        var marker = "aria-labelledby=\"";
        var start = svg.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return svg[start..svg.IndexOf('"', start)];
    }

    [Fact]
    public void LineChart_UsesUniqueAriaIdsAcrossRenders()
    {
        var renderer = new SvgLineChartRenderer();

        var first = FirstAriaId(renderer.Render(SingleSeriesModel()).ToString());
        var second = FirstAriaId(renderer.Render(SingleSeriesModel()).ToString());

        first.Should().NotBe(second);
    }

    [Fact]
    public void LineChart_WithoutData_RendersNothing()
    {
        var svg = new SvgLineChartRenderer().Render(new SvgChartModel
        {
            Title = "t",
            Description = "d",
            Categories = [],
            Series = []
        }).ToString();

        svg.Should().BeEmpty();
    }

    [Fact]
    public void LineChart_WhenValuesOutnumberCategories_UsesMatchingPairs()
    {
        var model = new SvgChartModel
        {
            Title = "t",
            Description = "d",
            Categories = ["Jan", "Feb"],
            Series = [new SvgChartSeries { Name = "Cases", Values = [1m, 4m, 2m] }]
        };

        var svg = new SvgLineChartRenderer().Render(model).ToString();

        svg.Should().Contain(">Jan</text>");
        svg.Should().Contain(">Feb</text>");
        svg.Should().NotContain(">Mar</text>");
        (svg.Split("<circle class=\"netra-chart__point\"", StringSplitOptions.None).Length - 1)
            .Should().Be(2);
    }

    [Fact]
    public void LineChart_WhenCategoriesOutnumberValues_UsesMatchingPairs()
    {
        var model = new SvgChartModel
        {
            Title = "t",
            Description = "d",
            Categories = ["Jan", "Feb", "Mar"],
            Series = [new SvgChartSeries { Name = "Cases", Values = [2m] }]
        };

        var svg = new SvgLineChartRenderer().Render(model).ToString();

        svg.Should().Contain(">Jan</text>");
        svg.Should().NotContain(">Feb</text>");
        svg.Should().NotContain(">Mar</text>");
    }

    // ------------------------------------------------------------- donut

    [Fact]
    public void DonutChart_RendersSegmentsCenterTotalAndLegend()
    {
        var svg = new SvgDonutChartRenderer().Render(SingleSeriesModel()).ToString();

        svg.Should().Contain("netra-chart--donut");
        svg.Should().Contain("stroke-dasharray");
        svg.Should().Contain(">7<"); // centre total
        svg.Should().Contain("TOTAL");
        svg.Should().Contain("netra-chart__legend-swatch");
        svg.Should().Contain("Jan");
        svg.Should().Contain("role=\"img\"");
    }

    [Fact]
    public void DonutChart_WithZeroTotal_RendersNothing()
    {
        var model = new SvgChartModel
        {
            Title = "t",
            Description = "d",
            Categories = ["Jan", "Feb"],
            Series = [new SvgChartSeries { Name = "Cases", Values = [0m, 0m] }]
        };

        new SvgDonutChartRenderer().Render(model).ToString().Should().BeEmpty();
    }

    [Fact]
    public void DonutChart_WhenValuesOutnumberCategories_UsesMatchingPairsAndTotal()
    {
        var model = new SvgChartModel
        {
            Title = "t",
            Description = "d",
            Categories = ["Jan", "Feb"],
            Series = [new SvgChartSeries { Name = "Cases", Values = [1m, 4m, 100m] }]
        };

        var svg = new SvgDonutChartRenderer().Render(model).ToString();

        svg.Should().Contain(">5</text>");
        svg.Should().Contain("Jan");
        svg.Should().Contain("Feb");
        svg.Should().NotContain("100");
    }

    [Fact]
    public void DonutChart_WhenCategoriesOutnumberValues_UsesMatchingPairsAndTotal()
    {
        var model = new SvgChartModel
        {
            Title = "t",
            Description = "d",
            Categories = ["Jan", "Feb", "Mar"],
            Series = [new SvgChartSeries { Name = "Cases", Values = [2m, 3m] }]
        };

        var svg = new SvgDonutChartRenderer().Render(model).ToString();

        svg.Should().Contain(">5</text>");
        svg.Should().Contain("Jan");
        svg.Should().Contain("Feb");
        svg.Should().NotContain("Mar");
    }

    [Fact]
    public void DonutChart_EscapesHostileTitlesDescriptionsCategoriesAndSeriesNames()
    {
        var model = new SvgChartModel
        {
            Title = "<script>alert(1)</script>",
            Description = "<img src=x onerror=\"alert(2)\">",
            Categories = ["<script>category</script>"],
            Series = [new SvgChartSeries
            {
                Name = "<script>series</script>",
                Values = [1m]
            }]
        };

        var svg = new SvgDonutChartRenderer().Render(model).ToString();

        svg.Should().NotContain("<script>");
        svg.Should().NotContain("<img");
        svg.Should().Contain("&lt;script&gt;");
        svg.Should().Contain("&lt;img");
    }

    // ------------------------------------------------------------- bar

    [Fact]
    public void BarChart_RendersStackedSeriesWithTooltipsAndLegend()
    {
        var model = new SvgChartModel
        {
            Title = "Severity by district",
            Description = "d",
            Categories = ["Central", "North"],
            Series =
            [
                new SvgChartSeries { Name = "High", Values = [3m, 1m] },
                new SvgChartSeries { Name = "Critical", Values = [1m, 0m] }
            ]
        };

        var svg = new SvgBarChartRenderer().Render(model).ToString();

        svg.Should().Contain("netra-chart--bar");
        svg.Should().Contain("Central, High: 3");
        svg.Should().Contain("Critical");
        svg.Should().Contain(">4<"); // row total for Central
        svg.Should().Contain("netra-chart__bar-track");
        svg.Should().NotContain("<script");
    }

    [Fact]
    public void BarChart_WithoutCategories_RendersNothing()
    {
        var model = new SvgChartModel
        {
            Title = "t",
            Description = "d",
            Categories = [],
            Series = [new SvgChartSeries { Name = "s", Values = [] }]
        };

        new SvgBarChartRenderer().Render(model).ToString().Should().BeEmpty();
    }

    // ------------------------------------------------------------- registry

    [Fact]
    public void Registry_ResolvesEachRendererByKind()
    {
        var registry = new ChartRendererRegistry(
            new IChartRenderer[]
            {
                new SvgLineChartRenderer(),
                new SvgBarChartRenderer(),
                new SvgDonutChartRenderer()
            });

        registry.Render(ChartKind.Line, SingleSeriesModel()).ToString().Should().Contain("netra-chart--line");
        registry.Render(ChartKind.Donut, SingleSeriesModel()).ToString().Should().Contain("netra-chart--donut");
        registry.Render(ChartKind.Bar, SingleSeriesModel()).ToString().Should().Contain("netra-chart--bar");
    }
}
