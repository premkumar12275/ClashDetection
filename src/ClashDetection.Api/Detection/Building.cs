using NetTopologySuite.Geometries;

namespace ClashDetection.Api.Detection;

/// <summary>
/// A building modelled as a vertical extrusion of a 2D footprint between
/// <see cref="Elevation"/> and <see cref="Top"/>. <see cref="Index"/> gives each building a
/// stable ordinal used to deduplicate pairs (only test j &gt; i) when scanning the spatial index.
/// </summary>
public sealed class Building
{
    public required int Index { get; init; }
    public required string Id { get; init; }
    public required Polygon Footprint { get; init; }
    public required double Elevation { get; init; }
    public required double Height { get; init; }

    /// <summary>Top of the extrusion (elevation + height).</summary>
    public double Top => Elevation + Height;
}
