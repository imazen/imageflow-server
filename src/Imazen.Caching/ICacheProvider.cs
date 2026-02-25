using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Imazen.Caching;

/// <summary>
/// Describes the nature of a cache provider for the cascade to make decisions.
/// </summary>
public sealed class CacheProviderCapabilities
{
    /// <summary>
    /// If true, operations must run inline on the request thread (e.g., memory cache).
    /// If false, store operations can be deferred to the upload queue.
    /// </summary>
    public bool RequiresInlineExecution { get; init; }

    /// <summary>
    /// Latency zone identifier. "local" for memory/disk, "s3:us-east-1:bucket" for cloud.
    /// Providers with non-"local" zones are gated by the bloom filter.
    /// </summary>
    public string LatencyZone { get; init; } = "local";

    /// <summary>
    /// Whether this provider is a local (fast) provider.
    /// </summary>
    public bool IsLocal => LatencyZone == "local" || RequiresInlineExecution;
}

/// <summary>
/// A cache storage backend. Implementations are simple: store bytes, fetch bytes.
/// Routing and coalescing are handled by the cascade, not the provider.
/// </summary>
public interface ICacheProvider
{
    string Name { get; }

    CacheProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Attempt to fetch a cached entry. Returns null if not found.
    /// </summary>
    ValueTask<CacheFetchResult?> FetchAsync(CacheKey key, CancellationToken ct = default);

    /// <summary>
    /// Store data to the cache. The data byte array must not be modified by the provider.
    /// </summary>
    ValueTask StoreAsync(CacheKey key, byte[] data, CacheEntryMetadata metadata, CancellationToken ct = default);

    /// <summary>
    /// Remove a specific entry. Returns true if the entry was found and removed.
    /// </summary>
    ValueTask<bool> InvalidateAsync(CacheKey key, CancellationToken ct = default);

    /// <summary>
    /// Purge all variants for a given source. Returns the count of entries removed.
    /// Uses the source hash prefix for listing/deletion.
    /// </summary>
    ValueTask<int> PurgeBySourceAsync(ReadOnlyMemory<byte> sourceHash, CancellationToken ct = default);

    /// <summary>
    /// Check if the provider probably contains the key.
    /// For local providers, return true (always try). For cloud, this may return false
    /// to skip unnecessary network calls. The cascade's bloom filter is the primary gate.
    /// </summary>
    bool ProbablyContains(CacheKey key);

    /// <summary>
    /// Health check. Returns true if the provider is operational.
    /// </summary>
    ValueTask<bool> HealthCheckAsync(CancellationToken ct = default);
}

/// <summary>
/// Result of fetching from a cache provider.
/// </summary>
public sealed class CacheFetchResult : IDisposable
{
    /// <summary>
    /// The cached data as a byte array.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>
    /// Metadata associated with this cache entry.
    /// </summary>
    public CacheEntryMetadata Metadata { get; }

    public CacheFetchResult(byte[] data, CacheEntryMetadata metadata)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    public void Dispose()
    {
        // No-op for byte arrays. Present for future extensibility if we
        // switch to pooled buffers.
    }
}
