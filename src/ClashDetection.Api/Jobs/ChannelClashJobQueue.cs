using System.Threading.Channels;

namespace ClashDetection.Api.Jobs;

/// <summary>
/// In-process job queue backed by an unbounded <see cref="Channel{T}"/>. Producers (HTTP requests)
/// write job ids; the background worker(s) consume them. Replace with a distributed broker for
/// horizontal scaling — the rest of the pipeline is written against <see cref="IClashJobQueue"/>.
/// </summary>
public sealed class ChannelClashJobQueue : IClashJobQueue
{
    private readonly Channel<string> _channel =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public ValueTask EnqueueAsync(string jobId, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<string> DequeueAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
