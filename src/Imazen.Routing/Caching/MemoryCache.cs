using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Extensibility.Support;
using Imazen.Routing.Serving;
using Microsoft.Extensions.Logging;

namespace Imazen.Routing.Caching;


public record MemoryCacheOptions(string UniqueName, int MaxMemoryUtilizationMb, int MaxItems, int MaxItemSizeKb, TimeSpan MinKeepNewItemsFor);

public class UsageTracker
{
    
    public required DateTimeOffset CreatedUtc { get; init; }
    public required DateTimeOffset LastAccessedUtc;
    public int AccessCount { get; private set; }

    public void Used()
    {
        LastAccessedUtc = DateTimeOffset.UtcNow;
        AccessCount++;
    }

    public static UsageTracker Create()
    {
        var now = DateTimeOffset.UtcNow;
        return new UsageTracker
        {
            CreatedUtc = now,
            LastAccessedUtc = now
        };
    }
}

public static class AsyncEnumerableExtensions
{
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public static async IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IEnumerable<T> enumerable)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        foreach (var item in enumerable)
        {
            yield return item;
        }
    }
}

/// <summary>
/// This is essential for managing thundering herds problems, even if items only stick around for a few seconds.
/// It requests inline execution in capabilities.
///
/// Ideally, we'll implement something better than LRU.
/// Items that are under the MinKeepNewItemsFor grace period should be kept in a separate list.
/// They can graduate to the main list if they are accessed more than x times
/// </summary>
public class MemoryCache(MemoryCacheOptions options, IReLogger<MemoryCache> logger) : IBlobCache, IHostedImageServerService
{
    private record CacheEntry(string CacheKey, IBlobWrapper BlobWrapper, UsageTracker UsageTracker)
    {
        internal MemoryCacheStorageReference GetReference()
        {
            return new MemoryCacheStorageReference(CacheKey);
        }
    }
    
    private record MemoryCacheStorageReference(string CacheKey) : IBlobStorageReference
    {
        public string GetFullyQualifiedRepresentation()
        {
            return $"MemoryCache:{CacheKey}";
        }

        public int EstimateAllocatedBytesRecursive => 24 + CacheKey.EstimateMemorySize(true);
    }
    ConcurrentDictionary<string, CacheEntry> __cache = new ConcurrentDictionary<string, CacheEntry>();
    
    public string UniqueName => options.UniqueName;


    public BlobCacheCapabilities InitialCacheCapabilities { get; } = new BlobCacheCapabilities
    {
        CanFetchMetadata = true,
        CanFetchData = true,
        CanConditionalFetch = false,
        CanPut = true,
        CanConditionalPut = false,
        CanDelete = true,
        CanSearchByTag = true,
        CanPurgeByTag = true,
        CanReceiveEvents = true,
        SupportsHealthCheck = true,
        SubscribesToRecentRequest = true,
        SubscribesToExternalHits = true,
        SubscribesToFreshResults = true,
        RequiresInlineExecution = true, // Unsure if this is true
        FixedSize = true
    };


    public void Initialize(BlobCacheSupportData supportData)
    {
        
    }

    public Task<CacheFetchResult> CacheFetch(IBlobCacheRequest request, CancellationToken cancellationToken = default)
    {
        if (__cache.TryGetValue(request.CacheKeyHashString, out var entry))
        {
            entry.UsageTracker.Used();
            return Task.FromResult(BlobCacheFetchFailure.OkResult(entry.BlobWrapper.ForkReference()));
        }

        return Task.FromResult(BlobCacheFetchFailure.MissResult(this, this));
    }

    private long memoryUsedSync;
    private long itemCountSync;
    private bool TryRemove(string cacheKey, [MaybeNullWhen(false)] out CacheEntry removed)
    {
        if (!__cache.TryRemove(cacheKey, out removed)) return false;
        var bytes = removed.BlobWrapper.EstimateAllocatedBytes;
        removed.BlobWrapper.Dispose();
        Interlocked.Add(ref memoryUsedSync, -bytes ?? 0);
        Interlocked.Decrement(ref itemCountSync);
        return true;
    }

    private void ClearAll()
    {
        logger.LogInformation("Clearing all cache entries");
        while (true)
        {
            var next = __cache.FirstOrDefault();
            if (next.Key == null) break;
            TryRemove(next.Key, out _);
        }
        if (!__cache.IsEmpty) logger.LogWarning("Failed to clear all cache entries");

        if (__cache.IsEmpty && (memoryUsedSync != 0 || itemCountSync != 0))
            logger.LogWarning("Memory usage accounting error after ClearAll: values are {MemoryUsed} bytes, {ItemCount} items", memoryUsedSync, itemCountSync);
    }
    
    private bool TryEnsureCapacity(long size)
    {
        List<CacheEntry>? snapshotOfEntries = null;
        int nextCandidateIndex = 0;
        while (itemCountSync > options.MaxItems || memoryUsedSync + size > options.MaxMemoryUtilizationMb * 1024 * 1024)
        {
            if (snapshotOfEntries == null)
            {
                // Sort by least total access count, then by least recently accessed
                snapshotOfEntries = __cache.Values
                    .OrderBy(entry => entry.UsageTracker.AccessCount)
                    .ThenBy(entry => entry.UsageTracker.LastAccessedUtc).ToList();
            }
            if (nextCandidateIndex >= snapshotOfEntries.Count)
            {
                return false; // We've run out of candidates. We can't make space.
            }
            var candidate = snapshotOfEntries[nextCandidateIndex++];
            if (candidate.UsageTracker.LastAccessedUtc > DateTimeOffset.UtcNow - options.MinKeepNewItemsFor)
            {
                continue; // This item is too new to evict.
            }
            TryRemove(candidate.CacheKey, out _);
        }
        return true;
    }

