using System.Collections.Concurrent;
using Imazen.Abstractions.Blobs;
using Imazen.Common.Concurrency;
using Microsoft.Extensions.Logging;

namespace Imazen.HybridCache.MetaStore
{
    internal class Shard
    {
        private readonly WriteLog writeLog;
        private readonly ILogger logger;
        private readonly int shardId;
        
        private volatile ConcurrentDictionary<string, CacheDatabaseRecord>? dict;

        private readonly BasicAsyncLock readLock = new BasicAsyncLock();
        private readonly BasicAsyncLock createLock = new BasicAsyncLock();
        internal Shard(int shardId, MetaStoreOptions options, string databaseDir, long directoryEntriesBytes,  ILogger logger)
        {
            this.shardId = shardId;
            this.logger = logger;
            writeLog = new WriteLog(shardId, databaseDir, options, directoryEntriesBytes + CleanupManager.DirectoryEntrySize(), logger);
        }


        private async Task<ConcurrentDictionary<string, CacheDatabaseRecord>> GetLoadedDict()
        {
            
                if (dict != null) return dict;
                using (var unused = await readLock.LockAsync())
                {
                    if (dict != null) return dict;
                    try
                    {
                        dict = await writeLog.Startup();
                    }
                    catch (Exception)
                    {
                        FailedToStart = true;
                        throw;
                    }
                    FailedToStart = false;
                }

                return dict;
            
        }
        
        internal bool FailedToStart{ get; set; }
        
        internal Task TryStart()
        {
            return GetLoadedDict();
        }
        
        
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return writeLog.StopAsync(cancellationToken);
        }

        public async Task UpdateLastDeletionAttempt(string relativePath, DateTime when)
        {
            using (await createLock.LockAsync())
            {
                if ((await GetLoadedDict()).TryGetValue(relativePath, out var record))
                {
                    record.LastDeletionAttempt = when;
                }
            }
        }

        public async Task<DeleteRecordResult> DeleteRecord(ICacheDatabaseRecord oldRecord)
        {
            using (await createLock.LockAsync())
            {
                if ((await GetLoadedDict()).TryRemove(oldRecord.RelativePath, out var currentRecord))
                {
                    if (currentRecord != oldRecord)
                    {
                        logger?.LogError(
                            "DeleteRecord tried to delete a different instance of the record than the one provided. Re-inserting in {ShardId}", shardId);
                        (await GetLoadedDict()).TryAdd(oldRecord.RelativePath, currentRecord);
                        return DeleteRecordResult.RecordStaleReQueryRetry;
                    }
                    else
                    {
                        await writeLog.LogDeleted(oldRecord);
                        return DeleteRecordResult.Deleted;
                    }
                }
                else
                {
                    return  DeleteRecordResult.NotFound;
                }
            }
        }

        public async Task<IEnumerable<ICacheDatabaseRecord>> GetDeletionCandidates(
            DateTime maxLastDeletionAttemptTime, DateTime maxCreatedDate, int count, Func<int, ushort> getUsageCount)
        {
            var results = (await GetLoadedDict()).Values
                .Where(r => r.CreatedAt < maxCreatedDate && r.LastDeletionAttempt < maxLastDeletionAttemptTime &&
                            !r.Flags.HasFlag(CacheEntryFlags.DoNotEvict))
                .OrderBy(r => (byte)r.Flags)
                .Select(r => new Tuple<CacheDatabaseRecord, ushort>(r, getUsageCount(r.AccessCountKey)))
                .OrderBy(t => t.Item2)
                .Select(t => (ICacheDatabaseRecord) t.Item1)
                .Take(count).ToArray();
            //logger?.LogInformation("Found {DeletionCandidates} deletion candidates in shard {ShardId} of MetaStore", results.Length, shardId);
            return results;
        }

        public async Task<long> GetShardSize()
        { 
            await GetLoadedDict();
            return writeLog.GetDiskSize();
        }
        public async Task<string?> GetContentType(string relativePath)
        {
            return (await GetRecord(relativePath))?.ContentType;
        }

        public async Task<ICacheDatabaseRecord?> GetRecord(string relativePath)
        {
            return (await GetLoadedDict()).TryGetValue(relativePath, out var record) ? 
                record: 
                null;
        }


        internal static int GetLogBytesOverhead(CacheDatabaseRecord newRecord)
        {
            return (newRecord.EstimateSerializedRowByteCount() + 1) * 4; //4x for create, update, rename, delete entries
        }
        public async Task<bool> CreateRecordIfSpace(CacheDatabaseRecord newRecord, long diskSpaceLimit)
        {
            // Lock so multiple writers can't get past the capacity check simultaneously
            using (await createLock.LockAsync())
            {
                var loadedDict = await GetLoadedDict();
                var extraLogBytes = GetLogBytesOverhead(newRecord);

                var existingDiskUsage = await GetShardSize();
                if (existingDiskUsage + newRecord.EstDiskSize + extraLogBytes > diskSpaceLimit)
                {
                    //logger?.LogInformation("Refusing new {RecordDiskSpace} byte record in shard {ShardId} of MetaStore",
                    //    recordDiskSpace, shardId);
                    return false;
                }
         
                if (loadedDict.TryAdd(newRecord.RelativePath, newRecord))
                {
                    await writeLog.LogCreated(newRecord);

                }
                else
                {
                    logger?.LogWarning("CreateRecordIfSpace did nothing - database entry already exists - {Path}", newRecord.RelativePath);
                }
            }

            return true;

        }

        public async Task UpdateCreatedDateAtomic(string relativePath, DateTime createdDate,Func<CacheDatabaseRecord> createIfMissing)
        {
            using (await createLock.LockAsync())
            {
                if ((await GetLoadedDict()).TryGetValue(relativePath, out var getRecord))
                {
                    getRecord.CreatedAt = createdDate;
                    await writeLog.LogUpdated(getRecord);
                }
                else
                {
                    var record = (await GetLoadedDict()).GetOrAdd(relativePath, (s) => createIfMissing());
                    
                    record.CreatedAt = createdDate;
                    await writeLog.LogCreated(record);
                    logger?.LogError("HybridCache UpdateCreatedDate had to recreate entry - {Path}", relativePath);
                }
            }
        }

        public async Task ReplaceRelativePathAndUpdateLastDeletion(ICacheDatabaseRecord record,
            string movedRelativePath,
            DateTime lastDeletionAttempt)
        {
            var newRecord = new CacheDatabaseRecord()
            {
                Flags = record.Flags,
                Tags = record.Tags,
                AccessCountKey = record.AccessCountKey,
                ContentType = record.ContentType,
                CreatedAt = record.CreatedAt,
                EstDiskSize = record.EstDiskSize,
                LastDeletionAttempt = lastDeletionAttempt,
                RelativePath = movedRelativePath
            };
            
            var loadedDict = await GetLoadedDict();
            if (loadedDict.TryAdd(movedRelativePath, newRecord))
            {
                await writeLog.LogCreated(newRecord);
            }
            else
            {
                throw new InvalidOperationException("Record already moved in database");
            }
            await DeleteRecord(record);
        }

        public async Task<IEnumerable<ICacheDatabaseRecord>> LinearSearchByTag(SearchableBlobTag tag)
        {
            var loadedDict = await GetLoadedDict();
            return loadedDict.Values.Where(r => r.Tags?.Contains(tag) ?? false).ToArray();

        }
    }
}