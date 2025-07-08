using System.Diagnostics;
using System.Globalization;
using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Concurrency;
using Imazen.Common.Extensibility.Support;
using Imazen.HybridCache.MetaStore;
using Imazen.Abstractions.Logging;
using Microsoft.Extensions.Logging;

namespace Imazen.HybridCache
{
    internal class AsyncCache : IBlobCache
    {
        private enum AsyncCacheDetailResult
        {
            Unknown = 0,
            MemoryHit,
            DiskHit,
            WriteSucceeded,
            QueueLockTimeoutAndCreated,
            FileAlreadyExists,
            Miss,
            CacheEvictionFailed,
            WriteTimedOut,
            QueueLockTimeoutAndFailed,
            EvictAndWriteLockTimedOut,
            ContendedDiskHit
        }


        public string UniqueName { get; }

        public AsyncCache(AsyncCacheOptions options, ICacheCleanupManager cleanupManager,
            ICacheDatabase<ICacheDatabaseRecord> database, HashBasedPathBuilder pathBuilder, IReLogger? logger)
        {
            UniqueName = options.UniqueName;
            Database = database;
            Options = options;
            PathBuilder = pathBuilder;
            CleanupManager = cleanupManager;
            Logger = logger;
            FileWriteLocks = new AsyncLockProvider();
            //QueueLocks = new AsyncLockProvider();
            EvictAndWriteLocks = new AsyncLockProvider();
            // CurrentWrites = new BoundedTaskCollection<BlobTaskItem>(options.MaxQueuedBytes);
            FileWriter = new CacheFileWriter(FileWriteLocks, Options.MoveFileOverwriteFunc, Options.MoveFilesIntoPlace);
            InitialCacheCapabilities = new BlobCacheCapabilities
            {
                CanConditionalFetch = false,
                CanConditionalPut = false,
                CanFetchData = true,
                CanFetchMetadata = true,
                CanPurgeByTag = true,
                CanSearchByTag = true,
                CanDelete = true,
                CanPut = true,
                CanReceiveEvents = true,
                SupportsHealthCheck = true,
                SubscribesToExternalHits = true,
                SubscribesToRecentRequest = true,
                FixedSize = true,
                SubscribesToFreshResults = true,
                RequiresInlineExecution = false
            };
            LatencyZone = new LatencyTrackingZone($"Hybrid Disk Cache ('{UniqueName}')", 30);
        }

        ICacheDatabase<ICacheDatabaseRecord> Database { get; }

        private LatencyTrackingZone LatencyZone { get; set; }
        
        private AsyncCacheOptions Options { get; }
        private HashBasedPathBuilder PathBuilder { get; }
        private IReLogger? Logger { get; }

        private ICacheCleanupManager CleanupManager { get; }

        /// <summary>
        /// Provides string-based locking for file write access. Note: this doesn't lock on relativepath in filewriter!
        /// </summary>
        private AsyncLockProvider FileWriteLocks { get; }

        // TODO: carefully review all these locks to ensure the key construction mechanism is the same on all
        private AsyncLockProvider EvictAndWriteLocks { get; }

        private CacheFileWriter FileWriter { get; }

        // /// <summary>
        // /// Provides string-based locking for image resizing (not writing, just processing). Prevents duplication of efforts in asynchronous mode, where 'Locks' is not being used.
        // /// </summary>
        // [Obsolete]
        // private AsyncLockProvider QueueLocks { get;  }

        /// <summary>
        /// Contains all the queued and in-progress writes to the cache. 
        /// </summary>
        // [Obsolete]
        // private BoundedTaskCollection<BlobTaskItem> CurrentWrites {get; }



        private static bool IsFileLocked(IOException exception)
        {
            //For linux
            const int linuxEAgain = 11;
            const int linuxEBusy = 16;
            const int linuxEPerm = 13;
            if (linuxEAgain == exception.HResult || linuxEBusy == exception.HResult || linuxEPerm == exception.HResult)
            {
                return true;
            }

            //For windows
            // See https://docs.microsoft.com/en-us/dotnet/standard/io/handling-io-errors
            const int errorSharingViolation = 0x20;
            const int errorLockViolation = 0x21;
            var errorCode = exception.HResult & 0x0000FFFF;
            return errorCode == errorSharingViolation || errorCode == errorLockViolation;
        }

