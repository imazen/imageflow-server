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

        // Verify data via Data property
        Assert.NotNull(result.Data);
        Assert.Equal(expectedData, result.Data);

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

        // After a disk hit, memory should receive the data (it subscribes to external hits)
        Assert.True(_memory.Contains(key));
    }

    [Fact]
    public async Task StoreToSubscribers_AfterFactoryCall()
    {
        var key = CacheKey.FromStrings("source4", "variant4");
        var data = TestData();

        var result = await _cascade.GetOrCreateAsync(key, async ct =>
        {
            return (data, new CacheEntryMetadata { ContentType = "image/jpeg" });
        });
        result.Dispose();

        // Memory should have it (inline store, subscribes to fresh)
        Assert.True(_memory.Contains(key));

        // Disk should get it via upload queue (subscribes to fresh)
        await _cascade.UploadQueue.DrainAsync();
        Assert.True(_disk.Contains(key));
    }

    [Fact]
    public async Task SubscriptionModel_ProviderCanRejectData()
    {
        var memory = new InMemoryCacheProvider("memory", requiresInline: true);
        var disk = new InMemoryCacheProvider("disk") { AcceptsFreshResults = false }; // Disk rejects fresh data

        var cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "memory", "disk" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 100,
        });
        cascade.RegisterProvider(memory);
        cascade.RegisterProvider(disk);

        var key = CacheKey.FromStrings("reject-src", "reject-var");
        var result = await cascade.GetOrCreateAsync(key, async ct =>
            (TestData(), new CacheEntryMetadata()));
        result.Dispose();

        // Memory accepted the data
        Assert.True(memory.Contains(key));

        // Disk rejected it
        await cascade.UploadQueue.DrainAsync();
        Assert.False(disk.Contains(key));

        cascade.Dispose();
    }

    [Fact]
    public async Task SubscriptionModel_SizeLimit_RejectsLargeEntries()
    {
        var memory = new InMemoryCacheProvider("memory", requiresInline: true)
        {
            MaxAcceptableBytes = 50 // Only accept entries <= 50 bytes
        };
        var disk = new InMemoryCacheProvider("disk");

        var cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "memory", "disk" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 100,
        });
        cascade.RegisterProvider(memory);
        cascade.RegisterProvider(disk);

        var key = CacheKey.FromStrings("big-src", "big-var");
        var largeData = TestData(200); // 200 bytes > 50 limit

        var result = await cascade.GetOrCreateAsync(key, async ct =>
            (largeData, new CacheEntryMetadata()));
        result.Dispose();

        // Memory rejected (too large)
        Assert.False(memory.Contains(key));

        // Disk accepted
        await cascade.UploadQueue.DrainAsync();
        Assert.True(disk.Contains(key));

        cascade.Dispose();
    }

    [Fact]
    public async Task SubscriptionModel_ExternalHitReplication_Bidirectional()
    {
        // Test that replication isn't just "up" — a disk hit can replicate to cloud
        var memory = new InMemoryCacheProvider("memory", requiresInline: true);
        var disk = new InMemoryCacheProvider("disk");
        var cloud = new InMemoryCacheProvider("cloud", latencyZone: "s3:us-east-1")
        {
            AcceptsExternalHits = true // Cloud wants data it missed
        };

        var cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "memory", "disk", "cloud" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 100,
        });
        cascade.RegisterProvider(memory);
        cascade.RegisterProvider(disk);
        cascade.RegisterProvider(cloud);

        var key = CacheKey.FromStrings("bidir-src", "bidir-var");
        var data = TestData();

        // Pre-populate disk only
        await disk.StoreAsync(key, data, new CacheEntryMetadata());

        var result = await cascade.GetOrCreateAsync(key, ct =>
            throw new InvalidOperationException("Should hit disk"));
        result.Dispose();

        // Memory should get it (faster tier, subscribes to external hits)
        Assert.True(memory.Contains(key));

        // Cloud should also get it (subscribes to external hits) — via upload queue
        await cascade.UploadQueue.DrainAsync();
        Assert.True(cloud.Contains(key));

        cascade.Dispose();
    }

    [Fact]
    public async Task SubscriptionModel_NoSubscribers_NoStoreEvents()
    {
        var events = new List<CacheEvent>();
        var memory = new InMemoryCacheProvider("memory", requiresInline: true)
        {
            AcceptsFreshResults = false,
            AcceptsExternalHits = false
        };

        var cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "memory" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 100,
            OnCacheEvent = e => events.Add(e)
        });
        cascade.RegisterProvider(memory);

        var key = CacheKey.FromStrings("nosub-src", "nosub-var");
        var result = await cascade.GetOrCreateAsync(key, async ct =>
            (TestData(), new CacheEntryMetadata()));
        result.Dispose();

        // No Store events should fire since nobody subscribed
        Assert.DoesNotContain(events, e => e.Kind == CacheEventKind.Store);
        Assert.False(memory.Contains(key));

        cascade.Dispose();
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

    [Fact]
    public async Task GetStream_ReturnsUsableStream()
    {
        var key = CacheKey.FromStrings("stream-src", "stream-var");
        var expectedData = TestData();

        var result = await _cascade.GetOrCreateAsync(key, async ct =>
            (expectedData, new CacheEntryMetadata { ContentType = "image/jpeg" }));

        // GetStream() should return a usable stream regardless of path
        using var stream = result.GetStream();
        Assert.NotNull(stream);
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        Assert.Equal(expectedData, ms.ToArray());

        result.Dispose();
    }
}
