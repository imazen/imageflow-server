using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Concurrency;
using Imazen.Common.Extensibility.StreamCache;
using Imazen.Common.Extensibility.Support;
using Imazen.HybridCache.MetaStore;
using Xunit;

namespace Imazen.HybridCache.Tests
{
    public class AsyncCacheTests
    {
        internal class NullCacheManager : ICacheCleanupManager
        {
            public long EstimateFileSizeOnDisk(long byteCount)
            {
                return byteCount;
            }

            public void NotifyUsed(CacheEntry cacheEntry){}
            public Task<string> GetContentType(CacheEntry cacheEntry, CancellationToken cancellationToken) => null;
            public Task<ReserveSpaceResult> TryReserveSpace(CacheEntry cacheEntry, string contentType, int byteCount, bool allowEviction,
                AsyncLockProvider writeLocks, 
                CancellationToken cancellationToken) => Task.FromResult(new ReserveSpaceResult(){Success = true});

            public Task MarkFileCreated(CacheEntry cacheEntry, string contentType, long recordDiskSpace, DateTime createdDate)
            {
                return Task.FromResult(true);
            }

            public Task<ICacheDatabaseRecord?> GetRecordReference(CacheEntry cacheEntry, CancellationToken cancellationToken)
            {
                return Task.FromResult<ICacheDatabaseRecord?>(null);
            }

            public Task<ReserveSpaceResult> TryReserveSpace(CacheEntry cacheEntry, CacheDatabaseRecord newRecord, bool allowEviction,
                AsyncLockProvider writeLocks, CancellationToken cancellationToken)
            {
                return Task.FromResult(new ReserveSpaceResult(){Success = true});
            }

            public int GetAccessCountKey(CacheEntry cacheEntry)
            {
                return 0;
            }

            public Task MarkFileCreated(CacheEntry cacheEntry, DateTime createdDate, Func<CacheDatabaseRecord> createIfMissing)
            {
                return Task.FromResult(true);
            }

            public Task<CodeResult<IAsyncEnumerable<IBlobStorageReference>>> CacheSearchByTag(SearchableBlobTag tag, CancellationToken cancellationToken = default)
            {
                // return empty list
                return Task.FromResult(CodeResult<IAsyncEnumerable<IBlobStorageReference>>.Ok(new List<IBlobStorageReference>().ToAsyncEnumerable()));
            }

            public Task<CodeResult<IAsyncEnumerable<CodeResult<IBlobStorageReference>>>> CachePurgeByTag(SearchableBlobTag tag, AsyncLockProvider writeLocks,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(CodeResult<IAsyncEnumerable<CodeResult<IBlobStorageReference>>>.Ok(new List<CodeResult<IBlobStorageReference>>().ToAsyncEnumerable()));
            }


            public Task<CodeResult> CacheDelete(string relativePath, AsyncLockProvider writeLocks, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }

        [Fact]
        public async Task SmokeTestAsyncMissHit()
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}");
            Directory.CreateDirectory(path);
            AsyncCache cache = null;
            try
            {
                var builder = new HashBasedPathBuilder(path,
                    8192, '/', ".jpg");
                
                cache = new AsyncCache(new AsyncCacheOptions
                {
                    UniqueName = "smoketest" 
                }, new NullCacheManager(), builder,null);

                var keyBasis = new byte[] {6,1,2};
                var result = await cache.GetOrCreateBytes(keyBasis, (token) =>
                    {
                        return Task.FromResult(new StreamCacheInput(
                            null, new ArraySegment<byte>(new byte[] {3, 2, 1})).ToIStreamCacheInput());
                    },
                    CancellationToken.None, false);
                Assert.NotNull(result.Data);
                await result.Data.DisposeAsync();
                Assert.Equal("Miss", result.Status);

                await cache.AwaitEnqueuedTasks();

                var result2 = await cache.GetOrCreateBytes(keyBasis, (token) =>
                    {
                        return Task.FromResult(new StreamCacheInput(
                            null, new ArraySegment<byte>(new byte[] {3, 2, 1})).ToIStreamCacheInput());
                    },
                    CancellationToken.None, false);
                Assert.NotNull(result2.Data);
                await result2.Data.DisposeAsync();
                Assert.Equal("DiskHit", result2.Status);

                
                var hash = builder.HashKeyBasis(keyBasis);
                var expectedPhysicalPath = builder.GetPhysicalPathFromHash(hash);
                Assert.True(File.Exists(expectedPhysicalPath));
            }
            finally
            {
                try
                {
                    await cache.AwaitEnqueuedTasks();
                    
                }
                finally
                {
                    Directory.Delete(path, true);
                }
            }
        }
        
        
        [Fact]
        public async Task SmokeTestSyncMissHit()
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}");
            Directory.CreateDirectory(path);
            AsyncCache cache= null;
            try
            {
                var asyncCacheOptions = new AsyncCacheOptions()
                {
                    MaxQueuedBytes = 0
                };
                var builder = new HashBasedPathBuilder(path,
                    8192, '/', ".jpg");
                
                cache = new AsyncCache(asyncCacheOptions, new NullCacheManager(), builder,null);

                var keyBasis = new byte[] {6,1,2};
                var result = await cache.GetOrCreateBytes(keyBasis, (token) =>
                    {
                        return Task.FromResult(new StreamCacheInput(
                            null, new ArraySegment<byte>(new byte[] {3, 2, 1})).ToIStreamCacheInput());
                    },
                    CancellationToken.None, false);
                Assert.NotNull(result.Data);
                await result.Data.DisposeAsync();
                Assert.Equal("WriteSucceeded", result.Status);

                await cache.AwaitEnqueuedTasks();

                var result2 = await cache.GetOrCreateBytes(keyBasis, (token) =>
                    {
                        return Task.FromResult(new StreamCacheInput(
                            null, new ArraySegment<byte>(new byte[] {3, 2, 1})).ToIStreamCacheInput());
                    },
                    CancellationToken.None, false);
                Assert.NotNull(result2.Data);
                await result2.Data.DisposeAsync();
                Assert.Equal("DiskHit", result2.Status);
                var hash = builder.HashKeyBasis(keyBasis);
                var expectedPhysicalPath = builder.GetPhysicalPathFromHash(hash);
                Assert.True(File.Exists(expectedPhysicalPath));
            }
            finally
            {
                try
                {
                    await cache?.AwaitEnqueuedTasks();
                }
                finally
                {
                    Directory.Delete(path, true);
                }
            }
        }

    }
}