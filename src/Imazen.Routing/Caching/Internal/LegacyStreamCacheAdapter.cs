using System.Diagnostics;
using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Concurrency;
using Imazen.Common.Concurrency.BoundedTaskCollection;
using Imazen.Common.Extensibility.StreamCache;
using Imazen.Common.Issues;
using Microsoft.Extensions.Logging;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Imazen.Routing.Caching.Internal;

internal class StreamCacheAdapterOptions
{  
    public int WaitForIdenticalRequestsTimeoutMs { get; set; } = 1000;
    public bool FailRequestsOnEnqueueLockTimeout { get; set; } = true;
    public bool WriteSynchronouslyWhenQueueFull { get; set; } = false;
    public bool EvictOnQueueFull { get; set; } = true;
}
internal class DisposableStreamCacheResult : IStreamCacheResult, IDisposable
{

    public DisposableStreamCacheResult(IConsumableBlob consumableBlob, string status)
    {
        
        Data = consumableBlob.BorrowStream(DisposalPromise.CallerDisposesStreamThenBlob);
        ContentType = consumableBlob.Attributes?.ContentType;
        Status = status;
    }

    public BlobWrapper? Blob { get; init; }
    

    public Stream Data { get; }
    public string? ContentType { get; }
    public string Status { get; }
    
    public void Dispose()
    {
        Data.Dispose();
        Blob?.Dispose();
    }
}
internal class LegacyStreamCacheAdapter : IStreamCache
{
    public LegacyStreamCacheAdapter(IBlobCache cache, StreamCacheAdapterOptions options, IReLogger logger)
    {
        Options = options;
        QueueLocks = new AsyncLockProvider();
        CurrentWrites = new BoundedTaskCollection<BlobTaskItem>(1000);
        this.cache = cache;
        Logger = logger;
    }

    private IReLogger? Logger { get; set; }
    private StreamCacheAdapterOptions Options { get; }
    private readonly IBlobCache cache;
    private readonly IReusableBlobFactory reusableBlobFactory = new SimpleReusableBlobFactory();

    // /// <summary>
    // /// Provides string-based locking for image resizing (not writing, just processing). Prevents duplication of efforts in asynchronous mode, where 'Locks' is not being used.
    // /// </summary>
    private AsyncLockProvider QueueLocks { get;  }

    /// <summary>
    /// Contains all the queued and in-progress writes to the cache. 
    /// </summary>
    private BoundedTaskCollection<BlobTaskItem> CurrentWrites {get; }