    private Task<bool> TryAdd(string cacheKey, IBlobWrapper unforkedBlobWrapper)
    {
        var estimateAllocatedBytes = unforkedBlobWrapper.EstimateAllocatedBytes;
        if (estimateAllocatedBytes == null)
        {
            throw new InvalidOperationException("Cannot cache a blob that does not have an EstimateAllocatedBytes");
        }

        if (__cache.TryGetValue(cacheKey, out _))
        {
            // Already exists with this key — don't replace.
            return Task.FromResult(false);
        }
        if (estimateAllocatedBytes > options.MaxItemSizeKb * 1024)
        {
            return Task.FromResult(false);
        }
        if (!TryEnsureCapacity((long)estimateAllocatedBytes!))
        {
            return Task.FromResult(false); // Can't make space? That's odd.
        }
        if (!unforkedBlobWrapper.IsReusable)
        {
            // await unforkedBlobWrapper.EnsureReusable();
            throw new InvalidOperationException("Cannot cache a blob that is not natively reusable");
        }
        var entry = new CacheEntry(cacheKey, unforkedBlobWrapper.ForkReference(), UsageTracker.Create());
        if (entry == __cache.AddOrUpdate(cacheKey, entry, (_, existing) => existing))
        {
            Interlocked.Increment(ref itemCountSync);
            Interlocked.Add(ref memoryUsedSync, estimateAllocatedBytes ?? 0);
            return Task.FromResult(true);
        }
        else
        {
            entry.BlobWrapper.Dispose();
            // If we updated instead of leaving in place, we would call Interlocked.Add(ref memoryUsedSync, replacementSizeDifference);
        }
        return Task.FromResult(false);
    }

    private void IncrementUsage(string cacheKeyHashString)
    {
        if (__cache.TryGetValue(cacheKeyHashString, out var entry))
        {
            entry.UsageTracker.Used();
        }
    }
    private IEnumerable<CacheEntry> AllEntries => __cache.Values;
    public async Task<CodeResult> CachePut(ICacheEventDetails e, CancellationToken cancellationToken = default)
    {
        if (e.Result?.TryUnwrap(out var blob) == true)
        {
            await TryAdd(e.OriginalRequest.CacheKeyHashString, blob);
        }

        return CodeResult.Ok();
    }

    public Task<CodeResult<IAsyncEnumerable<IBlobStorageReference>>> CacheSearchByTag(SearchableBlobTag tag, CancellationToken cancellationToken = default)
    {
        // we iterate instead of having an index, this is almost never called (we think)
        var results = 
            AllEntries.Where(entry => entry.BlobWrapper.Attributes.StorageTags?.Contains(tag) == true)
                .Select(entry => (IBlobStorageReference)entry.GetReference()).AsAsyncEnumerable();
        return Task.FromResult(CodeResult<IAsyncEnumerable<IBlobStorageReference>>.Ok(results));
    }

    public Task<CodeResult<IAsyncEnumerable<CodeResult<IBlobStorageReference>>>> CachePurgeByTag(SearchableBlobTag tag, CancellationToken cancellationToken = default)
    {
        // As we delete items from the cache, build a list of the deleted items
        // so we can return them to the caller.
        var deleted = new List<CodeResult<IBlobStorageReference>>();
        foreach (var entry in AllEntries.Where(entry => entry.BlobWrapper.Attributes.StorageTags?.Contains(tag) == true))
        {
            if (TryRemove(entry.CacheKey, out var removed))
            {
                deleted.Add(CodeResult<IBlobStorageReference>.Ok(removed.GetReference()));
            }
        }
        return Task.FromResult(CodeResult<IAsyncEnumerable<CodeResult<IBlobStorageReference>>>.Ok(deleted.AsAsyncEnumerable()));
    }

    public Task<CodeResult> CacheDelete(IBlobStorageReference reference, CancellationToken cancellationToken = default)
    {
        if (reference is MemoryCacheStorageReference memoryCacheStorageReference)
        {
            if (TryRemove(memoryCacheStorageReference.CacheKey, out _))
            {
                return Task.FromResult(CodeResult.Ok());
            }
        }

        return Task.FromResult(CodeResult.Err(404));
    }

    public Task<CodeResult> OnCacheEvent(ICacheEventDetails e, CancellationToken cancellationToken = default)
    {
        // notify usage trackers
        if (e.ExternalCacheHit != null)
        {
            IncrementUsage(e.OriginalRequest.CacheKeyHashString);
        }
        return Task.FromResult(CodeResult.Ok());
    }

    

    public ValueTask<IBlobCacheHealthDetails> CacheHealthCheck(CancellationToken cancellationToken = default)
    {
        // We could be low on memory, but we wouldn't turn off features, we'd just evict smarter.
        return new ValueTask<IBlobCacheHealthDetails>(BlobCacheHealthDetails.FullHealth(InitialCacheCapabilities));
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ClearAll();
        return Task.CompletedTask;
    }
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

}
