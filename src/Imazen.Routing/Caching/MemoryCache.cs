using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Extensibility.Support;

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
public class MemoryCache(MemoryCacheOptions options) : IBlobCache
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
        removed.BlobWrapper.Dispose();
        Interlocked.Add(ref memoryUsedSync, -removed.BlobWrapper.EstimateAllocatedBytes ?? 0);
        Interlocked.Decrement(ref itemCountSync);
        return true;
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
                nextCandidateIndex++; // Skip this item
                continue; // This item is too new to evict.
            }
            var _ = TryRemove(candidate.CacheKey, out var _);
            nextCandidateIndex++;
        }
        return true;
    }

    private async Task<bool> TryAdd(string cacheKey, IBlobWrapper blob)
    {


        if (blob.EstimateAllocatedBytes == null)
        {
            throw new InvalidOperationException("Cannot cache a blob that does not have an EstimateAllocatedBytes");
        }
        IBlobWrapper? existingBlob = null;
        if (__cache.TryGetValue(cacheKey, out var oldEntry))
        {
            existingBlob = oldEntry.BlobWrapper;
            if (existingBlob == blob)
            {
                return false; // Why are we adding the same blob twice? 
            }
        }
        var replacementSizeDifference = (long)blob.EstimateAllocatedBytes! - (existingBlob?.EstimateAllocatedBytes ?? 0);
        if (blob.EstimateAllocatedBytes > options.MaxItemSizeKb * 1024)
        {
            return false;
        }
        if (!TryEnsureCapacity(replacementSizeDifference))
        {
            return false; // Can't make space? That's odd.
        }
        if (!blob.IsReusable)
        {
            //await blob.EnsureReusable();
             throw new InvalidOperationException("Cannot cache a blob that is not natively reusable");
        }
        var entry = new CacheEntry(cacheKey, blob, UsageTracker.Create());
        if (entry == __cache.AddOrUpdate(cacheKey, entry, (_, existing) => existing with { BlobWrapper = blob }))
        {
            Interlocked.Increment(ref itemCountSync);
            Interlocked.Add(ref memoryUsedSync, blob.EstimateAllocatedBytes ?? 0);
            itemCountSync++;
            return true;
        }
        Interlocked.Add(ref memoryUsedSync, replacementSizeDifference);
        
        return false;
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
            await TryAdd(e.OriginalRequest.CacheKeyHashString, blob.ForkReference());
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
}
