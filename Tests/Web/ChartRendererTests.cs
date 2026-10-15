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
        svg.Should().Contain("netra-chart__hit");
        svg.Should().Contain("netra-chart__tooltip");
        svg.Should().Contain("Cases: 1");
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
        svg.Should().Contain("netra-chart__hit");
        svg.Should().Contain("Share:");
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
        svg.Should().Contain("netra-chart__hit");
        svg.Should().Contain("Risk category: Critical");
        svg.Should().Contain("Count: 1");
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

    // ------------------------------------------------------------- tooltips & interactions

    [Fact]
    public void LineChart_RendersInteractiveTooltips_WithCategorySeriesAndValue()
    {
        var model = new SvgChartModel
        {
            Title = "Crime Trend Analysis",
            Description = "Monthly trend",
            Categories = ["Jan 2026", "Feb 2026", "Mar 2026"],
            Series = [new SvgChartSeries { Name = "Recorded cases", Values = [3m, 7m, 12m] }]
        };

        var svg = new SvgLineChartRenderer().Render(model).ToString();

        // 1. Interactive group with accessibility attributes
        svg.Should().Contain("class=\"netra-chart__interactive netra-chart__interactive--point\"");
        svg.Should().Contain("tabindex=\"0\"");
        svg.Should().Contain("role=\"graphics-symbol\"");
        svg.Should().Contain("aria-label=\"Jan 2026, Recorded cases: 3\"");

        // 2. Larger hover target hit circle
        svg.Should().Contain("netra-chart__point-hit");
        svg.Should().Contain("r=\"16\"");

        // 3. Tooltip card structure
        svg.Should().Contain("netra-chart__tooltip");
        svg.Should().Contain("netra-chart__tooltip-bg");
        svg.Should().Contain("netra-chart__tooltip-accent");
        svg.Should().Contain(">Jan 2026</text>");
        svg.Should().Contain(">Recorded cases: </tspan>");
        svg.Should().Contain(">3</tspan>");

        // 4. Zero JavaScript
        svg.Should().NotContain("<script");
        svg.Should().NotContain("javascript:");
        svg.Should().NotContain("onclick");
    }

    [Fact]
    public void DonutChart_RendersIndependentSliceTooltips_WithCountAndShare()
    {
        var model = new SvgChartModel
        {
            Title = "Distribution by Type",
            Description = "Crime distribution",
            Categories = ["Fraud", "Assault", "Theft"],
            Series = [new SvgChartSeries { Name = "Cases", Values = [6m, 4m, 3m] }] // Total: 13, Fraud share: 6/13 = 46.2%
        };

        var svg = new SvgDonutChartRenderer().Render(model).ToString();

        // 1. Independent interactive slice groups
        svg.Should().Contain("class=\"netra-chart__interactive netra-chart__interactive--donut\"");
        svg.Should().Contain("tabindex=\"0\"");
        svg.Should().Contain("role=\"graphics-symbol\"");

        // 2. Calculated share percentage for Fraud (46.2%)
        svg.Should().Contain(">Fraud</text>");
        svg.Should().Contain(">Cases: </tspan><tspan class=\"netra-chart__tooltip-value\">6</tspan>");
        svg.Should().Contain(">Share: </tspan><tspan class=\"netra-chart__tooltip-value\">46.2%</tspan>");

        // 3. Center total and legend preserved
        svg.Should().Contain(">13</text>");
        svg.Should().Contain("TOTAL");
        svg.Should().Contain("netra-chart__legend-swatch");
    }

    [Fact]
    public void BarChart_RendersStackedSegmentTooltips_WithDistrictSeriesAndValue()
    {
        var model = new SvgChartModel
        {
            Title = "District Comparison",
            Description = "Risk by district",
            Categories = ["Central District", "North District"],
            Series =
            [
                new SvgChartSeries { Name = "Critical", Values = [4m, 1m] },
                new SvgChartSeries { Name = "High", Values = [2m, 3m] }
            ]
        };

        var svg = new SvgBarChartRenderer().Render(model).ToString();

        // 1. Stacked segment interactive items
        svg.Should().Contain("class=\"netra-chart__interactive netra-chart__interactive--bar\"");
        svg.Should().Contain("tabindex=\"0\"");
        svg.Should().Contain("role=\"graphics-symbol\"");
        svg.Should().Contain("aria-label=\"Central District, Critical: 4\"");

        // 2. Narrow segment hit area
        svg.Should().Contain("netra-chart__bar-hit");

        // 3. Tooltip content with district name, category, and exact count
        svg.Should().Contain(">Central District</text>");
        svg.Should().Contain("Risk category: Critical");
        svg.Should().Contain("Count: 4");

        // 4. Second segment on the same row has its own tooltip
        svg.Should().Contain("aria-label=\"Central District, High: 2\"");
        svg.Should().Contain("Risk category: High");
        svg.Should().Contain("Count: 2");
    }

    [Fact]
    public void CityComparisonBarChart_RendersTooltipsForCitiesAndYears()
    {
        var model = new SvgChartModel
        {
            Title = "Metropolitan City Comparison (2021 vs 2023)",
            Description = "Comparative volume",
            Categories = ["Ahmedabad (2021)", "Ahmedabad (2023)", "Surat (2021)", "Surat (2023)"],
            Series = [new SvgChartSeries { Name = "Cases Reported", Values = [447m, 246m, 296m, 397m] }]
        };

        var svg = new SvgBarChartRenderer().Render(model).ToString();

        svg.Should().Contain(">Ahmedabad (2021)</text>");
        svg.Should().Contain("Cases Reported");
        svg.Should().Contain("Count: 447");

        svg.Should().Contain(">Surat (2023)</text>");
        svg.Should().Contain("Count: 397");
    }

    [Fact]
    public void CybercrimeMotivesDonutChart_RendersTooltipsWithPercentages()
    {
        var model = new SvgChartModel
        {
            Title = "Reported Cybercrime Motives (Gujarat 2023)",
            Description = "Motive breakdown",
            Categories = ["Fraud", "Causing disrepute", "Extortion"],
            Series = [new SvgChartSeries { Name = "Cases by Motive", Values = [1187m, 387m, 183m] }] // Total 1757
        };

        var svg = new SvgDonutChartRenderer().Render(model).ToString();

        // 1187 / 1757 = 67.6%
        svg.Should().Contain(">Fraud</text>");
        svg.Should().Contain(">Cases: </tspan><tspan class=\"netra-chart__tooltip-value\">1187</tspan>");
        svg.Should().Contain(">Share: </tspan><tspan class=\"netra-chart__tooltip-value\">67.6%</tspan>");

        // 387 / 1757 = 22.0%
        svg.Should().Contain(">Causing disrepute</text>");
        svg.Should().Contain(">Cases: </tspan><tspan class=\"netra-chart__tooltip-value\">387</tspan>");
        svg.Should().Contain(">Share: </tspan><tspan class=\"netra-chart__tooltip-value\">22.0%</tspan>");

        // 183 / 1757 = 10.4%
        svg.Should().Contain(">Extortion</text>");
        svg.Should().Contain(">Cases: </tspan><tspan class=\"netra-chart__tooltip-value\">183</tspan>");
        svg.Should().Contain(">Share: </tspan><tspan class=\"netra-chart__tooltip-value\">10.4%</tspan>");
    }
}
