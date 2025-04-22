using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Common.Extensibility.StreamCache;
using Imazen.HybridCache.MetaStore;
using Imazen.Routing.Caching.Internal;
using Imazen.Routing.Tests.Serving;
using Imazen.Tests.Routing.Serving;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Imazen.HybridCache.Tests
{
    public class HybridCacheTests : ReLoggerTestBase
    {
        public HybridCacheTests() : base("HybridCacheTests")
        {
        }
        
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
                    UniqueName = "HybridCache"
                }
            };
            var database = new MetaStore.MetaStore(new MetaStoreOptions(path), cacheOptions, logger);
            HybridCache hybridCache = new HybridCache(database,cacheOptions, logger);
            var memoryLogger = MockHelpers.MakeMemoryLoggerFactory(new List<MemoryLogEntry>());
            var cache = new LegacyStreamCacheAdapter(hybridCache,new StreamCacheAdapterOptions()
            {
                WriteSynchronouslyWhenQueueFull = true,
            },logger);
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
                using var rd = result as IDisposable;
                Assert.Equal("WriteSucceeded", result.Status);
                
                var result2 = await cache.GetOrCreateBytes(key, DataProvider, cancellationToken, true);
                using var rd2 = result2 as IDisposable;
                Assert.Equal("DiskHit", result2.Status);
                Assert.Equal(contentType, result2.ContentType);
                Assert.NotNull(result2.Data);
                
                //Assert.NotNull(((AsyncCache.AsyncCacheResultOld)result2).CreatedAt);
                await cache.AwaitAllCurrentTasks(default);
                
                var result3 = await cache.GetOrCreateBytes(key, DataProvider, cancellationToken, true);
                using var rd3 = result3 as IDisposable;
                Assert.Equal("DiskHit", result3.Status);
                Assert.Equal(contentType, result3.ContentType);
                //Assert.NotNull(((AsyncCache.AsyncCacheResultOld)result3).CreatedAt);
                Assert.NotNull(result3.Data);
                var key2 = new byte[] {2, 1, 2, 3};
                Task<IStreamCacheInput> DataProvider2(CancellationToken token)
                {
                    return Task.FromResult(new StreamCacheInput("application/octet-stream", new ArraySegment<byte>(new byte[4000])).ToIStreamCacheInput());
                }
                var result4 = await cache.GetOrCreateBytes(key2, DataProvider2, cancellationToken, true);
                using var rd4 = result4 as IDisposable;
                Assert.Equal("WriteSucceeded", result4.Status);
     
                var result5 = await cache.GetOrCreateBytes(key2, DataProvider, cancellationToken, true);
                using var rd5 = result5 as IDisposable;
                Assert.Equal("DiskHit", result5.Status);
                Assert.Null(result5.ContentType);
                Assert.NotNull(result5.Data);
            }
            finally
            {
                try
                {
                    await cache.AwaitAllCurrentTasks(cancellationToken);
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