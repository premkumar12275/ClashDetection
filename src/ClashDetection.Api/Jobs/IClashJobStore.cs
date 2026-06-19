using ClashDetection.Api.Models;

namespace ClashDetection.Api.Jobs;

/// <summary>
/// Persists job state and lets callers briefly await completion (for the synchronous-wait window).
/// In-memory by default; swap for a Redis/SQL-backed store so job state survives restarts and is
/// visible across API instances in a scaled-out deployment.
/// </summary>
public interface IClashJobStore
{
    /// <summary>
    /// Returns the existing job for this hash, or creates a new Queued one.
    /// <paramref name="created"/> is true only when this call created the job — the caller then
    /// owns enqueuing it, ensuring concurrent identical requests enqueue exactly once.
    /// </summary>
    ClashJob GetOrCreate(string jobId, out bool created);

    ClashJob? Get(string jobId);

    void MarkProcessing(string jobId);
    void Complete(string jobId, FeatureCollection result);
    void Fail(string jobId, string error);

    /// <summary>
    /// Completes when the job reaches a terminal state, or after <paramref name="timeout"/>.
    /// Returns the job's final (or current) state. Used to answer fast requests inline before
    /// falling back to 202 + polling.
    /// </summary>
    Task<ClashJob?> WaitAsync(string jobId, TimeSpan timeout, CancellationToken cancellationToken = default);
}
