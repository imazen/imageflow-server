using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Common.Concurrency;
using Microsoft.Extensions.Logging;

namespace Imazen.HybridCache.MetaStore
{
    internal class Shard
    {
        private readonly WriteLog writeLog;
        private readonly ILogger logger;
        private readonly int shardId;
        
        private volatile ConcurrentDictionary<string, CacheDatabaseRecord> dict;

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

                dict = await writeLog.Startup();
            }
            return dict;
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
        
        public async Task DeleteRecord(ICacheDatabaseRecord oldRecord)
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

                    }
                    else
                    {
                        await writeLog.LogDeleted(oldRecord);
                    }
                }
            }
        }

        public async Task<IEnumerable<ICacheDatabaseRecord>> GetDeletionCandidates(
            DateTime maxLastDeletionAttemptTime, DateTime maxCreatedDate, int count, Func<int, ushort> getUsageCount)
        {
            var results = (await GetLoadedDict()).Values
                .Where(r => r.CreatedAt < maxCreatedDate && r.LastDeletionAttempt < maxLastDeletionAttemptTime)
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
        public async Task<string> GetContentType(string relativePath)
        {
            return (await GetRecord(relativePath))?.ContentType;
        }

        public async Task<ICacheDatabaseRecord> GetRecord(string relativePath)
        {
            return (await GetLoadedDict()).TryGetValue(relativePath, out var record) ? 
                record: 
                null;
        }


        internal static int GetLogBytesOverhead(int stringLength)
        {
            return 4 * (128 + stringLength);
        }
        public async Task<bool> CreateRecordIfSpace(string relativePath, string contentType, long recordDiskSpace,
            DateTime createdDate,
            int accessCountKey, long diskSpaceLimit)
        {
            // Lock so multiple writers can't get past the capacity check simultaneously
            using (await createLock.LockAsync())
            {
                var loadedDict = await GetLoadedDict();
                var extraLogBytes = GetLogBytesOverhead(relativePath.Length + (contentType?.Length ?? 0));

                var existingDiskUsage = await GetShardSize();
                if (existingDiskUsage + recordDiskSpace + extraLogBytes > diskSpaceLimit)
                {
                    //logger?.LogInformation("Refusing new {RecordDiskSpace} byte record in shard {ShardId} of MetaStore",
                    //    recordDiskSpace, shardId);
                    return false;
                }

                var newRecord = new CacheDatabaseRecord()
                {
                    AccessCountKey = accessCountKey,
                    ContentType = contentType,
                    CreatedAt = createdDate,
                    DiskSize = recordDiskSpace,
                    LastDeletionAttempt = DateTime.MinValue,
                    RelativePath = relativePath
                };

                if (loadedDict.TryAdd(relativePath, newRecord))
                {
                    await writeLog.LogCreated(newRecord);

                }
                else
                {
                    logger?.LogWarning("CreateRecordIfSpace did nothing - database entry already exists - {Path}", relativePath);
                }
            }

            return true;

        }

        public async Task UpdateCreatedDate(string relativePath, string contentType, long recordDiskSpace, DateTime createdDate, int accessCountKey)
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
                    var record = (await GetLoadedDict()).GetOrAdd(relativePath, (s) =>

                        new CacheDatabaseRecord()
                        {
                            AccessCountKey = accessCountKey,
                            ContentType = contentType,
                            CreatedAt = createdDate,
                            DiskSize = recordDiskSpace,
                            LastDeletionAttempt = DateTime.MinValue,
                            RelativePath = relativePath
                        }
                    );
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
                AccessCountKey = record.AccessCountKey,
                ContentType = record.ContentType,
                CreatedAt = record.CreatedAt,
                DiskSize = record.DiskSize,
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
    }
}