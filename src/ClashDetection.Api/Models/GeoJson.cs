using System.Text.Json.Serialization;

namespace ClashDetection.Api.Models;

/// <summary>
/// GeoJSON DTOs (RFC 7946 subset) used for the clash-detection request and response.
/// Only the pieces this service needs are modelled: a FeatureCollection of Polygon
/// features. Coordinates are kept as raw <c>double[]</c> positions so we control the
/// exact serialized shape and avoid pulling NTS types into the public contract.
/// </summary>
public sealed class FeatureCollection
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "FeatureCollection";

    [JsonPropertyName("features")]
    public List<Feature> Features { get; set; } = [];
}

public sealed class Feature
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "Feature";

    /// <summary>Building identifier. GeoJSON allows a string or number id; we treat it as a string.</summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("properties")]
    public FeatureProperties? Properties { get; set; }

    [JsonPropertyName("geometry")]
    public Geometry? Geometry { get; set; }
}

/// <summary>
/// Properties carried on a feature. Inputs supply <c>height</c>/<c>elevation</c>;
/// outputs additionally carry <c>buildings</c> (the ids that overlap in the section).
/// Nullable numerics let validation distinguish "missing" from "zero".
/// </summary>
public sealed class FeatureProperties
{
    [JsonPropertyName("elevation")]
    public double? Elevation { get; set; }

    [JsonPropertyName("height")]
    public double? Height { get; set; }

    [JsonPropertyName("buildings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Buildings { get; set; }
}

public sealed class Geometry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "Polygon";

    /// <summary>
    /// Polygon coordinates: an array of linear rings; each ring is an array of
    /// positions; each position is <c>[x, y]</c>. The first ring is the exterior.
    /// </summary>
    [JsonPropertyName("coordinates")]
    public List<List<double[]>> Coordinates { get; set; } = [];
}
