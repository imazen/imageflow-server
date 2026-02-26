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
/// - Subscription model: providers declare whether they want data via WantsToStore
///   with per-provider reasons (Missed vs NotQueried) so providers can make
///   smart decisions about re-accepting data they may have evicted
/// - Replication is direction-agnostic: any tier can replicate to any other
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

    /// <summary>
    /// Result of TryFetchAsync, including which providers were checked and missed.
    /// </summary>
    private readonly struct FetchOutcome
    {
        public CacheFetchResult? Result { get; init; }
        public string? HitProviderName { get; init; }
        /// <summary>
        /// Providers that were checked and definitely don't have the entry.
        /// Includes bloom-gated skips (bloom negative = definite miss).
        /// Excludes providers that weren't reached because a faster tier hit.
        /// </summary>
        public HashSet<string>? CheckedAndMissed { get; init; }
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
        var outcome = await TryFetchAsync(key, stringKey, ct).ConfigureAwait(false);

        if (outcome.Result != null)
        {
            sw.Stop();
            var status = ClassifyHitStatus(outcome.HitProviderName!);
            FireEvent(CacheEventKind.Hit, key, outcome.HitProviderName!, sw.Elapsed);

            // Ask non-hit providers if they want this data.
            // Each provider gets Missed or NotQueried depending on whether
            // the cascade actually checked it during this fetch.
            var subscribers = GetHitSubscribers(key, stringKey, outcome.Result.Data.Length,
                outcome.HitProviderName!, outcome.CheckedAndMissed);

            if (subscribers.Count > 0)
            {
                DistributeToSubscribers(key, stringKey, outcome.Result.Data,
                    outcome.Result.Metadata, subscribers);
            }

            return CacheResult.BufferedHit(status, outcome.Result.Data,
                outcome.Result.Metadata.ContentType, outcome.HitProviderName!, sw.Elapsed);
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
                    var recheck = await TryFetchAsync(key, stringKey, innerCt).ConfigureAwait(false);
                    if (recheck.Result != null)
                    {
                        return (recheck.Result.Data, recheck.Result.Metadata, false);
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
                StoreToFreshSubscribers(key, stringKey, result.Data, result.Metadata!);
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
            StoreToFreshSubscribers(key, stringKey, produced.Value.Data, produced.Value.Metadata);
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

    private async ValueTask<FetchOutcome> TryFetchAsync(
        CacheKey key, string stringKey, CancellationToken ct)
    {
        HashSet<string>? checkedAndMissed = null;

        // Sequential fetch through providers, checking upload queue at each tier
        for (int i = 0; i < _providerOrder.Count; i++)
        {
            var providerName = _providerOrder[i];

            // Check upload queue for this provider (in-flight writes are readable)
            var queueKey = stringKey + ":" + providerName;
            if (_uploadQueue.TryGet(queueKey, out var queueData, out var queueMeta) && queueData != null && queueMeta != null)
            {
                return new FetchOutcome
                {
                    Result = new CacheFetchResult(queueData, queueMeta),
                    HitProviderName = providerName,
                    CheckedAndMissed = checkedAndMissed
                };
            }

            if (!_providers.TryGetValue(providerName, out var provider)) continue;

            // Cloud providers: skip if bloom says definitely not present
            if (!provider.Capabilities.IsLocal)
            {
                var bloomKey = stringKey + ":" + providerName;
                if (!_bloom.ProbablyContains(bloomKey))
                {
                    // Bloom negative = definite miss. We know it's not there.
                    checkedAndMissed ??= new HashSet<string>();
                    checkedAndMissed.Add(providerName);
                    continue;
                }
            }

            try
            {
                var result = await provider.FetchAsync(key, ct).ConfigureAwait(false);
                if (result != null)
                {
                    return new FetchOutcome
                    {
                        Result = result,
                        HitProviderName = providerName,
                        CheckedAndMissed = checkedAndMissed
                    };
                }

                // Direct miss: provider was queried and returned null
                checkedAndMissed ??= new HashSet<string>();
                checkedAndMissed.Add(providerName);
            }
            catch
            {
                FireEvent(CacheEventKind.Error, key, providerName, detail: "Fetch failed");
                // Fetch failure counts as checked-and-missed (we tried, it failed)
                checkedAndMissed ??= new HashSet<string>();
                checkedAndMissed.Add(providerName);
            }
        }

        // Check for a general queue key (not tier-specific)
        if (_uploadQueue.TryGet(stringKey, out var generalData, out var generalMeta) && generalData != null && generalMeta != null)
        {
            return new FetchOutcome
            {
                Result = new CacheFetchResult(generalData, generalMeta),
                HitProviderName = "upload-queue",
                CheckedAndMissed = checkedAndMissed
            };
        }

        return new FetchOutcome
        {
            Result = null,
            HitProviderName = null,
            CheckedAndMissed = checkedAndMissed
        };
    }

    /// <summary>
    /// After a cache hit, ask all non-hit providers if they want the data.
    /// Each provider gets a per-provider reason:
    /// - Missed: provider was checked during fetch, or bloom filter says definitely not there
    /// - NotQueried: provider wasn't checked and bloom says "maybe" (may still have it)
    /// For non-local providers not reached during fetch, we check their bloom filter
    /// to distinguish "definitely doesn't have it" (Missed) from "might still have it" (NotQueried).
    /// This is zero-cost (in-memory hash check) and enables direction-agnostic replication.
    /// </summary>
    private List<(string Name, ICacheProvider Provider)> GetHitSubscribers(
        CacheKey key, string stringKey, long sizeBytes, string hitProviderName,
        HashSet<string>? checkedAndMissed)
    {
        var subscribers = new List<(string, ICacheProvider)>();

        foreach (var providerName in _providerOrder)
        {
            if (providerName == hitProviderName) continue;
            if (!_providers.TryGetValue(providerName, out var provider)) continue;

            CacheStoreReason reason;
            if (checkedAndMissed != null && checkedAndMissed.Contains(providerName))
            {
                // Provider was checked during fetch (directly or bloom-gated) and missed
                reason = CacheStoreReason.Missed;
            }
            else if (!provider.Capabilities.IsLocal)
            {
                // Non-local provider we didn't reach during fetch.
                // Check bloom filter: negative = definite miss (safe to classify as Missed)
                var bloomKey = stringKey + ":" + providerName;
                reason = !_bloom.ProbablyContains(bloomKey)
                    ? CacheStoreReason.Missed
                    : CacheStoreReason.NotQueried;
            }
            else
            {
                reason = CacheStoreReason.NotQueried;
            }

            if (provider.WantsToStore(key, sizeBytes, reason))
            {
                subscribers.Add((providerName, provider));
            }
        }

        return subscribers;
    }

    /// <summary>
    /// Store freshly-created data to all providers that want it.
    /// All providers get FreshlyCreated since nobody had this entry.
    /// </summary>
    private void StoreToFreshSubscribers(CacheKey key, string stringKey, byte[] data, CacheEntryMetadata metadata)
    {
        var subscribers = new List<(string Name, ICacheProvider Provider)>();

        foreach (var providerName in _providerOrder)
        {
            if (!_providers.TryGetValue(providerName, out var provider)) continue;
            if (provider.WantsToStore(key, data.Length, CacheStoreReason.FreshlyCreated))
            {
                subscribers.Add((providerName, provider));
            }
        }

        if (subscribers.Count == 0) return;

        DistributeToSubscribers(key, stringKey, data, metadata, subscribers);
    }

    /// <summary>
    /// Distribute data to the given list of subscriber providers.
    /// Inline providers get stored synchronously. Async providers go through the upload queue.
    /// Direction-agnostic: any tier can store to any other tier.
    /// </summary>
    private void DistributeToSubscribers(CacheKey key, string stringKey, byte[] data,
        CacheEntryMetadata metadata, List<(string Name, ICacheProvider Provider)> subscribers)
    {
        foreach (var (providerName, provider) in subscribers)
        {
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
                    FireEvent(CacheEventKind.Store, key, providerName);
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

    // Well-known key for bloom filter persistence.
    // Uses a reserved prefix ("__meta") that won't collide with content keys.
    private static readonly CacheKey BloomPersistenceKey =
        CacheKey.FromStrings("__meta/bloom", "__meta/bloom/state");

    /// <summary>
    /// Find the first local non-inline provider (disk tier) for bloom filter persistence.
    /// Returns null if no suitable provider exists.
    /// </summary>
    private ICacheProvider? GetBloomPersistenceProvider()
    {
        foreach (var name in _providerOrder)
        {
            if (!_providers.TryGetValue(name, out var provider)) continue;
            if (provider.Capabilities.IsLocal && !provider.Capabilities.RequiresInlineExecution)
                return provider;
        }
        return null;
    }

    /// <summary>
    /// Save the bloom filter to the first local non-inline provider (disk).
    /// Call periodically and on graceful shutdown.
    /// If no suitable provider exists, this is a no-op.
    /// </summary>
    public async ValueTask CheckpointBloomFilterAsync(CancellationToken ct = default)
    {
        var provider = GetBloomPersistenceProvider();
        if (provider == null) return;

        try
        {
            var data = _bloom.ToBytes();
            await provider.StoreAsync(BloomPersistenceKey, data,
                new CacheEntryMetadata { ContentType = "application/x-bloom-filter" }, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            FireEvent(CacheEventKind.Error, BloomPersistenceKey,
                "cascade", detail: "Bloom filter checkpoint failed");
        }
    }

    /// <summary>
    /// Load the bloom filter from the first local non-inline provider (disk).
    /// Call on startup before serving requests.
    /// If no persisted state exists, the filter starts empty (cloud tiers become
    /// invisible until re-warmed through normal traffic).
    /// </summary>
    public async ValueTask LoadBloomFilterAsync(CancellationToken ct = default)
    {
        var provider = GetBloomPersistenceProvider();
        if (provider == null) return;

        try
        {
            var result = await provider.FetchAsync(BloomPersistenceKey, ct).ConfigureAwait(false);
            if (result?.Data != null)
            {
                _bloom.LoadFromBytes(result.Data);
            }
        }
        catch
        {
            // No persisted state or incompatible format — start empty.
            // Cloud tiers will re-warm through normal traffic.
            FireEvent(CacheEventKind.Error, BloomPersistenceKey,
                "cascade", detail: "Bloom filter load failed, starting empty");
        }
    }

    /// <summary>
    /// OR-merge a bloom filter received from another cluster node.
    /// After merge, this node's bloom filter contains the union of both
    /// nodes' knowledge about cloud contents.
    /// </summary>
    public void MergeBloomFilterFromPeer(byte[] peerBloomData)
    {
        if (peerBloomData == null) throw new ArgumentNullException(nameof(peerBloomData));
        _bloom.MergeFromBytes(peerBloomData);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _uploadQueue.Dispose();
        _coalescer?.Dispose();
    }
}