        private async Task<FileStream?> TryWaitForLockedFile(string physicalPath, Stopwatch waitTime, int timeoutMs,
            CancellationToken cancellationToken)
        {
            var waitForFile = waitTime;
            waitTime.Stop();
            while (waitForFile.ElapsedMilliseconds < timeoutMs)
            {
                waitForFile.Start();
                try
                {
                    var fs = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);

                    waitForFile.Stop();
                    Logger?.LogInformation("Cache file locked, waited {WaitTime} to read {Path}", waitForFile.Elapsed,
                        physicalPath);
                    return fs;
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
                catch (IOException iex)
                {
                    if (!IsFileLocked(iex)) throw;
                }
                catch (UnauthorizedAccessException)
                {

                }

                await Task.Delay((int)Math.Min(15, Math.Round(timeoutMs / 3.0)), cancellationToken);
                waitForFile.Stop();
            }

            return null;
        }

        private async Task<IResult<IBlobWrapper, IBlobCacheFetchFailure>?> TryWaitForLockedFile(CacheEntry entry,
            ICacheDatabaseRecord? record, CancellationToken cancellationToken)
        {
            FileStream? openedStream = null;
            var waitTime = Stopwatch.StartNew();
            if (!await FileWriteLocks.TryExecuteAsync(entry.HashString, Options.WaitForIdenticalDiskWritesMs,
                    cancellationToken, async () =>
                    {
                        openedStream = await TryWaitForLockedFile(entry.PhysicalPath, waitTime,
                            Options.WaitForIdenticalDiskWritesMs, cancellationToken);
                    }))
            {
                return null;
            }

            if (openedStream != null)
            {
                //TODO:  add contended hit detail
                return AsyncCacheResult.FromHit(record, entry.RelativePath, PathBuilder, openedStream, LatencyZone, this, this, Logger);
            }

            return null;
        }

        private async Task<IResult<IBlobWrapper, IBlobCacheFetchFailure>?> TryGetFileBasedResult(CacheEntry entry,
            bool waitForFile, bool retrieveAttributes, CancellationToken cancellationToken)
        {
            if (!File.Exists(entry.PhysicalPath)) return null;

            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            var record = retrieveAttributes
                ? await CleanupManager.GetRecordReference(entry, cancellationToken)
                : null;

            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            try
            {
                return AsyncCacheResult.FromHit(record, entry.RelativePath, PathBuilder, new FileStream(
                    entry.PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan), LatencyZone, this, this, Logger);

            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                if (!waitForFile) return null;

                return await TryWaitForLockedFile(entry, record, cancellationToken);
            }

            catch (IOException ioException)
            {
                if (!waitForFile) return null;

                if (IsFileLocked(ioException))
                {
                    return await TryWaitForLockedFile(entry, record, cancellationToken);
                }
                else
                {
                    throw;
                }
            }
        }

