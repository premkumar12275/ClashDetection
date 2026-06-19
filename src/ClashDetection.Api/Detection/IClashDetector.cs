using ClashDetection.Api.Models;

namespace ClashDetection.Api.Detection;

/// <summary>
/// Detects 3D overlaps ("clashes") between extruded building footprints and returns the
/// overlapping sections as a GeoJSON FeatureCollection.
/// </summary>
public interface IClashDetector
{
    /// <param name="input">A validated FeatureCollection of Polygon buildings.</param>
    /// <param name="cancellationToken">
    /// Cancels a long-running computation (e.g. when the owning job is abandoned).
    /// </param>
    FeatureCollection Detect(FeatureCollection input, CancellationToken cancellationToken = default);
}
