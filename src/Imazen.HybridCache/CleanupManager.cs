using System.Globalization;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Concurrency;
using Imazen.Common.Extensibility.Support;
using Imazen.HybridCache.MetaStore;
using Microsoft.Extensions.Logging;

namespace Imazen.HybridCache
{

    internal class CleanupManager : ICacheCleanupManager
    {
        private readonly Lazy<BucketCounter> accessCounter;

        /// <summary>
        /// Creation of AccessCounter is not synchronized because we don't care if distinct references are handed out
        /// </summary>
        private BucketCounter AccessCounter => accessCounter.Value;

      
        private ICacheDatabase<ICacheDatabaseRecord> Database { get; }
        private CleanupManagerOptions Options { get; }
        
        private HashBasedPathBuilder PathBuilder { get; }
        private ILogger Logger { get; }
        
        private int[] ShardIds { get; }
        public CleanupManager(CleanupManagerOptions options, ICacheDatabase<ICacheDatabaseRecord> database, ILogger logger, HashBasedPathBuilder pathBuilder)
        {
            PathBuilder = pathBuilder;
            Logger = logger;
            Options = options;
            Database = database;
            ShardIds = Enumerable.Range(0, Database.GetShardCount()).ToArray();
            accessCounter = new Lazy<BucketCounter>(() => new BucketCounter(Options.AccessTrackingBits));
        }
        public void NotifyUsed(CacheEntry cacheEntry)
        {
            AccessCounter.Increment(cacheEntry.Hash);
        }
        
        public async Task<ICacheDatabaseRecord?> GetRecordReference(CacheEntry cacheEntry, CancellationToken cancellationToken)
        {
            return await Database.GetRecord(Database.GetShardForKey(cacheEntry.RelativePath), cacheEntry.RelativePath);
        }
        
        public async Task<CodeResult<IAsyncEnumerable<IBlobStorageReference>>> CacheSearchByTag(SearchableBlobTag tag, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            // check all shards simultaneously
            var results = await Task.WhenAll(ShardIds.Select(async shard => await Database.LinearSearchByTag(shard, tag)));
            return CodeResult<IAsyncEnumerable<IBlobStorageReference>>.Ok(
                results.SelectMany(r =>
                        r.Select(x =>
                            (IBlobStorageReference)new FileBlobStorageReference(x.RelativePath, PathBuilder, x)))
                    .ToAsyncEnumerable());
        }

        public async Task<CodeResult<IAsyncEnumerable<CodeResult<IBlobStorageReference>>>> CachePurgeByTag(SearchableBlobTag tag, AsyncLockProvider writeLocks, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            var shardTasks = ShardIds.Select(shard => ShardPurgeByTag(shard, tag, writeLocks, cancellationToken))
                .ToArray();
            var result = await Task.WhenAll(shardTasks);
            return CodeResult<IAsyncEnumerable<CodeResult<IBlobStorageReference>>>.Ok(
                result.SelectMany(r => r).ToAsyncEnumerable());
            
        }
        
        private async Task<CodeResult<IBlobStorageReference>[]> ShardPurgeByTag(int shard, SearchableBlobTag tag, AsyncLockProvider writeLocks, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            var results = await Database.LinearSearchByTag(shard, tag);
            return await Task.WhenAll(results.Select(async r =>
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                var result = await TryDelete(shard, r, writeLocks);
                if (result.DeletionFailed)
                {
                    Logger?.LogError("HybridCache: Failed to delete file {Path}", r.RelativePath);
                    return CodeResult<IBlobStorageReference>.Err((500, $"Failed to delete file {r.RelativePath}"));
                        
                }
                else
                {
                    return CodeResult<IBlobStorageReference>.Ok(new FileBlobStorageReference(r.RelativePath,PathBuilder,r));
                }
                    
            }));
        }