        // /// <summary>
        // /// Tries to fetch the result from disk cache, the memory queue, or create it. If the memory queue has space,
        // /// the writeCallback() will be executed and the resulting bytes put in a queue for writing to disk.
        // /// If the memory queue is full, writing to disk will be attempted synchronously.
        // /// In either case, writing to disk can also fail if the disk cache is full and eviction fails.
        // /// If the memory queue is full, eviction will be done synchronously and can cause other threads to time out
        // /// while waiting for QueueLock
        // /// </summary>
        // /// <param name="key"></param>
        // /// <param name="dataProviderCallback"></param>
        // /// <param name="cancellationToken"></param>
        // /// <param name="retrieveContentType"></param>
        // /// <returns></returns>
        // /// <exception cref="OperationCanceledException"></exception>
        // /// <exception cref="ArgumentOutOfRangeException"></exception>
        // [Obsolete]
        // public async Task<AsyncCacheResult> GetOrCreateBytes(
        //     byte[] key,
        //     AsyncBytesResult dataProviderCallback,
        //     CancellationToken cancellationToken,
        //     bool retrieveContentType)
        // {
        //     if (cancellationToken.IsCancellationRequested)
        //         throw new OperationCanceledException(cancellationToken);
        //
        //     var swGetOrCreateBytes = Stopwatch.StartNew();
        //     var entry = new CacheEntry(key, PathBuilder);
        //     
        //     // Tell cleanup what we're using
        //     CleanupManager.NotifyUsed(entry);
        //     
        //     // Fast path on disk hit
        //     var swFileExists = Stopwatch.StartNew();
        //
        //     var fileBasedResult = await TryGetFileBasedResult(entry, false, retrieveContentType, cancellationToken);
        //     if (fileBasedResult != null)
        //     {
        //         return fileBasedResult;
        //     }
        //     // Just continue on creating the file. It must have been deleted between the calls
        //
        //     swFileExists.Stop();
        //     
        //
        //
        //     var cacheResult = new AsyncCacheResult();
        //     
        //     //Looks like a miss. Let's enter a lock for the creation of the file. This is a different locking system
        //     // than for writing to the file 
        //     //This prevents two identical requests from duplicating efforts. Different requests don't lock.
        //
        //     //Lock execution using relativePath as the sync basis. Ignore casing differences. This prevents duplicate entries in the write queue and wasted CPU/RAM usage.
        //     var queueLockComplete = await QueueLocks.TryExecuteAsync(entry.StringKey,
        //         Options.WaitForIdenticalRequestsTimeoutMs, cancellationToken,
        //         async () =>
        //         {
        //             var swInsideQueueLock = Stopwatch.StartNew();
        //             
        //             // Now, if the item we seek is in the queue, we have a memcached hit.
        //             // If not, we should check the filesystem. It's possible the item has been written to disk already.
        //             // If both are a miss, we should see if there is enough room in the write queue.
        //             // If not, switch to in-thread writing. 
        //
        //             var existingQueuedWrite = CurrentWrites.Get(entry.StringKey);
        //
        //             if (existingQueuedWrite != null)
        //             {
        //                 cacheResult.Data = existingQueuedWrite.GetReadonlyStream();
        //                 cacheResult.CreatedAt = null; // Hasn't been written yet
        //                 cacheResult.ContentType = existingQueuedWrite.ContentType;
        //                 cacheResult.Detail = AsyncCacheDetailResult.MemoryHit;
        //                 return;
        //             }
        //             
        //             if (cancellationToken.IsCancellationRequested)
        //                 throw new OperationCanceledException(cancellationToken);
        //
        //             swFileExists.Start();
        //             // Fast path on disk hit, now that we're in a synchronized state
        //             var fileBasedResult2 = await TryGetFileBasedResult(entry, true, retrieveContentType, cancellationToken);
        //             if (fileBasedResult2 != null)
        //             {
        //                 cacheResult = fileBasedResult2;
        //                 return;
        //             }
        //             // Just continue on creating the file. It must have been deleted between the calls
        //         
        //             swFileExists.Stop();
        //
        //             var swDataCreation = Stopwatch.StartNew();
        //             //Read, resize, process, and encode the image. Lots of exceptions thrown here.
        //             var result = await dataProviderCallback(cancellationToken);
        //             swDataCreation.Stop();
        //             
        //             //Create AsyncWrite object to enqueue
        //             var w = new BlobTaskItem(entry.StringKey, result.Bytes, result.ContentType);
        //
        //             cacheResult.Detail = AsyncCacheDetailResult.Miss;
        //             cacheResult.ContentType = w.ContentType;
        //             cacheResult.CreatedAt = null; // Hasn't been written yet.
        //             cacheResult.Data = w.GetReadonlyStream();
        //
        //             // Create a lambda which we can call either in a spawned Task (if enqueued successfully), or
        //             // in this task, if our buffer is full.
        //             async Task<AsyncCacheDetailResult> EvictWriteAndLogUnsynchronized(bool queueFull, TimeSpan dataCreationTime,  CancellationToken ct)
        //             {
        //                 var delegateStartedAt = DateTime.UtcNow;
        //                 var swReserveSpace = Stopwatch.StartNew();
        //                 //We only permit eviction proceedings from within the queue or if the queue is disabled
        //                 var allowEviction = !queueFull || CurrentWrites.MaxQueueBytes <= 0;
        //                 var reserveSpaceResult = await CleanupManager.TryReserveSpace(entry, w.ContentType, 
        //                     w.GetUsedBytes(), allowEviction, EvictAndWriteLocks, ct);
        //                 swReserveSpace.Stop();
        //
        //                 var syncString = queueFull ? "synchronous" : "async";
        //                 if (!reserveSpaceResult.Success)
        //                 {
        //                     Logger?.LogError(
        //                         queueFull
        //                             ? "HybridCache synchronous eviction failed; {Message}. Time taken: {1}ms - {2}"
        //                             : "HybridCache async eviction failed; {Message}. Time taken: {1}ms - {2}",
        //                         syncString, reserveSpaceResult.Message, swReserveSpace.ElapsedMilliseconds,
        //                         entry.RelativePath);
        //
        //                     return AsyncCacheDetailResult.CacheEvictionFailed;
        //                 }
        //
        //                 var swIo = Stopwatch.StartNew();
        //                 // We only force an immediate File.Exists check when running from the Queue
        //                 // Otherwise it happens inside the lock
        //                 var fileWriteResult = await FileWriter.TryWriteFile(entry, delegate(Stream s, CancellationToken ct2)
        //                 {
        //                     if (ct2.IsCancellationRequested) throw new OperationCanceledException(ct2);
        //
        //                     var fromStream = w.GetReadonlyStream();
        //                     return fromStream.CopyToAsync(s, 81920, ct2);
        //                 }, !queueFull, Options.WaitForIdenticalDiskWritesMs, ct);
        //                 swIo.Stop();
        //
        //                 var swMarkCreated = Stopwatch.StartNew();
        //                 // Mark the file as created so it can be deleted
        //                 await CleanupManager.MarkFileCreated(entry, 
        //                     w.ContentType, 
        //                     w.GetUsedBytes(),
        //                     DateTime.UtcNow);
        //                 swMarkCreated.Stop();
        //                 
        //                 switch (fileWriteResult)
        //                 {
        //                     case CacheFileWriter.FileWriteStatus.LockTimeout:
        //                         //We failed to lock the file.
        //                         Logger?.LogWarning("HybridCache {Sync} write failed; disk lock timeout exceeded after {IoTime}ms - {Path}", 
        //                             syncString, swIo.ElapsedMilliseconds, entry.RelativePath);
        //                         return AsyncCacheDetailResult.WriteTimedOut;
        //                     case CacheFileWriter.FileWriteStatus.FileAlreadyExists:
        //                         Logger?.LogTrace("HybridCache {Sync} write found file already exists in {IoTime}ms, after a {DelayTime}ms delay and {CreationTime}- {Path}", 
        //                             syncString, swIo.ElapsedMilliseconds, 
        //                             delegateStartedAt.Subtract(w.JobCreatedAt).TotalMilliseconds, 
        //                             dataCreationTime, entry.RelativePath);
        //                         return AsyncCacheDetailResult.FileAlreadyExists;
        //                     case CacheFileWriter.FileWriteStatus.FileCreated:
        //                         if (queueFull)
        //                         {
        //                             Logger?.LogTrace(@"HybridCache synchronous write complete. Create: {CreateTime}ms. Write {WriteTime}ms. Mark Created: {MarkCreatedTime}ms. Eviction: {EvictionTime}ms - {Path}", 
        //                                 Math.Round(dataCreationTime.TotalMilliseconds).ToString(CultureInfo.InvariantCulture).PadLeft(4),
        //                                 swIo.ElapsedMilliseconds.ToString().PadLeft(4),
        //                                 swMarkCreated.ElapsedMilliseconds.ToString().PadLeft(4), 
        //                                 swReserveSpace.ElapsedMilliseconds.ToString().PadLeft(4), entry.RelativePath);
        //                         }
        //                         else
        //                         {
        //                             Logger?.LogTrace(@"HybridCache async write complete. Create: {CreateTime}ms. Write {WriteTime}ms. Mark Created: {MarkCreatedTime}ms Eviction {EvictionTime}ms. Delay {DelayTime}ms. - {Path}", 
        //                                 Math.Round(dataCreationTime.TotalMilliseconds).ToString(CultureInfo.InvariantCulture).PadLeft(4),
        //                                 swIo.ElapsedMilliseconds.ToString().PadLeft(4), 
        //                                 swMarkCreated.ElapsedMilliseconds.ToString().PadLeft(4), 
        //                                 swReserveSpace.ElapsedMilliseconds.ToString().PadLeft(4), 
        //                                 Math.Round(delegateStartedAt.Subtract(w.JobCreatedAt).TotalMilliseconds).ToString(CultureInfo.InvariantCulture).PadLeft(4), 
        //                                 entry.RelativePath);
        //                         }
        //
        //                         return AsyncCacheDetailResult.WriteSucceeded;
        //                     default:
        //                         throw new ArgumentOutOfRangeException();
        //                 }
        //             }
        //
        //             async Task<AsyncCacheDetailResult> EvictWriteAndLogSynchronized(bool queueFull,
        //                 TimeSpan dataCreationTime, CancellationToken ct)
        //             {
        //                 var cacheDetailResult = AsyncCacheDetailResult.Unknown;
        //                 var writeLockComplete = await EvictAndWriteLocks.TryExecuteAsync(entry.StringKey,
        //                     Options.WaitForIdenticalRequestsTimeoutMs, cancellationToken,
        //                     async () =>
        //                     {
        //                         cacheDetailResult =
        //                             await EvictWriteAndLogUnsynchronized(queueFull, dataCreationTime, ct);
        //                     });
        //                 if (!writeLockComplete)
        //                 {
        //                     cacheDetailResult = AsyncCacheDetailResult.EvictAndWriteLockTimedOut;
        //                 }
        //
        //                 return cacheDetailResult;
        //             }
        //             
        //
        //             var swEnqueue = Stopwatch.StartNew();
        //             var queueResult = CurrentWrites.Queue(w, async delegate
        //             {
        //                 try
        //                 {
        //                     var unused = await EvictWriteAndLogSynchronized(false, swDataCreation.Elapsed, CancellationToken.None);
        //                 }
        //                 catch (Exception ex)
        //                 {
        //                     Logger?.LogError(ex, "HybridCache failed to flush async write, {Exception} {Path}\n{StackTrace}", ex.ToString(),
        //                         entry.RelativePath, ex.StackTrace);
        //                 }
        //
        //             });
        //             swEnqueue.Stop();
        //             swInsideQueueLock.Stop();
        //             swGetOrCreateBytes.Stop();
        //
        //             // if (queueResult == BoundedTaskCollection<BlobTaskItem>.EnqueueResult.QueueFull)
        //             // {
        //             //     if (Options.WriteSynchronouslyWhenQueueFull)
        //             //     {
        //             //         var writerDelegateResult = await EvictWriteAndLogSynchronized(true, swDataCreation.Elapsed, cancellationToken);
        //             //         cacheResult.Detail = writerDelegateResult;
        //             //     }
        //             // }
        //         });
        //     if (!queueLockComplete)
        //     {
        //         //On queue lock failure
        //         if (!Options.FailRequestsOnEnqueueLockTimeout)
        //         {
        //             // We run the callback with no intent of caching
        //             var cacheInputEntry = await dataProviderCallback(cancellationToken);
        //             
        //             cacheResult.Detail = AsyncCacheDetailResult.QueueLockTimeoutAndCreated;
        //             cacheResult.ContentType = cacheInputEntry.ContentType;
        //             cacheResult.CreatedAt = null; //Hasn't been written yet
        //             
        //             cacheResult.Data = new MemoryStream(cacheInputEntry.Bytes.Array ?? throw new NullReferenceException(), 
        //                 cacheInputEntry.Bytes.Offset, cacheInputEntry.Bytes.Count, false, true);
        //         }
        //         else
        //         {
        //             cacheResult.Detail = AsyncCacheDetailResult.QueueLockTimeoutAndFailed;
        //         }
        //     }
        //     return cacheResult;
        // }
        //
        //

