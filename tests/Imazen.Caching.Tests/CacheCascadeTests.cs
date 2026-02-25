using Imazen.Caching;

namespace Imazen.Caching.Tests;

public class CacheCascadeTests : IDisposable
{
    private readonly InMemoryCacheProvider _memory;
    private readonly InMemoryCacheProvider _disk;
    private readonly InMemoryCacheProvider _cloud;
    private readonly CacheCascade _cascade;

    public CacheCascadeTests()
    {
        _memory = new InMemoryCacheProvider("memory", requiresInline: true);
        _disk = new InMemoryCacheProvider("disk");
        _cloud = new InMemoryCacheProvider("cloud", latencyZone: "s3:us-east-1");

        _cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "memory", "disk", "cloud" },
            EnableRequestCoalescing = true,
            CoalescingTimeoutMs = 5000,
            MaxUploadQueueBytes = 10 * 1024 * 1024,
            BloomFilterEstimatedItems = 1000,
            BloomFilterFalsePositiveRate = 0.01,
            BloomFilterSlots = 2
        });

        _cascade.RegisterProvider(_memory);
        _cascade.RegisterProvider(_disk);
        _cascade.RegisterProvider(_cloud);
    }

    public void Dispose()
    {
        _cascade.Dispose();
    }

    private static byte[] TestData(int size = 100)
    {
        var data = new byte[size];
        Random.Shared.NextBytes(data);
        return data;
    }

    [Fact]
    public async Task CacheMiss_CallsFactory_ReturnsCreated()
    {
        var key = CacheKey.FromStrings("source1", "variant1");
        var expectedData = TestData();
        int factoryCallCount = 0;

        var result = await _cascade.GetOrCreateAsync(key, async ct =>
        {
            Interlocked.Increment(ref factoryCallCount);
            return (expectedData, new CacheEntryMetadata { ContentType = "image/jpeg" });
        });

        Assert.Equal(CacheResultStatus.Created, result.Status);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(1, factoryCallCount);

        // Verify data
        using var ms = new MemoryStream();
        await result.Data!.CopyToAsync(ms);
        Assert.Equal(expectedData, ms.ToArray());

        result.Dispose();
    }

    [Fact]
    public async Task MemoryHit_ReturnsFromMemory()
    {
        var key = CacheKey.FromStrings("source2", "variant2");
        var data = TestData();

        // Pre-populate memory
        await _memory.StoreAsync(key, data, new CacheEntryMetadata { ContentType = "image/png" });

        var result = await _cascade.GetOrCreateAsync(key, ct =>
            throw new InvalidOperationException("Factory should not be called on hit"));

        Assert.Equal(CacheResultStatus.MemoryHit, result.Status);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal("memory", result.ProviderName);

        result.Dispose();
    }

    [Fact]
    public async Task DiskHit_ReturnsFromDisk_ReplicatesToMemory()
    {
        var key = CacheKey.FromStrings("source3", "variant3");
        var data = TestData();

        // Pre-populate disk only
        await _disk.StoreAsync(key, data, new CacheEntryMetadata { ContentType = "image/webp" });

        var result = await _cascade.GetOrCreateAsync(key, ct =>
            throw new InvalidOperationException("Factory should not be called on hit"));

        Assert.Equal(CacheResultStatus.DiskHit, result.Status);
        Assert.Equal("disk", result.ProviderName);

        result.Dispose();

        // After a disk hit, the data should be replicated to memory (inline)
        Assert.True(_memory.Contains(key));
    }

    [Fact]
    public async Task StoreToAllTiers_AfterFactoryCall()
    {
        var key = CacheKey.FromStrings("source4", "variant4");
        var data = TestData();

        var result = await _cascade.GetOrCreateAsync(key, async ct =>
        {
            return (data, new CacheEntryMetadata { ContentType = "image/jpeg" });
        });
        result.Dispose();

        // Memory should have it (inline store)
        Assert.True(_memory.Contains(key));

        // Disk should get it via upload queue
        await _cascade.UploadQueue.DrainAsync();
        Assert.True(_disk.Contains(key));
    }

    [Fact]
    public async Task CloudProvider_BloomGated()
    {
        var key = CacheKey.FromStrings("source5", "variant5");

        // Cloud has the data, but bloom filter hasn't been told
        await _cloud.StoreAsync(key, TestData(), new CacheEntryMetadata());

        // First request: bloom says "no" → cloud is skipped → factory called
        int factoryCallCount = 0;
        var result = await _cascade.GetOrCreateAsync(key, async ct =>
        {
            Interlocked.Increment(ref factoryCallCount);
            return (TestData(), new CacheEntryMetadata());
        });
        result.Dispose();

        // Factory was called because bloom filter skipped cloud
        Assert.Equal(1, factoryCallCount);
    }

    [Fact]
    public async Task Invalidate_RemovesFromAllTiers()
    {
        var key = CacheKey.FromStrings("source6", "variant6");
        var data = TestData();

        // Populate all tiers
        await _memory.StoreAsync(key, data, new CacheEntryMetadata());
        await _disk.StoreAsync(key, data, new CacheEntryMetadata());

        await _cascade.InvalidateAsync(key);

        Assert.False(_memory.Contains(key));
        Assert.False(_disk.Contains(key));
    }

    [Fact]
    public async Task SecondRequest_HitsMemory_NotFactory()
    {
        var key = CacheKey.FromStrings("source7", "variant7");
        var data = TestData();
        int factoryCallCount = 0;

        // First request: factory called
        var result1 = await _cascade.GetOrCreateAsync(key, async ct =>
        {
            Interlocked.Increment(ref factoryCallCount);
            return (data, new CacheEntryMetadata { ContentType = "image/jpeg" });
        });
        result1.Dispose();

        // Second request: should hit memory, not call factory
        var result2 = await _cascade.GetOrCreateAsync(key, ct =>
            throw new InvalidOperationException("Factory should not be called"));

        Assert.Equal(CacheResultStatus.MemoryHit, result2.Status);
        Assert.Equal(1, factoryCallCount);
        result2.Dispose();
    }

    [Fact]
    public async Task FactoryReturnsNull_ReturnsError()
    {
        var key = CacheKey.FromStrings("source8", "variant8");

        var result = await _cascade.GetOrCreateAsync(key, async ct =>
        {
            return ((byte[] Data, CacheEntryMetadata Metadata)?)null;
        });

        Assert.Equal(CacheResultStatus.Error, result.Status);
        result.Dispose();
    }

    [Fact]
    public void RegisterProvider_DuplicateName_Throws()
    {
        var cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "test" }
        });

        cascade.RegisterProvider(new InMemoryCacheProvider("test"));

        Assert.Throws<InvalidOperationException>(() =>
            cascade.RegisterProvider(new InMemoryCacheProvider("test")));

        cascade.Dispose();
    }

    [Fact]
    public async Task Events_FireOnHitAndMiss()
    {
        var events = new List<CacheEvent>();
        var cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "mem" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 100,
            OnCacheEvent = e => events.Add(e)
        });
        cascade.RegisterProvider(new InMemoryCacheProvider("mem", requiresInline: true));

        var key = CacheKey.FromStrings("src", "var");

        // Miss → Created
        var result = await cascade.GetOrCreateAsync(key, async ct =>
            (TestData(), new CacheEntryMetadata()));
        result.Dispose();

        Assert.Contains(events, e => e.Kind == CacheEventKind.Miss);
        Assert.Contains(events, e => e.Kind == CacheEventKind.Store);

        events.Clear();

        // Hit
        var result2 = await cascade.GetOrCreateAsync(key, ct =>
            throw new InvalidOperationException());
        result2.Dispose();

        Assert.Contains(events, e => e.Kind == CacheEventKind.Hit);

        cascade.Dispose();
    }
}
