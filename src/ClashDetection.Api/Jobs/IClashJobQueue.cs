namespace ClashDetection.Api.Jobs;

/// <summary>
/// Decouples request handling from computation. The default implementation is an in-process
/// channel; for a multi-instance deployment, back this with a real broker (Azure Service Bus,
/// Redis Streams, RabbitMQ) so any worker instance can pick up queued jobs.
/// </summary>
public interface IClashJobQueue
{
    ValueTask EnqueueAsync(string jobId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> DequeueAllAsync(CancellationToken cancellationToken);
}