        private BlobCacheSupportData? SupportData { get; set; }
        public void Initialize(BlobCacheSupportData supportData)
        {
            SupportData = supportData;
        }

        public async Task<IResult<IBlobWrapper, IBlobCacheFetchFailure>> CacheFetch(IBlobCacheRequest request,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            var entry = CacheEntry.FromHash(request.CacheKeyHash, request.CacheKeyHashString, PathBuilder);

            // Notify cleanup that we're using this entry
            CleanupManager.NotifyUsed(entry);

            var waitForFile = !request.FailFast;

            // Fast path on disk hit, now that we're in a synchronized state
            var fileBasedResult2 =
                await TryGetFileBasedResult(entry, waitForFile, request.FetchAllMetadata, cancellationToken);
            if (fileBasedResult2 != null)
            {
                return fileBasedResult2;
            }

            return AsyncCacheResult.FromMiss(this, this);
        }

        public async Task<CodeResult> CachePut(ICacheEventDetails e, CancellationToken cancellationToken = default)
        {
            var entry = CacheEntry.FromHash(e.OriginalRequest.CacheKeyHash, e.OriginalRequest.CacheKeyHashString,
                PathBuilder);
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            if (e.Result == null) throw new InvalidOperationException("Result is null");
            using var blob = await e.Result.Unwrap().GetConsumablePromise().IntoConsumableBlob();
            if (blob == null) throw new InvalidOperationException("Blob is null");
            var record = new CacheDatabaseRecord
            {
                AccessCountKey = CleanupManager.GetAccessCountKey(entry),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(1),
                LastDeletionAttempt = DateTime.MinValue,
                EstDiskSize = CleanupManager.EstimateFileSizeOnDisk(blob.StreamLength ?? 0),
                RelativePath = entry.RelativePath,
                ContentType = blob.Attributes.ContentType,
                Flags = (e.BlobCategory == BlobGroup.Essential) ? CacheEntryFlags.DoNotEvict : CacheEntryFlags.Unknown,
                Tags = blob.Attributes.StorageTags
            };

            var result = await EvictWriteAndLogSynchronized(entry, record, blob, e.AsyncWriteJobCreatedAt,
                false,
                e.FreshResultGenerationTime, cancellationToken);

            if (result != AsyncCacheDetailResult.WriteSucceeded)
            {
                Logger?.LogError("HybridCache failed to complete async write, {Result} {Path}", result,
                    entry.RelativePath);
                return CodeResult.Err((500, "Failed to flush async write"));
            }

            return CodeResult.Ok();
        }

