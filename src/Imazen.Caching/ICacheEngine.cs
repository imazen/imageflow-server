using System;
using System.Threading;
using System.Threading.Tasks;

namespace Imazen.Caching;

/// <summary>
/// High-level cache engine interface. Handles multi-tier cascade, request coalescing,
/// bloom filtering, and backpressure internally.
/// </summary>
public interface ICacheEngine : IDisposable
{
    /// <summary>
    /// Fetch from cache or create via factory. Always stores produced results to all tiers.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">
    /// Called on cache miss. Returns the produced data and metadata, or null if production fails.
    /// The byte array returned is owned by the caller and will be stored to all tiers.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A CacheResult that must be disposed by the caller.</returns>
    ValueTask<CacheResult> GetOrCreateAsync(
        CacheKey key,
        Func<CancellationToken, ValueTask<(byte[] Data, CacheEntryMetadata Metadata)?>> factory,
        CancellationToken ct = default);

    /// <summary>
    /// Invalidate a specific entry from all tiers.
    /// </summary>
    ValueTask InvalidateAsync(CacheKey key, CancellationToken ct = default);

    /// <summary>
    /// Purge all variants of a source from all tiers.
    /// </summary>
    /// <param name="sourceHash">The 16-byte source hash.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Total number of entries removed across all tiers.</returns>
    ValueTask<int> PurgeBySourceAsync(ReadOnlyMemory<byte> sourceHash, CancellationToken ct = default);
}
