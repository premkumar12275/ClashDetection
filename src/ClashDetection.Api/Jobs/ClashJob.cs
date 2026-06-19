using System.Text.Json.Serialization;
using ClashDetection.Api.Models;

namespace ClashDetection.Api.Jobs;

[JsonConverter(typeof(JsonStringEnumConverter<ClashJobStatus>))]
public enum ClashJobStatus
{
    Queued,
    Processing,
    Completed,
    Failed,
}

/// <summary>
/// Tracks one clash-detection computation. The <see cref="JobId"/> is the input content hash, so
/// identical inputs map to the same job — giving request de-duplication and caching for free.
/// </summary>
public sealed class ClashJob
{
    public required string JobId { get; init; }
    public ClashJobStatus Status { get; set; } = ClashJobStatus.Queued;

    /// <summary>The validated input to compute. Never serialized in API responses.</summary>
    [JsonIgnore]
    public FeatureCollection? Input { get; set; }

    /// <summary>Populated once the job completes successfully.</summary>
    public FeatureCollection? Result { get; set; }

    /// <summary>Populated with a descriptive message if the job fails.</summary>
    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
