using FluentAssertions;
using NETRA.Application.Network;
using NETRA.Web.Services.Network;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>SVG renderer tests: accessibility, deterministic geometry and HTML encoding.</summary>
public sealed class NetworkGraphRendererTests
{
    private readonly NetworkGraphRenderer _renderer = new();

    private static NetworkGraphRenderModel Model(
        IReadOnlyList<NetworkGraphRenderNode>? nodes = null,
        IReadOnlyList<NetworkGraphRenderRelationship>? relationships = null) => new()
        {
            Title = "Network graph",
            Description = "A server-rendered test graph.",
            SelectedNodeKey = "Person:root",
            Nodes = nodes ??
            [
                new NetworkGraphRenderNode
                {
                    Key = "Person:root",
                    EntityType = NetworkEntityType.Person,
                    Name = "Avery Root",
                    SecondaryLabel = "Person",
                    Depth = 0
                },
                new NetworkGraphRenderNode
                {
                    Key = "Case:one",
                    EntityType = NetworkEntityType.Case,
                    Name = "NET-001",
                    SecondaryLabel = "Test case",
                    Depth = 1
                },
                new NetworkGraphRenderNode
                {
                    Key = "Location:one",
                    EntityType = NetworkEntityType.Location,
                    Name = "Central Scene",
                    SecondaryLabel = "Location",
                    Depth = 1
                }
            ],
            Relationships = relationships ??
            [
                new NetworkGraphRenderRelationship
                {
                    SourceKey = "Person:root",
                    TargetKey = "Case:one",
                    Label = "Witness To"
                }
            ]
        };

    [Fact]
    public void Render_EmitsAccessibleSvgWithLegendAndAllNodeTypes()
    {
        var svg = _renderer.Render(Model()).Value!;

        svg.Should().StartWith("<svg class=\"netra-network\"");
        svg.Should().Contain("viewBox=\"0 0 1000 650\"");
        svg.Should().Contain("role=\"img\"");
        svg.Should().Contain("netra-network-title");
        svg.Should().Contain("A server-rendered test graph.");
        svg.Should().Contain("NODE TYPE");
        svg.Should().Contain("netra-network__node--person");
        svg.Should().Contain("netra-network__node--case");
        svg.Should().Contain("netra-network__node--location");
    }

    [Fact]
    public void Render_PlacesSelectedNodeAtFixedCentre()
    {
        var svg = _renderer.Render(Model()).Value!;

        svg.Should().Contain("translate(500 325)");
        svg.Should().Contain("netra-network__node--selected");
    }

    [Fact]
    public void Render_SameModelProducesEquivalentOutput()
    {
        var model = Model();

        _renderer.Render(model).Value.Should().Be(_renderer.Render(model).Value);
    }

    [Fact]
    public void Render_EncodesAllDynamicStrings()
    {
        var nodes = new[]
        {
            new NetworkGraphRenderNode
            {
                Key = "Person:root",
                EntityType = NetworkEntityType.Person,
                Name = "\"><script>alert(1)</script>",
                SecondaryLabel = "Tom & Jerry",
                Depth = 0
            }
        };
        var relationships = new[]
        {
            new NetworkGraphRenderRelationship
            {
                SourceKey = "Person:root",
                TargetKey = "Person:root",
                Label = "<script>edge</script>"
            }
        };

        var svg = _renderer.Render(Model(nodes, relationships)).Value!;

        svg.Should().NotContain("<script");
        svg.Should().Contain("&lt;script&gt;");
        svg.Should().Contain("Tom &amp; Jerry");
    }
}
