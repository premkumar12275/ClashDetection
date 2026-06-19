using System.Text.Json;
using ClashDetection.Api.Models;
using NetTopologySuite.Geometries;

namespace ClashDetection.Tests;

/// <summary>
/// Shared helpers for loading sample GeoJSON and comparing clash results independently of feature
/// order, ring winding, or vertex start point (all of which are insignificant for correctness).
/// </summary>
internal static class TestSupport
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly GeometryFactory Factory = new();

    public static string DataPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    public static FeatureCollection Load(string fileName)
    {
        var json = File.ReadAllText(DataPath(fileName));
        return JsonSerializer.Deserialize<FeatureCollection>(json, Json)
               ?? throw new InvalidOperationException($"Could not deserialize {fileName}");
    }

    public static Polygon ToPolygon(Feature feature)
    {
        var rings = feature.Geometry!.Coordinates;
        var shell = ToRing(rings[0]);
        var holes = rings.Skip(1).Select(ToRing).ToArray();
        return Factory.CreatePolygon(shell, holes);
    }

    private static LinearRing ToRing(List<double[]> ring)
        => Factory.CreateLinearRing(ring.Select(p => new Coordinate(p[0], p[1])).ToArray());

    /// <summary>
    /// Asserts two clash FeatureCollections describe the same set of overlaps: same count, and each
    /// expected feature has a one-to-one match with the same building pair, elevation/height (within
    /// tolerance), and a topologically-equal footprint.
    /// </summary>
    public static void AssertEquivalent(FeatureCollection expected, FeatureCollection actual)
    {
        Assert.Equal(expected.Features.Count, actual.Features.Count);

        var remaining = new List<Feature>(actual.Features);
        foreach (var exp in expected.Features)
        {
            var expPoly = ToPolygon(exp);
            var match = remaining.FirstOrDefault(act =>
                SameBuildings(exp, act)
                && Close(exp.Properties!.Elevation, act.Properties!.Elevation)
                && Close(exp.Properties!.Height, act.Properties!.Height)
                && expPoly.EqualsTopologically(ToPolygon(act)));

            Assert.True(match is not null,
                $"No matching clash for buildings [{string.Join(",", exp.Properties!.Buildings ?? [])}] " +
                $"at elevation {exp.Properties!.Elevation}, height {exp.Properties!.Height}.");
            remaining.Remove(match!);
        }
    }

    private static bool SameBuildings(Feature a, Feature b)
    {
        var sa = new HashSet<string>(a.Properties!.Buildings ?? []);
        var sb = new HashSet<string>(b.Properties!.Buildings ?? []);
        return sa.SetEquals(sb);
    }

    private static bool Close(double? a, double? b) => Math.Abs((a ?? double.NaN) - (b ?? double.NaN)) < 1e-6;
}