        public Task<CodeResult<IAsyncEnumerable<IBlobStorageReference>>> CacheSearchByTag(SearchableBlobTag tag,
            CancellationToken cancellationToken = default)
        {
            return CleanupManager.CacheSearchByTag(tag, cancellationToken);
        }

        public Task<CodeResult<IAsyncEnumerable<CodeResult<IBlobStorageReference>>>> CachePurgeByTag(SearchableBlobTag tag,
            CancellationToken cancellationToken = default)
        {
            // MAJOR FLAW - delete locks on relative path, other users of this use the string key
            return CleanupManager.CachePurgeByTag(tag, EvictAndWriteLocks, cancellationToken);
        }

        public Task<CodeResult> CacheDelete(IBlobStorageReference reference,
            CancellationToken cancellationToken = default)
        {
            // MAJOR FLAW - delete locks on relative path, other users of this use the string key
            return CleanupManager.CacheDelete(((FileBlobStorageReference)reference).RelativePath, EvictAndWriteLocks,
                cancellationToken);
        }

        public Task<CodeResult> OnCacheEvent(ICacheEventDetails e, CancellationToken cancellationToken = default)
        {
            if (e.FreshResultGenerated || e.ExternalCacheHit != null)
            {
                var entry = CacheEntry.FromHash(e.OriginalRequest.CacheKeyHash, e.OriginalRequest.CacheKeyHashString,
                    PathBuilder);

                // Tell cleanup what we're using
                CleanupManager.NotifyUsed(entry);
            }

            return Task.FromResult(CodeResult.Ok());
        }