        public async Task<CodeResult> CacheDelete(string relativePath, AsyncLockProvider writeLocks,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            var shard = Database.GetShardForKey(relativePath);
            var record = await Database.GetRecord(shard, relativePath);
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            
            if (record == null)
                return CodeResult.Ok((200, $"File doesn't exist (in metabase) to delete: {relativePath}"));
            
            var result = await TryDelete(shard, record, writeLocks);
            // Retry once if the record reference is stale.
            if (result.RecordDeleteResult == DeleteRecordResult.RecordStaleReQueryRetry)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                record = await Database.GetRecord(shard, relativePath);
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                if (record == null)
                    return CodeResult.Ok((200, $"File doesn't exist (in metabase) to delete: {relativePath}"));

                result = await TryDelete(shard, record, writeLocks);
            }
            
            //TODO: add more detail, normalize errors better

            if (!result.DeletionFailed)
            {
                if (!result.FileExisted)
                {
                    return CodeResult.Ok((200, $"File doesn't exist to delete: {relativePath}"));
                }

                return CodeResult.Ok();
            }

            if (result.FileMovedForFutureDeletion)
            {
                return CodeResult.Err((202,
                    $"File moved to make inaccessible (deletion not possible, retry later): {relativePath}"));
            }

            if (result.FileExisted)
            {
                return CodeResult.Err((500, $"File data not deleted: {relativePath}"));
            }

