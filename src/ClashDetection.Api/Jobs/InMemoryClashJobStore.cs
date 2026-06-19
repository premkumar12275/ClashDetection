using System.Collections.Concurrent;
using ClashDetection.Api.Models;

namespace ClashDetection.Api.Jobs;

/// <summary>
/// Thread-safe in-process job store. A per-job <see cref="TaskCompletionSource"/> lets
/// <see cref="WaitAsync"/> resolve the moment a job reaches a terminal state, so fast computations
/// can be returned inline rather than forcing the client to poll.
/// </summary>
public sealed class InMemoryClashJobStore : IClashJobStore
{
    private sealed record Entry(ClashJob Job, TaskCompletionSource Completion);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public ClashJob GetOrCreate(string jobId, out bool created)
    {
        var localCreated = false;
        var entry = _entries.GetOrAdd(jobId, id =>
        {
            localCreated = true;
            var job = new ClashJob { JobId = id };
            return new Entry(job, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        });
        created = localCreated;
        return entry.Job;
    }

    public ClashJob? Get(string jobId)
        => _entries.TryGetValue(jobId, out var entry) ? entry.Job : null;

    public void MarkProcessing(string jobId)
    {
        if (_entries.TryGetValue(jobId, out var entry))
            entry.Job.Status = ClashJobStatus.Processing;
    }

    public void Complete(string jobId, FeatureCollection result)
    {
        if (!_entries.TryGetValue(jobId, out var entry))
            return;
        entry.Job.Result = result;
        entry.Job.Status = ClashJobStatus.Completed;
        entry.Job.CompletedAt = DateTimeOffset.UtcNow;
        entry.Completion.TrySetResult();
    }

    public void Fail(string jobId, string error)
    {
        if (!_entries.TryGetValue(jobId, out var entry))
            return;
        entry.Job.Error = error;
        entry.Job.Status = ClashJobStatus.Failed;
        entry.Job.CompletedAt = DateTimeOffset.UtcNow;
        entry.Completion.TrySetResult();
    }

    public async Task<ClashJob?> WaitAsync(string jobId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(jobId, out var entry))
            return null;

        if (entry.Job.Status is ClashJobStatus.Completed or ClashJobStatus.Failed)
            return entry.Job;

        // Wait for either completion or the sync window to elapse, without throwing on timeout.
        var completed = await Task.WhenAny(entry.Completion.Task, Task.Delay(timeout, cancellationToken))
            .ConfigureAwait(false);
        return entry.Job;
    }
}
