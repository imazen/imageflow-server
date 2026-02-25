using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Imazen.Caching;

/// <summary>
/// Multi-tier sequential cache cascade implementing ICacheEngine.
///
/// Architecture:
/// - Sequential tiers (no parallel racing, no ref-counting)
/// - Bloom filter gates cloud provider reads (skip if definitely not present)
/// - Request coalescing prevents thundering herd on factory calls
/// - Async upload queue for deferred writes to slow tiers (disk/cloud)
/// - Backpressure: upload queue is byte-bounded, drops on overflow
/// - Every produced image is stored to ALL tiers (no popularity filtering)
/// </summary>
public sealed class CacheCascade : ICacheEngine
{
    private readonly CascadeConfig _config;
    private readonly Dictionary<string, ICacheProvider> _providers = new();
    private readonly List<string> _providerOrder = new();
    private readonly RotatingBloomFilter _bloom;
    private readonly RequestCoalescer? _coalescer;
    private readonly AsyncUploadQueue _uploadQueue;
    private volatile bool _disposed;

    public CacheCascade(CascadeConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _bloom = new RotatingBloomFilter(
            config.BloomFilterEstimatedItems,
            config.BloomFilterFalsePositiveRate,
            config.BloomFilterSlots);
        _coalescer = config.EnableRequestCoalescing ? new RequestCoalescer() : null;
        _uploadQueue = new AsyncUploadQueue(config.MaxUploadQueueBytes);
    }

    /// <summary>
    /// Register a provider. Must be called before any cache operations.
    /// Providers should be registered in the order listed in CascadeConfig.Providers.
    /// </summary>
    public void RegisterProvider(ICacheProvider provider)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        if (_providers.ContainsKey(provider.Name))
            throw new InvalidOperationException($"Provider '{provider.Name}' already registered");

        _providers[provider.Name] = provider;

