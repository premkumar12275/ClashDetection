using ClashDetection.Api.Caching;
using ClashDetection.Api.Detection;

namespace ClashDetection.Api.Jobs;

/// <summary>
/// Background worker that drains the job queue and runs clash detection off the request thread.
/// This is what makes computations longer than the client's 10s timeout safe: the HTTP request is
/// never blocked on the computation — it either returns a cached/fast result inline or a 202 with a
/// polling URL, while the actual work proceeds here. Several consumer loops run concurrently to use
/// available cores; a per-job timeout guards against runaway computations.
/// </summary>
public sealed class ClashJobWorker(
    IClashJobQueue queue,
    IClashJobStore store,
    IClashCache cache,
    IClashDetector detector,
    ILogger<ClashJobWorker> logger) : BackgroundService
{
    // Upper bound on a single computation; the async pattern tolerates long jobs, but this stops a
    // pathological input from pinning a worker forever.
    private static readonly TimeSpan MaxComputeTime = TimeSpan.FromMinutes(5);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var degreeOfParallelism = Math.Max(1, Environment.ProcessorCount);
        var loops = new Task[degreeOfParallelism];
        for (var i = 0; i < degreeOfParallelism; i++)
            loops[i] = ConsumeAsync(stoppingToken);
        return Task.WhenAll(loops);
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in queue.DequeueAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await ProcessAsync(jobId, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessAsync(string jobId, CancellationToken stoppingToken)
    {
        var job = store.Get(jobId);
        if (job?.Input is null)
        {
            logger.LogWarning("Job {JobId} dequeued but has no input; skipping.", jobId);
            return;
        }

        store.MarkProcessing(jobId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(MaxComputeTime);

        try
        {
            // Detection is CPU-bound; run it on the thread pool so the consumer loop stays responsive.
            var result = await Task.Run(() => detector.Detect(job.Input, timeout.Token), timeout.Token)
                .ConfigureAwait(false);

            await cache.SetAsync(jobId, result, stoppingToken).ConfigureAwait(false);
            store.Complete(jobId, result);
            job.Input = null; // release memory once computed
            logger.LogInformation(
                "Job {JobId} completed: {Count} clash section(s).", jobId, result.Features.Count);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            store.Fail(jobId, $"Computation exceeded the maximum allowed time of {MaxComputeTime.TotalSeconds:N0}s.");
            logger.LogWarning("Job {JobId} timed out after {Seconds}s.", jobId, MaxComputeTime.TotalSeconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Service is shutting down; leave the job for a future run / restart.
            logger.LogInformation("Job {JobId} interrupted by shutdown.", jobId);
        }
        catch (Exception ex)
        {
            store.Fail(jobId, $"Computation failed: {ex.Message}");
            logger.LogError(ex, "Job {JobId} failed.", jobId);
        }
    }
}
