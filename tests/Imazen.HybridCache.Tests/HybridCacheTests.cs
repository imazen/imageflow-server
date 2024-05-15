using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Common.Extensibility.StreamCache;
using Imazen.HybridCache.MetaStore;
using Imazen.Routing.Caching.Internal;
using Imazen.Routing.Tests.Serving;
using Xunit;

namespace Imazen.HybridCache.Tests
{
    public class HybridCacheTests
    {
        
        
        [Fact]
        public async Task SmokeTest()
        {
            var cancellationToken = CancellationToken.None;
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}");
            Directory.CreateDirectory(path);
            var cacheOptions = new HybridCacheAdvancedOptions("HybridCache",path)
            {
                AsyncCacheOptions = new AsyncCacheOptions
                {
                    MaxQueuedBytes = 0,
                    UniqueName = "HybridCache"
                }
            };
            var database = new MetaStore.MetaStore(new MetaStoreOptions(path), cacheOptions, null);
            HybridCache hybridCache = new HybridCache(database,cacheOptions, null);
            var memoryLogger = MockHelpers.MakeMemoryLoggerFactory(new List<MemoryLogEntry>());
            var cache = new LegacyStreamCacheAdapter(hybridCache,new StreamCacheAdapterOptions(),null);
            try
            {
                await cache.StartAsync(cancellationToken);

                var key = new byte[] {0, 1, 2, 3};
                var contentType = "application/octet-stream";

                Task<IStreamCacheInput> DataProvider(CancellationToken token)
                {
                    return Task.FromResult(new StreamCacheInput(contentType, new ArraySegment<byte>(new byte[4000])).ToIStreamCacheInput());
                }

                var result = await cache.GetOrCreateBytes(key, DataProvider, cancellationToken, true);
                Assert.Equal("WriteSucceeded", result.Status);
                result.Data.Dispose();
                
                var result2 = await cache.GetOrCreateBytes(key, DataProvider, cancellationToken, true);
                Assert.Equal("DiskHit", result2.Status);
                Assert.Equal(contentType, result2.ContentType);
                Assert.NotNull(result2.Data);
                
                //Assert.NotNull(((AsyncCache.AsyncCacheResultOld)result2).CreatedAt);
                result2.Data.Dispose();
                await cache.AwaitAllCurrentTasks(default);
                
                var result3 = await cache.GetOrCreateBytes(key, DataProvider, cancellationToken, true);
                Assert.Equal("DiskHit", result3.Status);
                Assert.Equal(contentType, result3.ContentType);
                //Assert.NotNull(((AsyncCache.AsyncCacheResultOld)result3).CreatedAt);
                Assert.NotNull(result3.Data);
                result3.Data.Dispose();
                var key2 = new byte[] {2, 1, 2, 3};
                Task<IStreamCacheInput> DataProvider2(CancellationToken token)
                {
                    return Task.FromResult(new StreamCacheInput(null, new ArraySegment<byte>(new byte[4000])).ToIStreamCacheInput());
                }
                var result4 = await cache.GetOrCreateBytes(key2, DataProvider2, cancellationToken, true);
                Assert.Equal("WriteSucceeded", result4.Status);
                result4.Data.Dispose();
                var result5 = await cache.GetOrCreateBytes(key2, DataProvider, cancellationToken, true);
                Assert.Equal("DiskHit", result5.Status);
                Assert.Null(result5.ContentType);
                Assert.NotNull(result5.Data);
                result5.Data.Dispose();
            }
            finally
            {
                try
                {
                    await cache.StopAsync(cancellationToken);
                }
                finally
                {
                    Directory.Delete(path, true);
                }
            }

        }
    }
}