            Logger?.LogError("HybridCache: Failed to delete file {Path}", relativePath);
            return CodeResult.Err((500, $"Failed to delete file {relativePath}"));
        }


        public static long DirectoryEntrySize()
        {
            return 4096;
        }
        
        internal static long EstimateFileSizeOnDiskFor(long byteCount)
        {
            // Most file systems have a 4KiB block size
            var withBlockSize = (Math.Max(1, byteCount) + 4095) / 4096 * 4096;
            // Add 1KiB for the file descriptor and 1KiB for the directory descriptor, although it's probably larger
            var withFileDescriptor = withBlockSize + 1024 + 1024;

            return withFileDescriptor;
        }

        public long EstimateFileSizeOnDisk(long byteCount) => EstimateFileSizeOnDiskFor(byteCount);

        private async Task<ReserveSpaceResult> EvictSpace(int shard, long diskSpace, AsyncLockProvider writeLocks, CancellationToken cancellationToken)
        {
            
            var bytesToDeleteOptimally = Math.Max(Options.MinCleanupBytes, diskSpace);
            var bytesToDeleteMin = diskSpace;

            long bytesDeleted = 0;

            while (bytesDeleted < bytesToDeleteOptimally)
            {
                var deletionCutoff = DateTime.UtcNow.Subtract(Options.RetryDeletionAfter);
                var creationCutoff = DateTime.UtcNow.Subtract(Options.MinAgeToDelete);
                
                var records =
                    (await Database.GetDeletionCandidates(shard, deletionCutoff, creationCutoff, Options.CleanupSelectBatchSize, AccessCounter.Get))
                    .Select(r => // I'm confused, GetDeletionCandidates already does this sort...
                        new Tuple<ushort, ICacheDatabaseRecord>(
                            AccessCounter.Get(r.AccessCountKey), r))
                    .OrderBy(r => r.Item1)
                    .Select(r => r.Item2).ToArray();
                
                foreach (var record in records)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);
                    
                    if (bytesDeleted >= bytesToDeleteOptimally) break;
                    
                    var deletedBytes = await TryDelete(shard, record, writeLocks);
                    bytesDeleted += deletedBytes.EstBytesDeleted;
                }

                // Unlikely to find more records in the next iteration
                if (records.Length < Options.CleanupSelectBatchSize)
                {
                    // If we hit the bare minimum, return OK 
                    if (bytesDeleted >= bytesToDeleteMin)
                    {
                        return new ReserveSpaceResult(){Success = true}; 
                    }
                    else
                    {
                        return new ReserveSpaceResult(){Success = false, Message = $"Failed to evict enough space using {records.Length} candidates"}; 
                    }
                }
            }
            return new ReserveSpaceResult(){Success = true}; 
        }
        
        public int GetAccessCountKey(CacheEntry cacheEntry) => AccessCounter.GetHash(cacheEntry.Hash);

        public async Task<ReserveSpaceResult> TryReserveSpace(CacheEntry cacheEntry, CacheDatabaseRecord newRecord, 
            bool allowEviction, AsyncLockProvider writeLocks,
            CancellationToken cancellationToken)
        {
            var shard = Database.GetShardForKey(cacheEntry.RelativePath);
            var shardSizeLimit = Options.MaxCacheBytes / Database.GetShardCount();
            
            // When we're okay with deleting the database entry even though the file isn't written
            if (newRecord.CreatedAt < DateTime.UtcNow.AddMinutes(1))
            {
                throw new ArgumentException("record.CreatedAt must be in the future to avoid issues");
            }
            var farFuture = DateTime.UtcNow.AddHours(1);

            var maxAttempts = 30;

            for (var attempts = 0; attempts < maxAttempts; attempts++)
            {
                
                // var newRecord = new CacheDatabaseRecord
                // {
                //     AccessCountKey = AccessCounter.GetHash(cacheEntry.Hash),
                //     ContentType = attributes.ContentType,
                //     CreatedAt = farFuture,
                //     EstDiskSize = attributes.EstDiskSize,
                //     LastDeletionAttempt = DateTime.MinValue,
                //     RelativePath = cacheEntry.RelativePath,
                //     Flags = attributes.Flags,
                //     Tags = attributes.Tags
                // };
                
                var recordCreated =
                    await Database.CreateRecordIfSpace(shard, newRecord, shardSizeLimit);

                // Return true if we created the record
                if (recordCreated) return new ReserveSpaceResult(){ Success = true};

                // We need to evict but we are not permitted
                if (!allowEviction) return new ReserveSpaceResult(){ Success = false, Message = "Eviction disabled in sync mode"};
                
                var entryDiskSpace = EstimateFileSizeOnDisk(newRecord.EstDiskSize) +
                                     Database.EstimateRecordDiskSpace(newRecord);
                
                var missingSpace = Math.Max(0, await Database.GetShardSize(shard) + entryDiskSpace - shardSizeLimit);
                // Evict space 
                var evictResult = await EvictSpace(shard, missingSpace, writeLocks, cancellationToken);
                if (!evictResult.Success)
                {
                    return evictResult; //We failed to evict enough space from the cache
                }
            }

            return new ReserveSpaceResult(){ Success = false, Message = $"Eviction worked but CreateRecordIfSpace failed {maxAttempts} times."};
        }
        

        public Task MarkFileCreated(CacheEntry cacheEntry, DateTime createdDate, Func<CacheDatabaseRecord> createIfMissing)
        {
            return Database.UpdateCreatedDateAtomic(
                Database.GetShardForKey(cacheEntry.RelativePath),
                cacheEntry.RelativePath,
                createdDate, createIfMissing);
        }
        
        //     return Database.UpdateCreatedDateAtomic(
        //         Database.GetShardForKey(cacheEntry.RelativePath),
        //         cacheEntry.RelativePath,
        //         createdDate, () =>
        //         
        //             new CacheDatabaseRecord()
        //             {
        //                 AccessCountKey = AccessCounter.GetHash(cacheEntry.Hash),
        //                 ContentType = contentType,
        //                 CreatedAt = createdDate,
        //                 DiskSize = EstimateEntryFileDiskBytesWithOverhead(recordDiskSpace),
        //                 LastDeletionAttempt = DateTime.MinValue,
        //                 RelativePath = cacheEntry.RelativePath
        //             }
        //         );
        // }

        
        private class TryDeleteResult
        {
            internal long EstBytesDeleted { get; set; } = 0;
            internal bool FileExisted { get; set; } = false;
            internal bool FileDeleted { get; set; } = false;
            internal DeleteRecordResult? RecordDeleteResult { get; set; } = null;
            internal bool DeletionFailed { get; set; } = true;
            internal bool FileMovedForFutureDeletion { get; set; } = false;
        }
        /// <summary>
        /// Skips the record if there is delete contention for a file.
        /// Only counts bytes as deleted if the physical file is deleted successfully.
        /// Deletes db record whether file exists or not.
        /// </summary>
        /// <param name="shard"></param>
        /// <param name="record"></param>
        /// <param name="writeLocks"></param>
        /// <returns></returns>
        private async Task<TryDeleteResult> TryDelete(int shard, ICacheDatabaseRecord record, AsyncLockProvider writeLocks)
        {
            var result = new TryDeleteResult();
            var unused = await writeLocks.TryExecuteAsync(record.RelativePath, 0, CancellationToken.None, async () =>
            {
                var physicalPath = PathBuilder.GetPhysicalPathFromRelativePath(record.RelativePath);
                try
                {
                    if (File.Exists(physicalPath))
                    {
                        result.FileExisted = true;
                        File.Delete(physicalPath);
                        result.FileDeleted = true;
                        result.RecordDeleteResult = await Database.DeleteRecord(shard, record);
                        if (result.RecordDeleteResult == DeleteRecordResult.RecordStaleReQueryRetry)
                        {
                            // We deleted the file but the record was stale, so it was probably recreated since
                            // the last query. Since we took the file off disk, we could call this a success.
                            // But the stale record will persist. 
                            
                        }
                        result.EstBytesDeleted = record.EstDiskSize;
                        result.DeletionFailed = false;
                    }
                    else
                    {
                        result.RecordDeleteResult = await Database.DeleteRecord(shard, record);
                        // We deleted the file but the record was stale, so it is probably being 
                        // recreated.
                        result.DeletionFailed = result.RecordDeleteResult == DeleteRecordResult.RecordStaleReQueryRetry;
                    }
                }
                catch (IOException ioException)
                {
                    if (physicalPath.Contains(".moving_"))
                    {
                        // We already moved it. All we can do is update the last deletion attempt
                        await Database.UpdateLastDeletionAttempt(shard, record.RelativePath, DateTime.UtcNow);
                        result.DeletionFailed = true;
                        return;
                    }

                    var movedRelativePath = record.RelativePath + ".moving_" +
                                            new Random().Next(int.MaxValue).ToString("x", CultureInfo.InvariantCulture);
                    var movedPath = PathBuilder.GetPhysicalPathFromRelativePath(movedRelativePath);
                    try
                    {
                        //Move it so it usage will decrease and it can be deleted later
                        //TODO: This is not transactional, as the db record is written *after* the file is moved
                        //This should be split up into create and delete
                        MoveOverwrite(physicalPath, movedPath);
                        await Database.ReplaceRelativePathAndUpdateLastDeletion(shard, record, movedRelativePath,
                            DateTime.UtcNow);
                        result.FileMovedForFutureDeletion = true;
                        result.DeletionFailed = true;
                        Logger?.LogError(ioException,"HybridCache: Error deleting file, moved for eventual deletion - {Path}", record.RelativePath);
                    }
                    catch (IOException ioException2)
                    {
                        await Database.UpdateLastDeletionAttempt(shard, record.RelativePath, DateTime.UtcNow);
                        result.DeletionFailed = true;
                        Logger?.LogError(ioException2,"HybridCache: Failed to move file for eventual deletion - {Path}", record.RelativePath);
                    }
                }
            });
            
            return result;
        }

        private void MoveOverwrite(string from, string to)
        {
            
            if (Options.MoveFileOverwriteFunc != null)
                Options.MoveFileOverwriteFunc(from, to);
            else
            {
#if NET5_0_OR_GREATER
                File.Move(from, to, true);
#else
                File.Move(from, to);
#endif
            }
        }
    }
}