using ClashDetection.Api.Models;
using Microsoft.Extensions.Caching.Memory;

namespace ClashDetection.Api.Caching;

/// <summary>
/// In-process cache backed by <see cref="IMemoryCache"/>. Suitable for a single instance or as an
/// L1 cache in front of a distributed L2. For horizontal scaling, replace the registration in
/// Program.cs with a Redis-backed implementation so cache hits are shared across instances.
/// </summary>
public sealed class MemoryClashCache(IMemoryCache cache) : IClashCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    private static string Key(string inputHash) => $"clash:{inputHash}";

    public Task<FeatureCollection?> GetAsync(string inputHash, CancellationToken cancellationToken = default)
        => Task.FromResult(cache.TryGetValue(Key(inputHash), out FeatureCollection? result) ? result : null);

    public Task SetAsync(string inputHash, FeatureCollection result, CancellationToken cancellationToken = default)
    {
        cache.Set(Key(inputHash), result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Ttl,
            Size = 1,
        });
        return Task.CompletedTask;
    }
}