        public BlobCacheCapabilities InitialCacheCapabilities { get; }

        public async ValueTask<IBlobCacheHealthDetails> CacheHealthCheck(CancellationToken cancellationToken = default)
        {
            try
            {
                // If checking the existence of the cache folder causes an exception (rather than just false/missing),
                // fail fetching

                var rootDirResult = await Database.TestRootDirectory();
                if (!rootDirResult.IsOk)
                {
                    return BlobCacheHealthDetails.Error(rootDirResult, BlobCacheCapabilities.OnlyHealthCheck)
                        with { SuggestedRecheckDelay = TimeSpan.FromMinutes(1) }; // Permissions/drive access issues don't resolve fast.
                }


                // If the metastore cannot load, fail all write actions and specify an increasing retry interval.
                var metaStoreResult = await Database.TestMetaStore();
                if (!metaStoreResult.IsOk)
                {
                    return BlobCacheHealthDetails.Error(metaStoreResult,
                        BlobCacheCapabilities.OnlyHealthCheck with
                        {
                            CanFetchData = true
                            // Recycling / locking issues usually take a bit for shutdown and unlock
                        }) with { SuggestedRecheckDelay = TimeSpan.FromSeconds(10) };
                }
            }
            catch (Exception ex)
            {
                return BlobCacheHealthDetails.Error(CodeResult.FromException(ex), BlobCacheCapabilities.OnlyHealthCheck);
            }

            return BlobCacheHealthDetails.FullHealth(InitialCacheCapabilities);
        }