    public Task AwaitAllCurrentTasks(CancellationToken cancellationToken)
    {
        return CurrentWrites.AwaitAllCurrentTasks();
    }
    public IEnumerable<IIssue> GetIssues()
    {
        return new List<IIssue>();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task<IStreamCacheResult?> CacheFetch(IBlobCacheRequest cacheRequest,CancellationToken cancellationToken)
    {
        var result = await cache.CacheFetch(cacheRequest, cancellationToken);
        if (result.IsOk)
        {
            using var blobWrapper = result.Unwrap();
            var consumableBlob = await blobWrapper.GetConsumablePromise().IntoConsumableBlob();
            return new DisposableStreamCacheResult(consumableBlob, "Hit");
        }

        return null;
    }

    public async Task<IStreamCacheResult> GetOrCreateBytes(byte[] key, AsyncBytesResult dataProviderCallback,
        CancellationToken cancellationToken,
        bool retrieveContentType)
    {
        var cacheRequest = new BlobCacheRequest(BlobGroup.GeneratedCacheEntry, key);
        
        var wrappedCallback = new Func<CancellationToken, Task<IResult<BlobSuccessResult, HttpStatus>>>(async (ct) =>
        {
            var sw = Stopwatch.StartNew();
            var cacheInputEntry = await dataProviderCallback(ct);
            sw.Stop();
            if (cacheInputEntry == null)
                throw new InvalidOperationException("Null entry provided by dataProviderCallback");
            if (cacheInputEntry.Bytes.Array == null)
                throw new InvalidOperationException("Null entry byte array provided by dataProviderCallback");
            var blobGenerated 
                = new MemoryBlob(cacheInputEntry.Bytes, new BlobAttributes(){
                    ContentType = cacheInputEntry.ContentType}, sw.Elapsed);
            return Result<BlobSuccessResult, HttpStatus>.Ok(new BlobSuccessResult(new BlobWrapper(new LatencyTrackingZone("freshCreate", 1000,true), blobGenerated), "Fresh"));
        });

        var result = await GetOrCreateResult(cacheRequest, wrappedCallback, cancellationToken, retrieveContentType);
        if (result.IsOk)
        {
            using var blobWrapper = result.Unwrap().Blob;
            
            var consumableBlob = await blobWrapper.GetConsumablePromise().IntoConsumableBlob();
            return new DisposableStreamCacheResult(consumableBlob, result.Unwrap().Status);
            
        }
        else
        {
            throw new InvalidOperationException(result.UnwrapError().ToString());
        }
    }
    
    internal record BlobSuccessResult(IBlobWrapper Blob, string Status);

    public async Task<IResult<BlobSuccessResult, IBlobCacheFetchFailure>> GetOrCreateResult(IBlobCacheRequest cacheRequest,
        Func<CancellationToken, Task<IResult<BlobSuccessResult, HttpStatus>>> dataProviderCallback,
        CancellationToken cancellationToken,
        bool retrieveContentType)
    {
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);
        var swGetOrCreateBytes = Stopwatch.StartNew();

        // Tries to fetch the result from disk cache, the memory queue, or create it. If the memory queue has space,
        // the writeCallback() will be executed and the resulting bytes put in a queue for writing to disk.
        // If the memory queue is full, writing to disk will be attempted synchronously.
        // In either case, writing to disk can also fail if the disk cache is full and eviction fails.
        // If the memory queue is full, eviction will be done synchronously and can cause other threads to time out
        // while waiting for QueueLock


        // Tell cleanup what we're using
        // TODO: raise event with all caches for utilization
        // CleanupManager.NotifyUsed(entry);

        // Fast path on disk hit
        var swFileExists = Stopwatch.StartNew();

        var fastResult = await cache.CacheFetch(cacheRequest.WithFailFast(true), cancellationToken);
        if (fastResult.IsOk) return Result<BlobSuccessResult, IBlobCacheFetchFailure>.Ok(new BlobSuccessResult(fastResult.Unwrap(), "DiskHit"));

        // Just continue on creating the file. It must have been deleted between the calls

        swFileExists.Stop();

        //Looks like a miss. Let's enter a lock for the creation of the file. This is a different locking system
        // than for writing to the file 
        //This prevents two identical requests from duplicating efforts. Different requests don't lock.

        //Lock execution using relativePath as the sync basis. Ignore casing differences. This prevents duplicate entries in the write queue and wasted CPU/RAM usage.
        var lockResult
            = await QueueLocks.TryExecuteAsync<IResult<BlobSuccessResult, IBlobCacheFetchFailure>
                , IBlobCacheRequest>(cacheRequest.CacheKeyHashString,
                Options.WaitForIdenticalRequestsTimeoutMs, cancellationToken, cacheRequest,
                async (IBlobCacheRequest r, CancellationToken ct) =>
                {
                    var swInsideQueueLock = Stopwatch.StartNew();

                    // Now, if the item we seek is in the queue, we have a memcached hit.
                    // If not, we should check the filesystem. It's possible the item has been written to disk already.
                    // If both are a miss, we should see if there is enough room in the write queue.
                    // If not, switch to in-thread writing. 

                    var existingQueuedWrite = CurrentWrites.Get(cacheRequest.CacheKeyHashString);

                    if (existingQueuedWrite != null)
                    {
                        return Result<BlobSuccessResult, IBlobCacheFetchFailure>.Ok(
                            new BlobSuccessResult(existingQueuedWrite.Blob, "MemoryHit"));
                    }

                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);

                    swFileExists.Start();
                    // Fast path on disk hit, now that we're in a synchronized state
                    var slowResult = await cache.CacheFetch(cacheRequest.WithFailFast(false), cancellationToken);
                    if (slowResult.IsOk) return Result<BlobSuccessResult, IBlobCacheFetchFailure>.Ok(new BlobSuccessResult(slowResult.Unwrap(), "DiskHit"));

                    // Just continue on creating the file. It must have been deleted between the calls

                    swFileExists.Stop();

                    var swDataCreation = Stopwatch.StartNew();
                    //Read, resize, process, and encode the image. Lots of exceptions thrown here.
                    var generatedResult = await dataProviderCallback(cancellationToken);
                    if (!generatedResult.IsOk)
                    {
                        return Result<BlobSuccessResult, IBlobCacheFetchFailure>.Err(new BlobCacheFetchFailure() {Status = generatedResult.UnwrapError().WithAppend("Error generating image")});
                    }

                    await generatedResult.Unwrap().Blob.EnsureReusable(cancellationToken);
                    swDataCreation.Stop();

                    //Create AsyncWrite object to enqueue
                    var w = new BlobTaskItem(cacheRequest.CacheKeyHashString, generatedResult.Unwrap().Blob);


                    var cachePutEvent = CacheEventDetails.CreateFreshResultGeneratedEvent
                    (cacheRequest, reusableBlobFactory,
                        Result<IBlobWrapper, IBlobCacheFetchFailure>.Ok(w.Blob));



                    var swEnqueue = Stopwatch.StartNew();
                    var queueResult = CurrentWrites.Queue(w, async delegate
                    {
                        try
                        {
                            var putResult = await cache.CachePut(cachePutEvent, CancellationToken.None);
                            if (!putResult.IsOk)
                            {
                                // Logger?.LogError("HybridCache failed to write to disk, {Exception} {Path}\n{StackTrace}", putResult.Error, w.RelativePath, putResult.Error.StackTrace);
                                // cacheResult.Detail = AsyncCacheDetailResult.QueueLockTimeoutAndFailed;
                                // return;
                            }
                            //var unused = await EvictWriteAndLogSynchronized(false, swDataCreation.Elapsed, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            Logger?.LogError(ex,
                                "HybridCache failed to flush async write, {Exception} {Path}\n{StackTrace}",
                                ex.ToString(),
                                w.Blob.Attributes.BlobStorageReference?.GetFullyQualifiedRepresentation(),
                                ex.StackTrace);
                            if (Logger == null) throw;
                        }

                    });
                    swEnqueue.Stop();
                    swInsideQueueLock.Stop();
                    swGetOrCreateBytes.Stop();

                    if (queueResult == TaskEnqueueResult.QueueFull)
                    {
                        if (Options.WriteSynchronouslyWhenQueueFull)
                        {
                            var putResult = await cache.CachePut(cachePutEvent, CancellationToken.None);
                            if (putResult.TryUnwrapError(out var putError))
                            {
                                Logger?.LogError("HybridCache failed to write to disk, {Error} {Event}", putError, 
                                     cachePutEvent);
                                //cacheResult.Detail = AsyncCacheDetailResult.QueueLockTimeoutAndFailed;
                                return Result<BlobSuccessResult, IBlobCacheFetchFailure>.Err(new BlobCacheFetchFailure {Status = putError});
                            }
                            return Result<BlobSuccessResult, IBlobCacheFetchFailure>.Ok(new BlobSuccessResult(w.Blob, "WriteSucceeded"));
                            //cacheResult.Detail = writerDelegateResult;
                        }
                    }

                    return Result<BlobSuccessResult, IBlobCacheFetchFailure>.Ok(new BlobSuccessResult(w.Blob, queueResult.ToString()));
                });
        if (lockResult.IsOk) return lockResult.Unwrap();

        //On queue lock failure
        if (!Options.FailRequestsOnEnqueueLockTimeout)
        {
            // We run the callback with no intent of caching
            var generationResult = await dataProviderCallback(cancellationToken);
            if (!generationResult.IsOk)
                return Result<BlobSuccessResult, IBlobCacheFetchFailure>.Err(new BlobCacheFetchFailure {Status =generationResult.UnwrapError()});

            // It's not completely ok, but we can return the bytes
            return Result<BlobSuccessResult, IBlobCacheFetchFailure>.Ok(generationResult.Unwrap());

        }
        else
        {
            return Result<BlobSuccessResult, IBlobCacheFetchFailure>.Err(new BlobCacheFetchFailure {Status = (500, "Queue lock timeout")});
            
        }
    }

    
}