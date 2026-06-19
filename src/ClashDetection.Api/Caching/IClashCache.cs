using ClashDetection.Api.Models;

namespace ClashDetection.Api.Caching;

/// <summary>
/// Caches computed clash results keyed by the input content hash. The interface is async and
/// store-agnostic so the in-memory implementation can be swapped for a distributed cache
/// (e.g. Redis via IDistributedCache) when running multiple API instances — see
/// <see cref="MemoryClashCache"/> for the local default.
/// </summary>
public interface IClashCache
{
    Task<FeatureCollection?> GetAsync(string inputHash, CancellationToken cancellationToken = default);
    Task SetAsync(string inputHash, FeatureCollection result, CancellationToken cancellationToken = default);
}
