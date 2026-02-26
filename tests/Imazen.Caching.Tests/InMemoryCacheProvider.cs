using System.Collections.Concurrent;
using Imazen.Caching;

namespace Imazen.Caching.Tests;

/// <summary>
/// Simple in-memory ICacheProvider for testing. Thread-safe via ConcurrentDictionary.
/// </summary>
public class InMemoryCacheProvider : ICacheProvider
{
    private readonly ConcurrentDictionary<string, (byte[] Data, CacheEntryMetadata Metadata)> _store = new();
    public int FetchCount;
    public int StoreCount;

    /// <summary>
    /// Controls what this provider's WantsToStore returns.
    /// Default: always wants fresh results and external hits.
    /// Set to false to simulate a provider that rejects data (e.g., full disk).
    /// </summary>
    public bool AcceptsFreshResults { get; set; } = true;
    public bool AcceptsExternalHits { get; set; } = true;

    /// <summary>
    /// Optional: reject entries larger than this many bytes. -1 = no limit.
    /// </summary>
    public long MaxAcceptableBytes { get; set; } = -1;

    public string Name { get; }
    public CacheProviderCapabilities Capabilities { get; }

    public InMemoryCacheProvider(string name, bool requiresInline = false, string latencyZone = "local")
    {
        Name = name;
        Capabilities = new CacheProviderCapabilities
        {
            RequiresInlineExecution = requiresInline,
            LatencyZone = latencyZone
        };
    }

    public ValueTask<CacheFetchResult?> FetchAsync(CacheKey key, CancellationToken ct = default)
    {
        Interlocked.Increment(ref FetchCount);
        var path = key.ToStoragePath();
        if (_store.TryGetValue(path, out var entry))
        {
            return new ValueTask<CacheFetchResult?>(new CacheFetchResult(entry.Data, entry.Metadata));
        }
        return new ValueTask<CacheFetchResult?>((CacheFetchResult?)null);
    }

    public ValueTask StoreAsync(CacheKey key, byte[] data, CacheEntryMetadata metadata, CancellationToken ct = default)
    {
        Interlocked.Increment(ref StoreCount);
        _store[key.ToStoragePath()] = (data, metadata);
        return default;
    }

    public bool WantsToStore(CacheKey key, long sizeBytes, CacheStoreReason reason)
    {
        if (MaxAcceptableBytes >= 0 && sizeBytes > MaxAcceptableBytes) return false;

        return reason switch
        {
            CacheStoreReason.FreshlyCreated => AcceptsFreshResults,
            CacheStoreReason.ExternalHit => AcceptsExternalHits,
            _ => true
        };
    }

    public ValueTask<bool> InvalidateAsync(CacheKey key, CancellationToken ct = default)
    {
        return new ValueTask<bool>(_store.TryRemove(key.ToStoragePath(), out _));
    }

    public ValueTask<int> PurgeBySourceAsync(ReadOnlyMemory<byte> sourceHash, CancellationToken ct = default)
    {
        // Simple prefix-based purge
        var prefix = CacheKey.FromStrings("dummy", "dummy").SourcePrefix();
        // For testing, just clear everything
        var count = _store.Count;
        _store.Clear();
        return new ValueTask<int>(count);
    }

    public bool ProbablyContains(CacheKey key) => _store.ContainsKey(key.ToStoragePath());

    public ValueTask<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        return new ValueTask<bool>(true);
    }

    public bool Contains(CacheKey key) => _store.ContainsKey(key.ToStoragePath());
    public int Count => _store.Count;
}

/// <summary>
/// A slow in-memory provider that simulates cloud latency.
/// </summary>
public class SlowCacheProvider : ICacheProvider
{
    private readonly InMemoryCacheProvider _inner;
    private readonly TimeSpan _delay;

    public string Name => _inner.Name;
    public CacheProviderCapabilities Capabilities => _inner.Capabilities;

    public SlowCacheProvider(string name, TimeSpan delay, string latencyZone = "cloud:us-east-1")
    {
        _delay = delay;
        _inner = new InMemoryCacheProvider(name, requiresInline: false, latencyZone: latencyZone);
    }

    public async ValueTask<CacheFetchResult?> FetchAsync(CacheKey key, CancellationToken ct = default)
    {
        await Task.Delay(_delay, ct);
        return await _inner.FetchAsync(key, ct);
    }

    public async ValueTask StoreAsync(CacheKey key, byte[] data, CacheEntryMetadata metadata, CancellationToken ct = default)
    {
        await Task.Delay(_delay, ct);
        await _inner.StoreAsync(key, data, metadata, ct);
    }

    public bool WantsToStore(CacheKey key, long sizeBytes, CacheStoreReason reason) =>
        _inner.WantsToStore(key, sizeBytes, reason);

    public ValueTask<bool> InvalidateAsync(CacheKey key, CancellationToken ct = default) => _inner.InvalidateAsync(key, ct);
    public ValueTask<int> PurgeBySourceAsync(ReadOnlyMemory<byte> sourceHash, CancellationToken ct = default) => _inner.PurgeBySourceAsync(sourceHash, ct);
    public bool ProbablyContains(CacheKey key) => _inner.ProbablyContains(key);
    public ValueTask<bool> HealthCheckAsync(CancellationToken ct = default) => _inner.HealthCheckAsync(ct);
    public int FetchCount => _inner.FetchCount;
    public int StoreCount => _inner.StoreCount;
}