        // Create a lambda which we can call either in a spawned Task (if enqueued successfully), or
        // in this task, if our buffer is full.
        async Task<AsyncCacheDetailResult> EvictWriteAndLogUnsynchronized(CacheEntry entry,
            CacheDatabaseRecord newRecord, IConsumableBlob blob, bool insideImageDeliveryRequest,
            DateTimeOffset jobCreatedAt, TimeSpan dataCreationTime, CancellationToken ct)
        {
            if (insideImageDeliveryRequest) throw new NotImplementedException();
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            if (blob.StreamLength == null) throw new InvalidOperationException("StreamLength is null");
            if (blob.StreamLength.Value > int.MaxValue)
                throw new InvalidOperationException("StreamLength exceeds 2gb limit");
            var blobByteCount = (int)(blob.StreamLength ?? -1);


            var delegateStartedAt = DateTimeOffset.UtcNow;
            var swReserveSpace = Stopwatch.StartNew();
            //We only permit eviction proceedings from within the queue or if the queue is disabled
            var allowEviction = !insideImageDeliveryRequest; // || CurrentWrites.MaxQueueBytes <= 0;
            var reserveSpaceResult =
                await CleanupManager.TryReserveSpace(entry, newRecord, allowEviction, EvictAndWriteLocks, ct);
            swReserveSpace.Stop();

            var syncString = insideImageDeliveryRequest ? "synchronous" : "async";
            if (!reserveSpaceResult.Success)
            {
                Logger?.LogError(
                    insideImageDeliveryRequest
                        ? "HybridCache synchronous eviction failed; {Message}. Time taken: {1}ms - {2}"
                        : "HybridCache async eviction failed; {Message}. Time taken: {1}ms - {2}",
                    syncString, reserveSpaceResult.Message, swReserveSpace.ElapsedMilliseconds,
                    entry.RelativePath);

                return AsyncCacheDetailResult.CacheEvictionFailed;
            }

            var swIo = Stopwatch.StartNew();
            // We only force an immediate File.Exists check when running from the Queue
            // Otherwise it happens inside the lock
            var fileWriteResult = await FileWriter.TryWriteFile(entry, async delegate(Stream s, CancellationToken ct2)
            {
                if (ct2.IsCancellationRequested) throw new OperationCanceledException(ct2);

                using var fromStream = blob.BorrowStream(DisposalPromise.CallerDisposesStreamThenBlob);
                await fromStream.CopyToAsync(s, 81920, ct2);
            }, !insideImageDeliveryRequest, Options.WaitForIdenticalDiskWritesMs, ct);
            swIo.Stop();

            var swMarkCreated = Stopwatch.StartNew();
            // Mark the file as created so it can be deleted
            await CleanupManager.MarkFileCreated(entry,
                DateTime.UtcNow, () => newRecord);
            swMarkCreated.Stop();

            switch (fileWriteResult)
            {
                case CacheFileWriter.FileWriteStatus.LockTimeout:
                    //We failed to lock the file.
                    Logger?.LogWarning(
                        "HybridCache {Sync} write failed; disk lock timeout exceeded after {IoTime}ms - {Path}",
                        syncString, swIo.ElapsedMilliseconds, entry.RelativePath);
                    return AsyncCacheDetailResult.WriteTimedOut;
                case CacheFileWriter.FileWriteStatus.FileAlreadyExists:
                    Logger?.LogTrace(
                        "HybridCache {Sync} write found file already exists in {IoTime}ms, after a {DelayTime}ms delay and {CreationTime}- {Path}",
                        syncString, swIo.ElapsedMilliseconds,
                        delegateStartedAt.Subtract(jobCreatedAt).TotalMilliseconds,
                        dataCreationTime, entry.RelativePath);
                    return AsyncCacheDetailResult.FileAlreadyExists;
                case CacheFileWriter.FileWriteStatus.FileCreated:
                    if (insideImageDeliveryRequest)
                    {
                        Logger?.LogTrace(
                            @"HybridCache synchronous write complete. Create: {CreateTime}ms. Write {WriteTime}ms. Mark Created: {MarkCreatedTime}ms. Eviction: {EvictionTime}ms - {Path}",
                            Math.Round(dataCreationTime.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)
                                .PadLeft(4),
                            swIo.ElapsedMilliseconds.ToString().PadLeft(4),
                            swMarkCreated.ElapsedMilliseconds.ToString().PadLeft(4),
                            swReserveSpace.ElapsedMilliseconds.ToString().PadLeft(4), entry.RelativePath);
                    }
                    else
                    {
                        Logger?.LogTrace(
                            @"HybridCache async write complete. Create: {CreateTime}ms. Write {WriteTime}ms. Mark Created: {MarkCreatedTime}ms Eviction {EvictionTime}ms. Delay {DelayTime}ms. - {Path}",
                            Math.Round(dataCreationTime.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)
                                .PadLeft(4),
                            swIo.ElapsedMilliseconds.ToString().PadLeft(4),
                            swMarkCreated.ElapsedMilliseconds.ToString().PadLeft(4),
                            swReserveSpace.ElapsedMilliseconds.ToString().PadLeft(4),
                            Math.Round(delegateStartedAt.Subtract(jobCreatedAt).TotalMilliseconds)
                                .ToString(CultureInfo.InvariantCulture).PadLeft(4),
                            entry.RelativePath);
                    }

                    return AsyncCacheDetailResult.WriteSucceeded;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async Task<AsyncCacheDetailResult> EvictWriteAndLogSynchronized(CacheEntry entry,
            CacheDatabaseRecord record, IConsumableBlob blob, DateTimeOffset jobCreatedAt, bool queueFull,
            TimeSpan dataCreationTime, CancellationToken cancellationToken)
        {
            var cacheDetailResult = AsyncCacheDetailResult.Unknown;
            var writeLockComplete = await EvictAndWriteLocks.TryExecuteAsync(entry.HashString,
                Options.WaitForIdenticalCachePutTimeoutMs, cancellationToken,
                async () =>
                {
                    cacheDetailResult =
                        await EvictWriteAndLogUnsynchronized(entry, record, blob, queueFull, jobCreatedAt,
                            dataCreationTime, cancellationToken);
                });
            if (!writeLockComplete)
            {
                cacheDetailResult = AsyncCacheDetailResult.EvictAndWriteLockTimedOut;
            }

            return cacheDetailResult;
        }


        private static class AsyncCacheResult
        {

            internal static IResult<IBlobWrapper, IBlobCacheFetchFailure> FromHit(ICacheDatabaseRecord? record,
                string entryRelativePath, HashBasedPathBuilder interpreter,
                FileStream stream, LatencyTrackingZone latencyZone, IBlobCache notifyOfResult, IBlobCache notifyOfExternalHit, IReLogger? logger)
            {
                var blob = new StreamBlob(new BlobAttributes()
                {
                    ContentType = record?.ContentType,
                    Etag = record?.RelativePath,
                    LastModifiedDateUtc = record?.CreatedAt,
                    EstimatedBlobByteCount = stream.CanSeek ? stream.Length : null,
                    StorageTags = record?.Tags,
                    BlobStorageReference = new FileBlobStorageReference(entryRelativePath, interpreter, record)

                }, stream, logger?.WithReScopeData("file", interpreter.GetShortVersionOfRelativePath(entryRelativePath)));
                return Result<IBlobWrapper, IBlobCacheFetchFailure>.Ok( new BlobWrapper(latencyZone, blob));

            }

            internal static IResult<IBlobWrapper, IBlobCacheFetchFailure> FromMiss(IBlobCache notifyOfResult,
                IBlobCache notifyOfExternalHit)
            {
                return BlobCacheFetchFailure.MissResult(notifyOfResult, notifyOfExternalHit);
            }

        }
    }
}