using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Imazen.Caching;

/// <summary>
/// Why the cascade is offering data to a provider.
/// Providers use this to make smarter accept/reject decisions.
/// </summary>
public enum CacheStoreReason
{
    /// <summary>
    /// Factory just produced this — no tier had it. You definitely don't have it.
    /// </summary>
    FreshlyCreated,

    /// <summary>
    /// This provider was queried (directly or bloom-gated) and definitely doesn't
    /// have the entry. Another tier supplied it. Safe to accept unconditionally.
    /// </summary>
    Missed,

    /// <summary>
    /// This provider was NOT queried because a faster tier hit first. We don't know
    /// if you still have it — you might, or you might have evicted it.
    /// </summary>
    NotQueried,
}

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
/// The cascade handles coalescing, bloom filtering, and upload queue management.
/// </summary>
public interface ICacheProvider
{
    string Name { get; }

    CacheProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Attempt to fetch a cached entry. Returns null if not found.
    /// The returned Stream is owned by the caller and must be disposed.
    /// </summary>
    ValueTask<CacheFetchResult?> FetchAsync(CacheKey key, CancellationToken ct = default);

    /// <summary>
    /// Store data to the cache. The data byte array must not be modified by the provider.
    /// </summary>
    ValueTask StoreAsync(CacheKey key, byte[] data, CacheEntryMetadata metadata, CancellationToken ct = default);

    /// <summary>
    /// Called by the cascade after a miss is resolved or a hit is found in another tier.
    /// Return true if this provider wants to receive the data for storage.
    /// This gates buffering — the cascade only buffers when at least one subscriber wants data.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="sizeBytes">Approximate size of the data in bytes, or -1 if unknown.</param>
    /// <param name="reason">Why the data is being offered.</param>
    bool WantsToStore(CacheKey key, long sizeBytes, CacheStoreReason reason);

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
    /// Health check. Returns true if the provider is operational.
    /// </summary>
    ValueTask<bool> HealthCheckAsync(CancellationToken ct = default);
}

/// <summary>
/// Result of fetching from a cache provider.
/// Exactly one of Data or DataStream is non-null:
/// - Memory providers set Data (byte[], already in memory)
/// - Disk/cloud providers set DataStream (stream, avoids full buffering)
///
/// The cascade decides whether to buffer based on whether subscribers need the data.
/// When no subscribers want it (steady-state hot path), the stream passes through
/// directly to the HTTP response with no buffering.
/// </summary>
public sealed class CacheFetchResult : IDisposable
{
    /// <summary>
    /// Buffered data (memory providers). Null for stream-based providers.
    /// </summary>
    public byte[]? Data { get; }

    /// <summary>
    /// Stream data (disk/cloud providers). Owned by the caller — must be disposed.
    /// Null for memory providers.
    /// </summary>
    public Stream? DataStream { get; }

    /// <summary>
    /// Metadata associated with this cache entry.
    /// </summary>
    public CacheEntryMetadata Metadata { get; }

    /// <summary>
    /// Content length in bytes, from Data.Length, Metadata.ContentLength, or Stream.Length.
    /// Returns -1 if truly unknown.
    /// </summary>
    public long ContentLength
    {
        get
        {
            if (Data != null) return Data.Length;
            if (Metadata.ContentLength >= 0) return Metadata.ContentLength;
            if (DataStream != null && DataStream.CanSeek) return DataStream.Length;
            return -1;
        }
    }

    /// <summary>
    /// Memory provider path: data already in byte[].
    /// </summary>
    public CacheFetchResult(byte[] data, CacheEntryMetadata metadata)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <summary>
    /// Disk/cloud provider path: data as a stream.
    /// Metadata.ContentLength should be set so the cascade can check subscribers
    /// without buffering.
    /// </summary>
    public CacheFetchResult(Stream dataStream, CacheEntryMetadata metadata)
    {
        DataStream = dataStream ?? throw new ArgumentNullException(nameof(dataStream));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    public void Dispose()
    {
        DataStream?.Dispose();
    }
}