        // Maintain the config-specified order
        if (_config.Providers.Contains(provider.Name) && !_providerOrder.Contains(provider.Name))
        {
            _providerOrder.Add(provider.Name);
        }
    }

    /// <inheritdoc />
    public async ValueTask<CacheResult> GetOrCreateAsync(
        CacheKey key,
        Func<CancellationToken, ValueTask<(byte[] Data, CacheEntryMetadata Metadata)?>> factory,
        CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CacheCascade));

        var sw = Stopwatch.StartNew();
        var stringKey = key.ToStringKey();

        // 1. Try fetch from all tiers
        var (fetchResult, hitProviderIndex, hitProviderName) = await TryFetchAsync(key, stringKey, ct).ConfigureAwait(false);

        if (fetchResult != null)
        {
            sw.Stop();
            var status = ClassifyHitStatus(hitProviderName!);
            FireEvent(CacheEventKind.Hit, key, hitProviderName!, sw.Elapsed);

            // Async-replicate to missed faster tiers
            if (hitProviderIndex > 0)
            {
                ReplicateToFasterTiers(key, stringKey, fetchResult.Data, fetchResult.Metadata, hitProviderIndex);
            }

            return CacheResult.Hit(status, fetchResult.Data, fetchResult.Metadata.ContentType,
                hitProviderName!, sw.Elapsed);
        }

        // 2. Cache miss — invoke factory (with optional coalescing)
        FireEvent(CacheEventKind.Miss, key, "cascade", sw.Elapsed);

        if (_coalescer != null)
        {
            var (success, result) = await _coalescer.TryExecuteAsync<(byte[]? Data, CacheEntryMetadata? Metadata, bool WasCreated)>(
                stringKey,
                _config.CoalescingTimeoutMs,
                async innerCt =>
                {
                    // Double-check: another coalesced request may have populated the cache
                    var (recheck, _, _) = await TryFetchAsync(key, stringKey, innerCt).ConfigureAwait(false);
                    if (recheck != null)
                    {
                        return (recheck.Data, recheck.Metadata, false);
                    }

                    var produced = await factory(innerCt).ConfigureAwait(false);
                    if (produced == null) return (null, null, true);
                    return (produced.Value.Data, produced.Value.Metadata, true);
                },
                ct).ConfigureAwait(false);

            if (!success)
            {
                return CacheResult.TimeoutResult();
            }

            if (result.Data == null)
            {
                return CacheResult.ErrorResult("Factory returned null");
            }

            sw.Stop();
            if (result.WasCreated)
            {
                StoreToAllTiers(key, stringKey, result.Data, result.Metadata!);
            }

            return CacheResult.Created(result.Data, result.Metadata?.ContentType, sw.Elapsed);
        }
        else
        {
            // No coalescing — just call factory directly
            var produced = await factory(ct).ConfigureAwait(false);
            if (produced == null)
            {
                return CacheResult.ErrorResult("Factory returned null");
            }

            sw.Stop();
            StoreToAllTiers(key, stringKey, produced.Value.Data, produced.Value.Metadata);
            return CacheResult.Created(produced.Value.Data, produced.Value.Metadata.ContentType, sw.Elapsed);
        }
    }

    /// <inheritdoc />
    public async ValueTask InvalidateAsync(CacheKey key, CancellationToken ct = default)
    {
        foreach (var name in _providerOrder)
        {
            if (_providers.TryGetValue(name, out var provider))
            {
                try
                {
                    await provider.InvalidateAsync(key, ct).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort invalidation across all tiers
                }
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<int> PurgeBySourceAsync(ReadOnlyMemory<byte> sourceHash, CancellationToken ct = default)
    {
        int total = 0;
        foreach (var name in _providerOrder)
        {
            if (_providers.TryGetValue(name, out var provider))
            {
                try
                {
                    total += await provider.PurgeBySourceAsync(sourceHash, ct).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort purge across all tiers
                }
            }
        }
        return total;
    }

    private async ValueTask<(CacheFetchResult? Result, int ProviderIndex, string? ProviderName)> TryFetchAsync(
        CacheKey key, string stringKey, CancellationToken ct)
    {
        // Check upload queue first (in-flight writes are readable)
        for (int i = 0; i < _providerOrder.Count; i++)
        {
            var providerName = _providerOrder[i];
            var queueKey = stringKey + ":" + providerName;
            if (_uploadQueue.TryGet(queueKey, out var queueData, out var queueMeta) && queueData != null && queueMeta != null)
            {
                return (new CacheFetchResult(queueData, queueMeta), i, providerName);
            }
        }

        // Also check for a general queue key
        if (_uploadQueue.TryGet(stringKey, out var generalData, out var generalMeta) && generalData != null && generalMeta != null)
        {
            return (new CacheFetchResult(generalData, generalMeta), 0, "upload-queue");
        }

        // Sequential fetch through providers
        for (int i = 0; i < _providerOrder.Count; i++)
        {
            var providerName = _providerOrder[i];
            if (!_providers.TryGetValue(providerName, out var provider)) continue;

            // Cloud providers: skip if bloom says definitely not present
            if (!provider.Capabilities.IsLocal)
            {
                var bloomKey = stringKey + ":" + providerName;
                if (!_bloom.ProbablyContains(bloomKey))
                {
                    continue; // Definitely not there
                }
            }

            try
            {
                var result = await provider.FetchAsync(key, ct).ConfigureAwait(false);
                if (result != null)
                {
                    return (result, i, providerName);
                }
            }
            catch
            {
                FireEvent(CacheEventKind.Error, key, providerName, detail: "Fetch failed");
            }
        }

        return (null, -1, null);
    }

    private void StoreToAllTiers(CacheKey key, string stringKey, byte[] data, CacheEntryMetadata metadata)
    {
        foreach (var providerName in _providerOrder)
        {
            if (!_providers.TryGetValue(providerName, out var provider)) continue;

            // Update bloom filter for cloud providers
            if (!provider.Capabilities.IsLocal)
            {
                _bloom.Insert(stringKey + ":" + providerName);
            }

            if (provider.Capabilities.RequiresInlineExecution)
            {
                // Inline store (memory cache) — fire-and-forget the ValueTask
                try
                {
                    var task = provider.StoreAsync(key, data, metadata);
                    if (!task.IsCompletedSuccessfully)
                    {
                        task.AsTask().ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                FireEvent(CacheEventKind.Error, key, providerName,
                                    detail: $"Inline store failed: {t.Exception?.GetBaseException().Message}");
                        }, TaskScheduler.Default);
                    }
                }
                catch
                {
                    FireEvent(CacheEventKind.Error, key, providerName, detail: "Inline store threw");
                }
            }
            else
            {
                // Async store via upload queue
                var queueKey = stringKey + ":" + providerName;
                var capturedProvider = provider;
                var capturedKey = key;
                var result = _uploadQueue.TryEnqueue(queueKey, data, metadata,
                    (d, m, ct2) => capturedProvider.StoreAsync(capturedKey, d, m, ct2).AsTask());

                if (result == EnqueueResult.QueueFull)
                {
                    FireEvent(CacheEventKind.StoreDropped, key, providerName, detail: "Upload queue full");
                }
                else if (result == EnqueueResult.Enqueued)
                {
                    FireEvent(CacheEventKind.Store, key, providerName);
                }
            }
        }
    }

    private void ReplicateToFasterTiers(CacheKey key, string stringKey, byte[] data,
        CacheEntryMetadata metadata, int hitIndex)
    {
        for (int i = 0; i < hitIndex; i++)
        {
            var providerName = _providerOrder[i];
            if (!_providers.TryGetValue(providerName, out var provider)) continue;

            if (provider.Capabilities.RequiresInlineExecution)
            {
                try
                {
                    var task = provider.StoreAsync(key, data, metadata);
                    if (!task.IsCompletedSuccessfully)
                    {
                        task.AsTask().ContinueWith(_ => { }, TaskScheduler.Default);
                    }
                }
                catch
                {
                    // Best-effort replication
                }
            }
            else
            {
                var queueKey = stringKey + ":" + providerName;
                var capturedProvider = provider;
                var capturedKey = key;
                _uploadQueue.TryEnqueue(queueKey, data, metadata,
                    (d, m, ct2) => capturedProvider.StoreAsync(capturedKey, d, m, ct2).AsTask());
            }

            FireEvent(CacheEventKind.Replicate, key, providerName);
        }
    }

    private CacheResultStatus ClassifyHitStatus(string providerName)
    {
        if (providerName == "upload-queue") return CacheResultStatus.QueueHit;

        if (_providers.TryGetValue(providerName, out var provider))
        {
            if (provider.Capabilities.RequiresInlineExecution) return CacheResultStatus.MemoryHit;
            if (provider.Capabilities.IsLocal) return CacheResultStatus.DiskHit;
            return CacheResultStatus.CloudHit;
        }

        return CacheResultStatus.DiskHit; // fallback
    }

    private void FireEvent(CacheEventKind kind, CacheKey key, string providerName,
        TimeSpan? latency = null, string? detail = null)
    {
        try
        {
            _config.OnCacheEvent?.Invoke(new CacheEvent(kind, key, providerName, latency, detail));
        }
        catch
        {
            // Never let event handlers crash the cascade
        }
    }

    /// <summary>
    /// Access the bloom filter for diagnostics or testing.
    /// </summary>
    public RotatingBloomFilter BloomFilter => _bloom;

    /// <summary>
    /// Access the upload queue for diagnostics or testing.
    /// </summary>
    public AsyncUploadQueue UploadQueue => _uploadQueue;

    /// <summary>
    /// Access the coalescer for diagnostics or testing.
    /// </summary>
    public RequestCoalescer? Coalescer => _coalescer;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _uploadQueue.Dispose();
        _coalescer?.Dispose();
    }
}
