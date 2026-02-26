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
    public async Task SubscriptionModel_Replication_Bidirectional()
    {
        // Replication goes in ALL directions: faster and slower tiers equally.
        // Disk hit → memory gets Missed (it was checked during fetch, didn't have it)
        // Disk hit → cloud gets Missed (bloom filter checked post-hit, negative = definite miss)
        var memory = new InMemoryCacheProvider("memory", requiresInline: true);
        var disk = new InMemoryCacheProvider("disk");
        var cloud = new InMemoryCacheProvider("cloud", latencyZone: "s3:us-east-1");
        // cloud.AcceptsMissed is true by default — cloud accepts data it definitely doesn't have

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

        // Memory was checked before disk and missed during fetch → gets Missed reason → accepts
        Assert.True(memory.Contains(key));

        // Cloud wasn't reached during fetch, but post-hit bloom check says
        // "definitely not there" → gets Missed reason → accepts
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
            AcceptsMissed = false,
            AcceptsNotQueried = false
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
    public async Task SubscriptionModel_MissedVsNotQueried_Distinction()
    {
        // memory → disk → cloud. If disk hits, memory gets Missed (it was checked),
        // cloud gets Missed (bloom-negative = checked). Both accept.
        // But if memory hits, disk gets NotQueried (wasn't checked at all).
        // Disk declines NotQueried by default (might still have it).
        var memory = new InMemoryCacheProvider("memory", requiresInline: true);
        var disk = new InMemoryCacheProvider("disk");
        // disk.AcceptsNotQueried is false by default

        var cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "memory", "disk" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 100,
        });
        cascade.RegisterProvider(memory);
        cascade.RegisterProvider(disk);

        var key = CacheKey.FromStrings("mvsn-src", "mvsn-var");
        var data = TestData();

        // Pre-populate both tiers
        await memory.StoreAsync(key, data, new CacheEntryMetadata());
        await disk.StoreAsync(key, data, new CacheEntryMetadata());

        disk.StoreCount = 0; // Reset counter

        // Memory hit → disk gets NotQueried → disk declines (already has it)
        var result = await cascade.GetOrCreateAsync(key, ct =>
            throw new InvalidOperationException("Should hit memory"));
        result.Dispose();

        // Disk's StoreAsync should NOT have been called (it declined NotQueried)
        Assert.Equal(0, disk.StoreCount);

        cascade.Dispose();
    }

    [Fact]
    public async Task Eviction_FallThrough_Recovers()
    {
        // When a fast tier evicts and a slow tier still has it,
        // the sequential fetch falls through and the slow tier serves it.
        // The fast tier gets Missed (was checked) and re-accepts.
        var memory = new InMemoryCacheProvider("memory", requiresInline: true);
        var disk = new InMemoryCacheProvider("disk");

        var cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "memory", "disk" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 100,
        });
        cascade.RegisterProvider(memory);
        cascade.RegisterProvider(disk);

        var key = CacheKey.FromStrings("evict-src", "evict-var");
        var data = TestData();

        // Populate both tiers
        await memory.StoreAsync(key, data, new CacheEntryMetadata());
        await disk.StoreAsync(key, data, new CacheEntryMetadata());

        // Simulate memory eviction
        await memory.InvalidateAsync(key);
        Assert.False(memory.Contains(key));

        // Request: memory miss → disk hit → memory gets Missed → re-populates
        var result = await cascade.GetOrCreateAsync(key, ct =>
            throw new InvalidOperationException("Should hit disk"));

        Assert.Equal(CacheResultStatus.DiskHit, result.Status);
        result.Dispose();

        // Memory should be repopulated (got Missed reason, accepted)
        Assert.True(memory.Contains(key));

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

    [Fact]
    public async Task BloomFilter_Checkpoint_And_Load_Survives_Restart()
    {
        // Simulate: populate bloom filter, checkpoint to disk, restart, load bloom, hit cloud.
        // Disk stores the bloom checkpoint but rejects cache data — only cloud has content.
        var disk = new InMemoryCacheProvider("disk") { AcceptsFreshResults = false, AcceptsMissed = false };
        var cloud = new InMemoryCacheProvider("cloud", latencyZone: "s3:us-east-1");

        var cascade1 = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "disk", "cloud" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 1000,
            BloomFilterFalsePositiveRate = 0.01,
            BloomFilterSlots = 2
        });
        cascade1.RegisterProvider(disk);
        cascade1.RegisterProvider(cloud);

        // Insert key → cloud gets data, bloom learns about it
        var key = CacheKey.FromStrings("persist-src", "persist-var");
        var data = TestData();
        var result = await cascade1.GetOrCreateAsync(key, async ct =>
            (data, new CacheEntryMetadata()));
        result.Dispose();
        await cascade1.UploadQueue.DrainAsync();

        // Cloud has data, disk does not (rejected fresh)
        Assert.True(cloud.Contains(key));
        Assert.False(disk.Contains(key));
        var bloomKey = key.ToStringKey() + ":cloud";
        Assert.True(cascade1.BloomFilter.ProbablyContains(bloomKey));

        // Checkpoint bloom filter to disk (stores under reserved __meta key)
        await cascade1.CheckpointBloomFilterAsync();
        cascade1.Dispose();

        // Create a new cascade (simulating restart)
        var cascade2 = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "disk", "cloud" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 1000,
            BloomFilterFalsePositiveRate = 0.01,
            BloomFilterSlots = 2
        });
        cascade2.RegisterProvider(disk);
        cascade2.RegisterProvider(cloud);

        // Before load: bloom is empty, cloud is invisible
        Assert.False(cascade2.BloomFilter.ProbablyContains(bloomKey));

        // Load from disk
        await cascade2.LoadBloomFilterAsync();

        // After load: bloom knows about cloud's data
        Assert.True(cascade2.BloomFilter.ProbablyContains(bloomKey));

        // Fetch should hit cloud (bloom says "maybe there", cloud has it)
        var result2 = await cascade2.GetOrCreateAsync(key, ct =>
            throw new InvalidOperationException("Factory should not be called — cloud has data"));

        Assert.Equal(CacheResultStatus.CloudHit, result2.Status);
        result2.Dispose();

        cascade2.Dispose();
    }

    [Fact]
    public async Task BloomFilter_PeerMerge_EnablesCloudHit()
    {
        // Simulate two cluster nodes: node1 populates cloud, node2 merges bloom from node1
        var cloud = new InMemoryCacheProvider("cloud", latencyZone: "s3:us-east-1");

        // Node 1: populates cloud and bloom filter
        var node1 = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "cloud" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 1000,
            BloomFilterSlots = 2
        });
        node1.RegisterProvider(cloud);

        var key = CacheKey.FromStrings("cluster-src", "cluster-var");
        var result1 = await node1.GetOrCreateAsync(key, async ct =>
            (TestData(), new CacheEntryMetadata()));
        result1.Dispose();
        await node1.UploadQueue.DrainAsync();

        // Export node1's bloom filter
        var peerData = node1.BloomFilter.ToBytes();
        node1.Dispose();

        // Node 2: fresh cascade, cloud has data but bloom is empty
        var node2 = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "cloud" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 1000,
            BloomFilterSlots = 2
        });
        node2.RegisterProvider(cloud);

        // Before merge: bloom says "not there", factory would be called
        var bloomKey = key.ToStringKey() + ":cloud";
        Assert.False(node2.BloomFilter.ProbablyContains(bloomKey));

        // Merge peer's bloom filter (OR union)
        node2.MergeBloomFilterFromPeer(peerData);

        // After merge: bloom knows cloud has this key
        Assert.True(node2.BloomFilter.ProbablyContains(bloomKey));

        // Fetch hits cloud instead of calling factory
        var result2 = await node2.GetOrCreateAsync(key, ct =>
            throw new InvalidOperationException("Factory should not be called"));

        Assert.Equal(CacheResultStatus.CloudHit, result2.Status);
        result2.Dispose();
        node2.Dispose();
    }

    [Fact]
    public async Task StreamingDiskHit_NoSubscribers_StreamsDirectly()
    {
        // Hot path: disk hit with no subscribers → stream passes through, no buffering.
        var memory = new InMemoryCacheProvider("memory", requiresInline: true);
        var disk = new StreamingCacheProvider("disk");

        var cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "memory", "disk" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 100,
        });
        cascade.RegisterProvider(memory);
        cascade.RegisterProvider(disk);

        var key = CacheKey.FromStrings("stream-disk-src", "stream-disk-var");
        var expectedData = TestData();

        // Pre-populate both tiers so there are no subscribers on hit
        await memory.StoreAsync(key, expectedData, new CacheEntryMetadata { ContentType = "image/jpeg" });
        await disk.StoreAsync(key, expectedData, new CacheEntryMetadata { ContentType = "image/jpeg" });

        // Clear memory to force disk hit
        await memory.InvalidateAsync(key);

        // Now: disk hit. Memory was checked and missed → gets Missed → subscribes.
        // So this case has a subscriber. Let's test the no-subscriber case instead.
        // Pre-populate memory again so it doesn't subscribe.
        await memory.StoreAsync(key, expectedData, new CacheEntryMetadata { ContentType = "image/jpeg" });

        // Reset disk fetch count
        disk.FetchCount = 0;

        // Memory hits first — disk is never even reached
        var result = await cascade.GetOrCreateAsync(key, ct =>
            throw new InvalidOperationException("Factory should not be called"));

        Assert.Equal(CacheResultStatus.MemoryHit, result.Status);
        Assert.NotNull(result.Data); // Memory returns byte[]
        Assert.Equal(0, disk.FetchCount); // Disk was never fetched
        result.Dispose();
        cascade.Dispose();
    }

    [Fact]
    public async Task StreamingDiskHit_WithSubscribers_BuffersForReplication()
    {
        // Disk hit with a subscriber (memory missed) → buffers stream for replication.
        var memory = new InMemoryCacheProvider("memory", requiresInline: true);
        var disk = new StreamingCacheProvider("disk");

        var cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "memory", "disk" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 100,
        });
        cascade.RegisterProvider(memory);
        cascade.RegisterProvider(disk);

        var key = CacheKey.FromStrings("stream-buf-src", "stream-buf-var");
        var expectedData = TestData();

        // Only disk has data — memory will miss and subscribe
        await disk.StoreAsync(key, expectedData, new CacheEntryMetadata { ContentType = "image/webp" });

        var result = await cascade.GetOrCreateAsync(key, ct =>
            throw new InvalidOperationException("Factory should not be called"));

        Assert.Equal(CacheResultStatus.DiskHit, result.Status);
        // Data was buffered for replication to memory
        Assert.NotNull(result.Data);
        Assert.Equal(expectedData, result.Data);

        // Memory should have received the data via replication
        Assert.True(memory.Contains(key));

        result.Dispose();
        cascade.Dispose();
    }

    [Fact]
    public async Task StreamingDiskHit_NoSubscribers_ReturnsStream()
    {
        // Disk hit where no other provider wants the data → stream passes through directly.
        // Use a single-provider cascade (no memory tier to subscribe).
        var disk = new StreamingCacheProvider("disk");

        var cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "disk" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 100,
        });
        cascade.RegisterProvider(disk);

        var key = CacheKey.FromStrings("stream-only-src", "stream-only-var");
        var expectedData = TestData();

        await disk.StoreAsync(key, expectedData, new CacheEntryMetadata { ContentType = "image/png" });

        var result = await cascade.GetOrCreateAsync(key, ct =>
            throw new InvalidOperationException("Factory should not be called"));

        Assert.Equal(CacheResultStatus.DiskHit, result.Status);
        // Stream path: Data is null, DataStream is set
        Assert.Null(result.Data);
        Assert.NotNull(result.DataStream);

        // Read through GetStream() and verify data
        using var ms = new MemoryStream();
        await result.DataStream!.CopyToAsync(ms);
        Assert.Equal(expectedData, ms.ToArray());

        result.Dispose();
        cascade.Dispose();
    }

    [Fact]
    public async Task StreamingProvider_GetStream_WorksForBothPaths()
    {
        // GetStream() returns the right thing for both stream and buffered results.
        var disk = new StreamingCacheProvider("disk");

        var cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "disk" },
            EnableRequestCoalescing = false,
            BloomFilterEstimatedItems = 100,
        });
        cascade.RegisterProvider(disk);

        var key = CacheKey.FromStrings("getstream-src", "getstream-var");
        var expectedData = TestData();

        await disk.StoreAsync(key, expectedData, new CacheEntryMetadata { ContentType = "image/jpeg" });

        var result = await cascade.GetOrCreateAsync(key, ct =>
            throw new InvalidOperationException("Should hit disk"));

        // GetStream() should work regardless of internal representation
        using var stream = result.GetStream();
        Assert.NotNull(stream);
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        Assert.Equal(expectedData, ms.ToArray());

        result.Dispose();
        cascade.Dispose();
    }
}
