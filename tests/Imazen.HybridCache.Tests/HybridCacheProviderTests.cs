using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Caching;
using Imazen.HybridCache;
using MetaStoreClass = Imazen.HybridCache.MetaStore.MetaStore;
using Imazen.Tests.Routing.Serving;

namespace Imazen.HybridCache.Tests;

/// <summary>
/// Tests for HybridCacheProvider, which adapts HybridCache to the ICacheProvider interface.
/// Each test creates a temporary directory for the cache and cleans up after.
/// </summary>
public class HybridCacheProviderTests : ReLoggerTestBase
{
    public HybridCacheProviderTests() : base("HybridCacheProviderTests") { }

    private (HybridCache cache, HybridCacheProvider provider, string tempDir) CreateProvider()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hcp-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var options = new HybridCacheAdvancedOptions("disk-test", tempDir)
        {
            AsyncCacheOptions = new AsyncCacheOptions { UniqueName = "disk-test" },
            CleanupManagerOptions = new CleanupManagerOptions
            {
                MaxCacheBytes = 100 * 1024 * 1024, // 100MB
                MinCleanupBytes = 1024 * 1024,
                MinAgeToDelete = TimeSpan.FromSeconds(0), // Allow immediate eviction for tests
            }
        };

        var database = new MetaStoreClass(new MetaStore.MetaStoreOptions(tempDir), options, logger);
        var cache = new HybridCache(database, options, logger);
        var provider = new HybridCacheProvider(cache);
        return (cache, provider, tempDir);
    }

    private async Task CleanupAsync(HybridCache cache, string tempDir)
    {
        try { await cache.StopAsync(CancellationToken.None); } catch { }
        try { Directory.Delete(tempDir, true); } catch { }
    }

    [Fact]
    public async Task StoreFetch_RoundTrip()
    {
        var (cache, provider, tempDir) = CreateProvider();
        try
        {
            await cache.StartAsync(CancellationToken.None);

            var key = CacheKey.FromStrings("/images/photo.jpg", "width=800&format=webp");
            var data = Encoding.UTF8.GetBytes("fake image data for round trip test");
            var metadata = new CacheEntryMetadata
            {
                ContentType = "image/webp",
                ContentLength = data.Length
            };

            // Store
            await provider.StoreAsync(key, data, metadata);

            // Fetch — should return stream-based result
            var result = await provider.FetchAsync(key);
            Assert.NotNull(result);
            Assert.Null(result!.Data);       // Disk provider returns stream, not byte[]
            Assert.NotNull(result.DataStream);
            Assert.Equal(data.Length, result.ContentLength);
            Assert.Equal("image/webp", result.Metadata.ContentType);

            // Read the stream and verify contents
            using (result)
            {
                using var ms = new MemoryStream();
                await result.DataStream!.CopyToAsync(ms);
                Assert.Equal(data, ms.ToArray());
            }
        }
        finally
        {
            await CleanupAsync(cache, tempDir);
        }
    }

    [Fact]
    public async Task FetchMiss_ReturnsNull()
    {
        var (cache, provider, tempDir) = CreateProvider();
        try
        {
            await cache.StartAsync(CancellationToken.None);

            var key = CacheKey.FromStrings("/nonexistent", "params");
            var result = await provider.FetchAsync(key);
            Assert.Null(result);
        }
        finally
        {
            await CleanupAsync(cache, tempDir);
        }
    }

    [Fact]
    public async Task Store_MultipleDifferentKeys_AllRetrievable()
    {
        var (cache, provider, tempDir) = CreateProvider();
        try
        {
            await cache.StartAsync(CancellationToken.None);

            var keys = new[]
            {
                CacheKey.FromStrings("/images/a.jpg", "w=100"),
                CacheKey.FromStrings("/images/a.jpg", "w=200"),
                CacheKey.FromStrings("/images/b.jpg", "w=100"),
            };

            for (int i = 0; i < keys.Length; i++)
            {
                var data = Encoding.UTF8.GetBytes($"content-{i}");
                await provider.StoreAsync(keys[i], data, new CacheEntryMetadata
                {
                    ContentType = "image/jpeg",
                    ContentLength = data.Length
                });
            }

            // All should be retrievable
            for (int i = 0; i < keys.Length; i++)
            {
                var result = await provider.FetchAsync(keys[i]);
                Assert.NotNull(result);
                using (result!)
                {
                    using var ms = new MemoryStream();
                    await result.DataStream!.CopyToAsync(ms);
                    Assert.Equal($"content-{i}", Encoding.UTF8.GetString(ms.ToArray()));
                }
            }
        }
        finally
        {
            await CleanupAsync(cache, tempDir);
        }
    }

    [Fact]
    public async Task Invalidate_RemovesEntry()
    {
        var (cache, provider, tempDir) = CreateProvider();
        try
        {
            await cache.StartAsync(CancellationToken.None);

            var key = CacheKey.FromStrings("/images/delete-me.jpg", "w=100");
            var data = Encoding.UTF8.GetBytes("to be deleted");
            await provider.StoreAsync(key, data, new CacheEntryMetadata { ContentType = "image/jpeg" });

            // Verify it's there
            var result = await provider.FetchAsync(key);
            Assert.NotNull(result);
            result!.Dispose();

            // Invalidate
            var removed = await provider.InvalidateAsync(key);
            Assert.True(removed);

            // Verify it's gone
            var result2 = await provider.FetchAsync(key);
            Assert.Null(result2);
        }
        finally
        {
            await CleanupAsync(cache, tempDir);
        }
    }

    [Fact]
    public async Task WantsToStore_AcceptsFreshAndMissed_RejectsNotQueried()
    {
        var (cache, provider, tempDir) = CreateProvider();
        try
        {
            var key = CacheKey.FromStrings("/images/test.jpg", "w=100");
            Assert.True(provider.WantsToStore(key, 1000, CacheStoreReason.FreshlyCreated));
            Assert.True(provider.WantsToStore(key, 1000, CacheStoreReason.Missed));
            Assert.False(provider.WantsToStore(key, 1000, CacheStoreReason.NotQueried));
        }
        finally
        {
            await CleanupAsync(cache, tempDir);
        }
    }

    [Fact]
    public async Task Capabilities_IsLocalDisk()
    {
        var (cache, provider, tempDir) = CreateProvider();
        try
        {
            Assert.False(provider.Capabilities.RequiresInlineExecution);
            Assert.Equal("local", provider.Capabilities.LatencyZone);
            Assert.True(provider.Capabilities.IsLocal);
        }
        finally
        {
            await CleanupAsync(cache, tempDir);
        }
    }

    [Fact]
    public async Task HealthCheck_ReturnsTrue_WhenHealthy()
    {
        var (cache, provider, tempDir) = CreateProvider();
        try
        {
            await cache.StartAsync(CancellationToken.None);
            var healthy = await provider.HealthCheckAsync();
            Assert.True(healthy);
        }
        finally
        {
            await CleanupAsync(cache, tempDir);
        }
    }

    [Fact]
    public async Task Name_MatchesCacheName()
    {
        var (cache, provider, tempDir) = CreateProvider();
        try
        {
            Assert.Equal("disk-test", provider.Name);
        }
        finally
        {
            await CleanupAsync(cache, tempDir);
        }
    }

    [Fact]
    public async Task StoreOverwrite_SecondStoreDoesNotCorrupt()
    {
        var (cache, provider, tempDir) = CreateProvider();
        try
        {
            await cache.StartAsync(CancellationToken.None);

            var key = CacheKey.FromStrings("/images/overwrite.jpg", "w=100");

            // Store first version
            var data1 = Encoding.UTF8.GetBytes("version-1");
            await provider.StoreAsync(key, data1, new CacheEntryMetadata { ContentType = "image/jpeg" });

            // Store second version (same key — file already exists)
            var data2 = Encoding.UTF8.GetBytes("version-2");
            await provider.StoreAsync(key, data2, new CacheEntryMetadata { ContentType = "image/jpeg" });

            // Fetch — should return one of the versions (first wins in HybridCache)
            var result = await provider.FetchAsync(key);
            Assert.NotNull(result);
            using (result!)
            {
                using var ms = new MemoryStream();
                await result.DataStream!.CopyToAsync(ms);
                var content = Encoding.UTF8.GetString(ms.ToArray());
                // HybridCache skips write if file already exists
                Assert.Equal("version-1", content);
            }
        }
        finally
        {
            await CleanupAsync(cache, tempDir);
        }
    }

    [Fact]
    public async Task ConcurrentStoreAndFetch_NoCorruption()
    {
        var (cache, provider, tempDir) = CreateProvider();
        try
        {
            await cache.StartAsync(CancellationToken.None);

            var tasks = new Task[20];
            for (int i = 0; i < 20; i++)
            {
                var idx = i;
                tasks[i] = Task.Run(async () =>
                {
                    var key = CacheKey.FromStrings($"/images/concurrent-{idx}.jpg", "w=100");
                    var data = Encoding.UTF8.GetBytes($"data-{idx}");
                    await provider.StoreAsync(key, data, new CacheEntryMetadata
                    {
                        ContentType = "image/jpeg",
                        ContentLength = data.Length
                    });

                    // Immediately try to fetch
                    var result = await provider.FetchAsync(key);
                    Assert.NotNull(result);
                    using (result!)
                    {
                        using var ms = new MemoryStream();
                        await result.DataStream!.CopyToAsync(ms);
                        Assert.Equal($"data-{idx}", Encoding.UTF8.GetString(ms.ToArray()));
                    }
                });
            }

            await Task.WhenAll(tasks);
        }
        finally
        {
            await CleanupAsync(cache, tempDir);
        }
    }
}
