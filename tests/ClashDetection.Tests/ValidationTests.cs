using ClashDetection.Api.Models;
using ClashDetection.Api.Validation;

namespace ClashDetection.Tests;

public class ValidationTests
{
    [Fact]
    public void ValidSample_HasNoErrors()
        => Assert.Empty(GeoJsonValidator.Validate(TestSupport.Load("input-sample1.json")));

    [Fact]
    public void NullCollection_IsRejected()
        => Assert.NotEmpty(GeoJsonValidator.Validate(null));

    [Fact]
    public void EmptyFeatures_IsRejected()
    {
        var errors = GeoJsonValidator.Validate(new FeatureCollection { Features = [] });
        Assert.Contains(errors, e => e.Contains("non-empty"));
    }

    [Fact]
    public void MissingHeight_ReportsDescriptiveError()
    {
        var fc = new FeatureCollection
        {
            Features =
            [
                new Feature
                {
                    Id = "b0",
                    Properties = new FeatureProperties { Elevation = 0 }, // height missing
                    Geometry = Square(),
                },
            ],
        };

        var errors = GeoJsonValidator.Validate(fc);
        Assert.Contains(errors, e => e.Contains("height") && e.Contains("required"));
    }

    [Fact]
    public void NegativeHeight_IsRejected()
    {
        var fc = One(new FeatureProperties { Elevation = 0, Height = -3 }, Square());
        Assert.Contains(GeoJsonValidator.Validate(fc), e => e.Contains("greater than 0"));
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        var fc = new FeatureCollection
        {
            Features =
            [
                new Feature { Id = "dup", Properties = new() { Elevation = 0, Height = 1 }, Geometry = Square() },
                new Feature { Id = "dup", Properties = new() { Elevation = 0, Height = 1 }, Geometry = Square() },
            ],
        };
        Assert.Contains(GeoJsonValidator.Validate(fc), e => e.Contains("duplicate id"));
    }

    [Fact]
    public void UnclosedRing_IsRejected()
    {
        var geom = new Geometry
        {
            Coordinates = [[[0, 0], [10, 0], [10, 10], [0, 10]]], // not closed, only 4 pts non-repeating
        };
        var fc = One(new FeatureProperties { Elevation = 0, Height = 1 }, geom);
        Assert.Contains(GeoJsonValidator.Validate(fc), e => e.Contains("not closed"));
    }

    [Fact]
    public void NonPolygonGeometry_IsRejected()
    {
        var geom = new Geometry { Type = "LineString", Coordinates = [[[0, 0], [10, 10]]] };
        var fc = One(new FeatureProperties { Elevation = 0, Height = 1 }, geom);
        Assert.Contains(GeoJsonValidator.Validate(fc), e => e.Contains("Polygon"));
    }

    private static FeatureCollection One(FeatureProperties props, Geometry geom)
        => new() { Features = [new Feature { Id = "b0", Properties = props, Geometry = geom }] };

    private static Geometry Square() => new()
    {
        Coordinates = [[[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]]],
    };
}
