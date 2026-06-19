using ClashDetection.Api.Models;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace ClashDetection.Api.Detection;

/// <summary>
/// Core clash-detection engine.
///
/// Model: each building is a 2D footprint extruded vertically over [elevation, elevation+height].
/// Two buildings clash when (a) their footprints intersect with positive area AND (b) their
/// vertical intervals overlap. A clash section is the footprint intersection, carrying
/// elevation = max(e1,e2) and height = min(top1,top2) - elevation. When three or more buildings
/// share a region, every pairwise overlap is reported (matches output-sample2.json).
///
/// Optimization: a naive all-pairs scan is O(n^2) footprint intersections. Instead we load
/// building envelopes into an STRtree (a packed R-tree). Querying the tree returns only the
/// spatially-near candidates, so the expensive NTS polygon intersection runs on a near-linear
/// number of pairs for typical (spatially distributed) inputs. Within each candidate pair the
/// cheap vertical-interval test runs first, short-circuiting before any polygon work.
/// </summary>
public sealed class ClashDetector : IClashDetector
{
    // Sections smaller than this (square metres) are treated as floating-point noise from
    // buildings that merely touch edge-to-edge, not a real overlap.
    private const double MinOverlapArea = 1e-6;

    private readonly GeometryFactory _geometryFactory = new();

    public FeatureCollection Detect(FeatureCollection input, CancellationToken cancellationToken = default)
    {
        var buildings = BuildBuildings(input);

        var result = new FeatureCollection();
        if (buildings.Count < 2)
            return result; // nothing can clash

        // Pack all building envelopes into the spatial index once.
        var index = new STRtree<Building>();
        foreach (var b in buildings)
            index.Insert(b.Footprint.EnvelopeInternal, b);
        index.Build();

        foreach (var a in buildings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var b in index.Query(a.Footprint.EnvelopeInternal))
            {
                // Each unordered pair is returned twice (a→b and b→a); keep one ordering.
                if (b.Index <= a.Index)
                    continue;

                // Cheap vertical filter first.
                var zLow = Math.Max(a.Elevation, b.Elevation);
                var zHigh = Math.Min(a.Top, b.Top);
                if (zHigh <= zLow)
                    continue;

                // Expensive footprint intersection only for vertically-overlapping pairs.
                NetTopologySuite.Geometries.Geometry intersection;
                try
                {
                    intersection = a.Footprint.Intersection(b.Footprint);
                }
                catch (NetTopologySuite.Geometries.TopologyException)
                {
                    // Fall back to noded/robust geometries if the overlay hits a precision edge case.
                    intersection = a.Footprint.Buffer(0).Intersection(b.Footprint.Buffer(0));
                }

                AppendPolygonalParts(intersection, a.Id, b.Id, zLow, zHigh - zLow, result);
            }
        }

        return result;
    }

    private List<Building> BuildBuildings(FeatureCollection input)
    {
        var buildings = new List<Building>(input.Features.Count);
        var index = 0;
        foreach (var f in input.Features)
        {
            // Validation has already guaranteed the shape of these values.
            var footprint = ToPolygon(f.Geometry!);
            buildings.Add(new Building
            {
                Index = index++,
                Id = f.Id!,
                Footprint = footprint,
                Elevation = f.Properties!.Elevation!.Value,
                Height = f.Properties!.Height!.Value,
            });
        }
        return buildings;
    }

    private Polygon ToPolygon(Models.Geometry geometry)
    {
        var shell = ToRing(geometry.Coordinates[0]);
        var holes = geometry.Coordinates.Count > 1
            ? geometry.Coordinates.Skip(1).Select(ToRing).ToArray()
            : null;

        var polygon = _geometryFactory.CreatePolygon(shell, holes);

        // Repair winding/self-touch issues so the overlay engine gets a valid input.
        return polygon.IsValid ? polygon : (Polygon)polygon.Buffer(0);
    }

    private LinearRing ToRing(List<double[]> ring)
    {
        var coords = new Coordinate[ring.Count];
        for (var i = 0; i < ring.Count; i++)
            coords[i] = new Coordinate(ring[i][0], ring[i][1]);
        return _geometryFactory.CreateLinearRing(coords);
    }

    /// <summary>
    /// Emits one output feature per polygonal part of <paramref name="intersection"/>. The overlay
    /// can return a Polygon, a MultiPolygon, or a GeometryCollection (mixed with lines/points where
    /// footprints also touch); only parts with meaningful area become clash sections.
    /// </summary>
    private static void AppendPolygonalParts(
        NetTopologySuite.Geometries.Geometry intersection, string idA, string idB, double elevation, double height, FeatureCollection result)
    {
        for (var i = 0; i < intersection.NumGeometries; i++)
        {
            if (intersection.GetGeometryN(i) is not Polygon polygon)
                continue;
            if (polygon.IsEmpty || polygon.Area <= MinOverlapArea)
                continue;

            result.Features.Add(new Feature
            {
                Type = "Feature",
                Properties = new FeatureProperties
                {
                    Elevation = elevation,
                    Height = height,
                    Buildings = [idA, idB],
                },
                Geometry = ToGeoJsonPolygon(polygon),
            });
        }
    }

    private static Models.Geometry ToGeoJsonPolygon(Polygon polygon)
    {
        var rings = new List<List<double[]>>(1 + polygon.NumInteriorRings)
        {
            RingToCoords(polygon.ExteriorRing),
        };
        for (var i = 0; i < polygon.NumInteriorRings; i++)
            rings.Add(RingToCoords(polygon.GetInteriorRingN(i)));

        return new Models.Geometry { Type = "Polygon", Coordinates = rings };
    }

    private static List<double[]> RingToCoords(LineString ring)
    {
        var coords = ring.Coordinates;
        var list = new List<double[]>(coords.Length);
        foreach (var c in coords)
            list.Add([c.X, c.Y]);
        return list;
    }
}
