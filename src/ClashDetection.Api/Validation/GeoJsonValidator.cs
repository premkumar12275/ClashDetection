using ClashDetection.Api.Models;

namespace ClashDetection.Api.Validation;

/// <summary>
/// Validates an incoming <see cref="FeatureCollection"/> against the rules required for
/// clash detection and returns descriptive, client-friendly error messages. Validation is
/// deliberately strict and exhaustive (collects all problems) so a caller can fix every
/// issue in one round-trip rather than discovering them one at a time.
/// </summary>
public static class GeoJsonValidator
{
    public static IReadOnlyList<string> Validate(FeatureCollection? collection)
    {
        var errors = new List<string>();

        if (collection is null)
        {
            errors.Add("Request body is missing or is not a valid GeoJSON object.");
            return errors;
        }

        if (!string.Equals(collection.Type, "FeatureCollection", StringComparison.Ordinal))
            errors.Add($"'type' must be 'FeatureCollection' but was '{collection.Type ?? "null"}'.");

        if (collection.Features is null || collection.Features.Count == 0)
        {
            errors.Add("'features' must be a non-empty array of building features.");
            return errors;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < collection.Features.Count; i++)
        {
            var f = collection.Features[i];
            // Prefer the feature's own id in messages; fall back to positional index.
            var label = string.IsNullOrWhiteSpace(f.Id) ? $"features[{i}]" : $"feature '{f.Id}'";

            if (string.IsNullOrWhiteSpace(f.Id))
                errors.Add($"{label}: 'id' is required and must be a non-empty string.");
            else if (!seenIds.Add(f.Id))
                errors.Add($"{label}: duplicate id '{f.Id}'. Building ids must be unique.");

            ValidateProperties(f.Properties, label, errors);
            ValidateGeometry(f.Geometry, label, errors);
        }

        return errors;
    }

    private static void ValidateProperties(FeatureProperties? props, string label, List<string> errors)
    {
        if (props is null)
        {
            errors.Add($"{label}: 'properties' is required (must include 'height' and 'elevation').");
            return;
        }

        if (props.Height is null)
            errors.Add($"{label}: 'properties.height' is required.");
        else if (double.IsNaN(props.Height.Value) || double.IsInfinity(props.Height.Value))
            errors.Add($"{label}: 'properties.height' must be a finite number.");
        else if (props.Height.Value <= 0)
            errors.Add($"{label}: 'properties.height' must be greater than 0 (was {props.Height.Value}).");

        if (props.Elevation is null)
            errors.Add($"{label}: 'properties.elevation' is required.");
        else if (double.IsNaN(props.Elevation.Value) || double.IsInfinity(props.Elevation.Value))
            errors.Add($"{label}: 'properties.elevation' must be a finite number.");
    }

    private static void ValidateGeometry(Geometry? geom, string label, List<string> errors)
    {
        if (geom is null)
        {
            errors.Add($"{label}: 'geometry' is required.");
            return;
        }

        if (!string.Equals(geom.Type, "Polygon", StringComparison.Ordinal))
        {
            errors.Add($"{label}: geometry.type must be 'Polygon' but was '{geom.Type ?? "null"}'.");
            return;
        }

        if (geom.Coordinates is null || geom.Coordinates.Count == 0)
        {
            errors.Add($"{label}: polygon must have at least one linear ring in 'coordinates'.");
            return;
        }

        for (var r = 0; r < geom.Coordinates.Count; r++)
        {
            var ring = geom.Coordinates[r];
            if (ring is null || ring.Count < 4)
            {
                errors.Add($"{label}: ring[{r}] must have at least 4 positions (a closed triangle).");
                continue;
            }

            foreach (var pos in ring)
            {
                if (pos is null || pos.Length < 2)
                {
                    errors.Add($"{label}: ring[{r}] contains a position that is not a valid [x, y] pair.");
                    break;
                }
                if (double.IsNaN(pos[0]) || double.IsNaN(pos[1]) ||
                    double.IsInfinity(pos[0]) || double.IsInfinity(pos[1]))
                {
                    errors.Add($"{label}: ring[{r}] contains a non-finite coordinate value.");
                    break;
                }
            }

            var first = ring[0];
            var last = ring[^1];
            if (first is { Length: >= 2 } && last is { Length: >= 2 } &&
                (first[0] != last[0] || first[1] != last[1]))
            {
                errors.Add($"{label}: ring[{r}] is not closed (first and last positions must be equal).");
            }
        }
    }
}
