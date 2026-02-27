using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Imazen.HybridCache.MetaStore
{
    public class MetaStore: ICacheDatabase
    {
        private readonly Shard[] shards;


        public MetaStore(MetaStoreOptions options, HybridCacheOptions cacheOptions, ILogger logger)
        {
            if (options.Shards <= 0 || options.Shards > 2048)
            {
                throw new ArgumentException("Shards must be between 1 and 2048");
            }

            var pathBuilder =
                new HashBasedPathBuilder(cacheOptions.PhysicalCacheDir, cacheOptions.Subfolders, '/', ".jpg");

            var directoryEntriesBytes = pathBuilder.GetDirectoryEntriesBytesTotal() + CleanupManager.DirectoryEntrySize();

            var shardCount = options.Shards;
            shards = new Shard[shardCount];
            for (var i = 0; i < shardCount; i++)
            {
                shards[i] = new Shard(i, options, Path.Combine(options.DatabaseDir,"db", i.ToString()),directoryEntriesBytes / shardCount, logger);
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.WhenAll(shards.Select(s => s.StartAsync(cancellationToken)));
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.WhenAll(shards.Select(s => s.StopAsync(cancellationToken)));
        }

        public Task UpdateLastDeletionAttempt(int shard, string relativePath, DateTime when)
        {
            return shards[shard].UpdateLastDeletionAttempt(relativePath, when);
        }

        public int GetShardForKey(string key)
        {
#if NET8_0_OR_GREATER
            var keyByteCount = Encoding.UTF8.GetByteCount(key);
            Span<byte> keyBytes = keyByteCount <= 256 ? stackalloc byte[keyByteCount] : new byte[keyByteCount];
            Encoding.UTF8.GetBytes(key, keyBytes);
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(keyBytes, hash);
            var shardSeed = BitConverter.ToUInt32(hash.Slice(8, 4));
            return (int)(shardSeed % (uint)shards.Length);
#else
            var stringBytes = Encoding.UTF8.GetBytes(key);
            byte[] a;
            lock (Sha256Lock)
            {
                a = Sha256Instance.ComputeHash(stringBytes);
            }
            var shardSeed = BitConverter.ToUInt32(a, 8);
            return (int)(shardSeed % shards.Length);
#endif
        }

#if !NET8_0_OR_GREATER
        private static readonly SHA256 Sha256Instance = SHA256.Create();
        private static readonly object Sha256Lock = new object();
#endif

        public Task DeleteRecord(int shard, ICacheDatabaseRecord record)
        {
            if (record.CreatedAt > DateTime.UtcNow)
                throw new InvalidOperationException();
            return shards[shard].DeleteRecord(record);
        }

        public Task<IEnumerable<ICacheDatabaseRecord>> GetDeletionCandidates(int shard, DateTime maxLastDeletionAttemptTime, DateTime maxCreatedDate, int count,
            Func<int, ushort> getUsageCount)
        {
            return shards[shard]
                .GetDeletionCandidates(maxLastDeletionAttemptTime, maxCreatedDate, count, getUsageCount);
        }

        public Task<long> GetShardSize(int shard)
        {
            return shards[shard].GetShardSize();
        }

        public int GetShardCount()
        {
            return shards.Length;
        }

        public Task<string> GetContentType(int shard, string relativePath)
        {
            return shards[shard].GetContentType(relativePath);
        }

        public Task<ICacheDatabaseRecord> GetRecord(int shard, string relativePath)
        {
            return shards[shard].GetRecord(relativePath);
        }

        public int EstimateRecordDiskSpace(int stringLength)
        {
            return Shard.GetLogBytesOverhead(stringLength);
        }

        public Task<bool> CreateRecordIfSpace(int shard, string relativePath, string contentType, long recordDiskSpace, DateTime createdDate,
            int accessCountKey, long diskSpaceLimit)
        {
            return shards[shard].CreateRecordIfSpace(relativePath, contentType, recordDiskSpace, createdDate,
                accessCountKey, diskSpaceLimit);
        }

        public Task UpdateCreatedDateAtomic(int shard, string relativePath, string contentType, long recordDiskSpace, DateTime createdDate, int accessCountKey)
        {
            return shards[shard].UpdateCreatedDate(relativePath, contentType, recordDiskSpace, createdDate, accessCountKey);
        }

        public Task ReplaceRelativePathAndUpdateLastDeletion(int shard, ICacheDatabaseRecord record, string movedRelativePath,
            DateTime lastDeletionAttempt)
        {
            return shards[shard].ReplaceRelativePathAndUpdateLastDeletion(record, movedRelativePath, lastDeletionAttempt);
        }
    }
}