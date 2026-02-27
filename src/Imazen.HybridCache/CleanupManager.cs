using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Common.Concurrency;
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

      
        private ICacheDatabase Database { get; }
        private CleanupManagerOptions Options { get; }
        
        private HashBasedPathBuilder PathBuilder { get; }
        private ILogger Logger { get; }
        public CleanupManager(CleanupManagerOptions options, ICacheDatabase database, ILogger logger, HashBasedPathBuilder pathBuilder)
        {
            PathBuilder = pathBuilder;
            Logger = logger;
            Options = options;
            Database = database;
            accessCounter = new Lazy<BucketCounter>(() => new BucketCounter(Options.AccessTrackingBits));
        }

        private void MoveOverwrite(string from, string to)
        {
            if (Options.MoveFileOverwriteFunc != null)
            {
                Options.MoveFileOverwriteFunc(from, to);
            }
            else
            {
#if NET5_0_OR_GREATER
                File.Move(from, to, true);
#else
                File.Move(from, to);
#endif
            }
        }
        public void NotifyUsed(CacheEntry cacheEntry)
        {
            AccessCounter.Increment(cacheEntry.Hash);
        }

        public Task<string> GetContentType(CacheEntry cacheEntry, CancellationToken cancellationToken)
        {
            return Database.GetContentType(Database.GetShardForKey(cacheEntry.RelativePath), cacheEntry.RelativePath);
        }

        public async Task<ICacheDatabaseRecord> GetRecordReference(CacheEntry cacheEntry, CancellationToken cancellationToken)
        {
            return await Database.GetRecord(Database.GetShardForKey(cacheEntry.RelativePath), cacheEntry.RelativePath);
        }

        public static long DirectoryEntrySize()
        {
            return 4096;
        }
        
        internal static long EstimateEntryBytesWithOverhead(long byteCount)
        {
            // Most file systems have a 4KiB block size
            var withBlockSize = (Math.Max(1, byteCount) + 4095) / 4096 * 4096;
            // Add 1KiB for the file descriptor and 1KiB for the directory descriptor, although it's probably larger
            var withFileDescriptor = withBlockSize + 1024 + 1024;

            return withFileDescriptor;
        }

        private async Task<ReserveSpaceResult> EvictSpace(int shard, long diskSpace, AsyncLockProvider writeLocks, CancellationToken cancellationToken)
        {
            
            var bytesToDeleteOptimally = Math.Max(Options.MinCleanupBytes, diskSpace);
            var bytesToDeleteMin = diskSpace;

            long bytesDeleted = 0;

            while (bytesDeleted < bytesToDeleteOptimally)
            {
                var deletionCutoff = DateTime.UtcNow.Subtract(Options.RetryDeletionAfter);
                var creationCutoff = DateTime.UtcNow.Subtract(Options.MinAgeToDelete);
                
                // GetDeletionCandidates already returns candidates sorted by usage count (least-used first)
                var records =
                    (await Database.GetDeletionCandidates(shard, deletionCutoff, creationCutoff, Options.CleanupSelectBatchSize, AccessCounter.Get))
                    .ToArray();
                
                foreach (var record in records)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);
                    
                    if (bytesDeleted >= bytesToDeleteOptimally) break;
                    
                    var deletedBytes = await TryDeleteRecord(shard, record, writeLocks);
                    bytesDeleted += deletedBytes;
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

        public async Task<ReserveSpaceResult> TryReserveSpace(CacheEntry cacheEntry, string contentType, int byteCount,
            bool allowEviction, AsyncLockProvider writeLocks,
            CancellationToken cancellationToken)
        {
            var shard = Database.GetShardForKey(cacheEntry.RelativePath);
            var shardSizeLimit = Options.MaxCacheBytes / Database.GetShardCount();
            
            // When we're okay with deleting the database entry even though the file isn't written
            var farFuture = DateTime.UtcNow.AddHours(1);

            var maxAttempts = 30;

            for (var attempts = 0; attempts < maxAttempts; attempts++)
            {
                var recordCreated =
                    await Database.CreateRecordIfSpace(shard, cacheEntry.RelativePath,
                        contentType,
                        EstimateEntryBytesWithOverhead(byteCount),
                        farFuture,
                        AccessCounter.GetHash(cacheEntry.Hash),
                        shardSizeLimit);

                // Return true if we created the record
                if (recordCreated) return new ReserveSpaceResult(){ Success = true};

                // We need to evict but we are not permitted
                if (!allowEviction) return new ReserveSpaceResult(){ Success = false, Message = "Eviction disabled in sync mode"};
                
                var entryDiskSpace = EstimateEntryBytesWithOverhead(byteCount) +
                                     Database.EstimateRecordDiskSpace(cacheEntry.RelativePath.Length + (contentType?.Length ?? 0));
                
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
        

        public Task MarkFileCreated(CacheEntry cacheEntry, string contentType, long recordDiskSpace, DateTime createdDate)
        {
            return Database.UpdateCreatedDateAtomic(
                Database.GetShardForKey(cacheEntry.RelativePath), 
                cacheEntry.RelativePath, 
                contentType,
                EstimateEntryBytesWithOverhead(recordDiskSpace), 
                createdDate, 
                AccessCounter.GetHash(cacheEntry.Hash));
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
        private async Task<long> TryDeleteRecord(int shard, ICacheDatabaseRecord record, AsyncLockProvider writeLocks)
        {
            long bytesDeleted = 0;
            var unused = await writeLocks.TryExecuteAsync(record.RelativePath, 0, CancellationToken.None, async () =>
            {
                var physicalPath = PathBuilder.GetPhysicalPathFromRelativePath(record.RelativePath);
                try
                {
                    if (File.Exists(physicalPath))
                    {
                        File.Delete(physicalPath);
                        await Database.DeleteRecord(shard, record);
                        bytesDeleted = record.DiskSize;
                    }
                    else
                    {
                        await Database.DeleteRecord(shard, record); 
                    }
                }
                catch (IOException ioException)
                {
                    if (physicalPath.Contains(".moving_"))
                    {
                        // We already moved it. All we can do is update the last deletion attempt
                        await Database.UpdateLastDeletionAttempt(shard, record.RelativePath, DateTime.UtcNow);
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
                        Logger?.LogError(ioException,"HybridCache: Error deleting file, moved for eventual deletion - {Path}", record.RelativePath);
                    }
                    catch (IOException ioException2)
                    {
                        await Database.UpdateLastDeletionAttempt(shard, record.RelativePath, DateTime.UtcNow);
                        Logger?.LogError(ioException2,"HybridCache: Failed to move file for eventual deletion - {Path}", record.RelativePath);
                    }
                }
            });
            
            return bytesDeleted;
        }

    }
}