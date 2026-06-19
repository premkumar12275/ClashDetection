using ClashDetection.Api.Caching;
using ClashDetection.Api.Models;
using Microsoft.Extensions.Options;

namespace ClashDetection.Api.Jobs;

public enum SubmissionOutcome
{
    /// <summary>Result is ready now (cache hit or finished within the sync-wait window) — return 200.</summary>
    Completed,
    /// <summary>Still computing — return 202 with a polling URL.</summary>
    Accepted,
    /// <summary>Computation failed — surface the error.</summary>
    Failed,
}

public sealed record ClashSubmission(
    SubmissionOutcome Outcome,
    string JobId,
    FeatureCollection? Result,
    string? Error);

public sealed class ClashOptions
{
    /// <summary>
    /// How long a POST will wait for a fresh computation before falling back to 202 + polling.
    /// Kept comfortably below the typical 10s client timeout so most small inputs return inline
    /// while large ones degrade gracefully to async.
    /// </summary>
    public double SyncWaitWindowSeconds { get; set; } = 2.0;
}

/// <summary>
/// Orchestrates a clash request: cache lookup → job de-duplication by content hash → enqueue →
/// brief synchronous wait. The endpoint layer translates the returned <see cref="ClashSubmission"/>
/// into the appropriate HTTP response.
/// </summary>
public sealed class ClashJobService(
    IClashCache cache,
    IClashJobStore store,
    IClashJobQueue queue,
    IOptions<ClashOptions> options)
{
    private readonly TimeSpan _syncWaitWindow = TimeSpan.FromSeconds(options.Value.SyncWaitWindowSeconds);

    public async Task<ClashSubmission> SubmitAsync(
        FeatureCollection input, string inputHash, CancellationToken cancellationToken = default)
    {
        // 1. Fast path: identical input already computed.
        var cached = await cache.GetAsync(inputHash, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
            return new ClashSubmission(SubmissionOutcome.Completed, inputHash, cached, null);

        // 2. De-duplicate by content hash: concurrent identical requests share one job/computation.
        var job = store.GetOrCreate(inputHash, out var created);
        if (created)
        {
            job.Input = input;
            await queue.EnqueueAsync(inputHash, cancellationToken).ConfigureAwait(false);
        }

        // 3. Wait briefly so quick computations return inline instead of forcing a poll.
        var settled = await store.WaitAsync(inputHash, _syncWaitWindow, cancellationToken).ConfigureAwait(false)
                      ?? job;

        return settled.Status switch
        {
            ClashJobStatus.Completed => new ClashSubmission(SubmissionOutcome.Completed, inputHash, settled.Result, null),
            ClashJobStatus.Failed => new ClashSubmission(SubmissionOutcome.Failed, inputHash, null, settled.Error),
            _ => new ClashSubmission(SubmissionOutcome.Accepted, inputHash, null, null),
        };
    }

    public ClashJob? GetJob(string jobId) => store.Get(jobId);
}
