using ClashDetection.Api.Detection;
using ClashDetection.Api.Models;

namespace ClashDetection.Tests;

public class ClashDetectorTests
{
    private readonly ClashDetector _detector = new();

    [Fact]
    public void Sample1_ProducesExpectedOverlaps()
    {
        var input = TestSupport.Load("input-sample1.json");
        var expected = TestSupport.Load("output-sample1.json");

        var actual = _detector.Detect(input);

        TestSupport.AssertEquivalent(expected, actual);
    }

    [Fact]
    public void Sample2_ProducesPairwiseOverlaps()
    {
        var input = TestSupport.Load("input-sample2.json");
        var expected = TestSupport.Load("output-sample2.json");

        var actual = _detector.Detect(input);

        TestSupport.AssertEquivalent(expected, actual);
    }

    [Fact]
    public void NoOverlap_ReturnsEmptyCollection()
    {
        // Two footprints that share an edge only (touch, zero-area intersection) at the same height.
        var input = new FeatureCollection
        {
            Features =
            [
                Box("a", 0, 0, 10, 10, elevation: 0, height: 5),
                Box("b", 10, 0, 20, 10, elevation: 0, height: 5),
            ],
        };

        var result = _detector.Detect(input);

        Assert.Empty(result.Features);
    }

    [Fact]
    public void OverlappingFootprints_ButDisjointHeights_DoNotClash()
    {
        var input = new FeatureCollection
        {
            Features =
            [
                Box("a", 0, 0, 10, 10, elevation: 0, height: 5),   // [0,5]
                Box("b", 0, 0, 10, 10, elevation: 10, height: 5),  // [10,15]
            ],
        };

        var result = _detector.Detect(input);

        Assert.Empty(result.Features);
    }

    [Fact]
    public void SingleBuilding_NeverClashes()
    {
        var input = new FeatureCollection { Features = [Box("solo", 0, 0, 10, 10, 0, 5)] };
        Assert.Empty(_detector.Detect(input).Features);
    }

    [Fact]
    public void PartialVerticalOverlap_UsesIntersectionOfIntervals()
    {
        var input = new FeatureCollection
        {
            Features =
            [
                Box("a", 0, 0, 10, 10, elevation: 0, height: 6),  // [0,6]
                Box("b", 0, 0, 10, 10, elevation: 4, height: 6),  // [4,10]
            ],
        };

        var result = _detector.Detect(input);

        var clash = Assert.Single(result.Features);
        Assert.Equal(4, clash.Properties!.Elevation);   // max(0,4)
        Assert.Equal(2, clash.Properties!.Height);      // min(6,10) - 4
        Assert.Equal(["a", "b"], clash.Properties!.Buildings);
    }

    [Fact]
    public void ThreeMutuallyOverlapping_ReportsThreePairs()
    {
        var input = new FeatureCollection
        {
            Features =
            [
                Box("a", 0, 0, 10, 10, 0, 5),
                Box("b", 0, 0, 10, 10, 0, 5),
                Box("c", 0, 0, 10, 10, 0, 5),
            ],
        };

        var result = _detector.Detect(input);

        Assert.Equal(3, result.Features.Count); // ab, ac, bc
    }

    private static Feature Box(string id, double minX, double minY, double maxX, double maxY,
        double elevation, double height) => new()
    {
        Id = id,
        Properties = new FeatureProperties { Elevation = elevation, Height = height },
        Geometry = new Geometry
        {
            Coordinates =
            [
                [
                    [minX, minY], [maxX, minY], [maxX, maxY], [minX, maxY], [minX, minY],
                ],
            ],
        },
    };
}
