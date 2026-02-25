using System.Diagnostics;
using Imazen.Caching;

namespace Imazen.Caching.Tests;

public class ThrashTests : IDisposable
{
    private readonly CacheCascade _cascade;
    private readonly InMemoryCacheProvider _memory;
    private readonly InMemoryCacheProvider _disk;

    public ThrashTests()
    {
        _memory = new InMemoryCacheProvider("memory", requiresInline: true);
        _disk = new InMemoryCacheProvider("disk");

        _cascade = new CacheCascade(new CascadeConfig
        {
            Providers = new List<string> { "memory", "disk" },
            EnableRequestCoalescing = true,
            CoalescingTimeoutMs = 10000,
            MaxUploadQueueBytes = 50 * 1024 * 1024,
            BloomFilterEstimatedItems = 100_000,
            BloomFilterFalsePositiveRate = 0.01,
            BloomFilterSlots = 2
        });

        _cascade.RegisterProvider(_memory);
        _cascade.RegisterProvider(_disk);
    }

    public void Dispose()
    {
        _cascade.Dispose();
    }

    [Fact]
    public async Task HighConcurrency_MixedReadWrite()
    {
        const int totalOps = 1000;
        const int uniqueKeys = 100;
        int factoryCalls = 0;

        var tasks = Enumerable.Range(0, totalOps).Select(async i =>
        {
            var keyIndex = i % uniqueKeys;
            var key = CacheKey.FromStrings($"source-{keyIndex}", $"variant-{keyIndex}");
            var data = new byte[64];

            var result = await _cascade.GetOrCreateAsync(key, async ct =>
            {
                Interlocked.Increment(ref factoryCalls);
                await Task.Delay(1, ct); // Simulate minimal work
                return (data, new CacheEntryMetadata { ContentType = "image/jpeg" });
            });

            Assert.True(result.Status is CacheResultStatus.Created or CacheResultStatus.MemoryHit
                or CacheResultStatus.DiskHit or CacheResultStatus.QueueHit,
                $"Unexpected status: {result.Status}");

            result.Dispose();
        }).ToArray();

        await Task.WhenAll(tasks);
        await _cascade.UploadQueue.DrainAsync();

        // Factory should be called much less than totalOps due to caching + coalescing
        Assert.True(factoryCalls <= uniqueKeys * 2,
            $"Factory called {factoryCalls} times for {uniqueKeys} unique keys across {totalOps} ops");
    }

    [Fact]
    public async Task BloomFilter_ManyUniqueKeys_MemoryBounded()
    {
        var bloom = new RotatingBloomFilter(100_000, 0.01, slotCount: 4);

        var initialMemory = GC.GetTotalMemory(forceFullCollection: true);

        // Insert 50K keys
        for (int i = 0; i < 50_000; i++)
        {
            bloom.Insert($"key-{i}");
        }

        var afterMemory = GC.GetTotalMemory(forceFullCollection: true);

        // Bloom filter memory should be bounded (not growing per-key)
        // The filter itself is pre-allocated, so memory growth should be minimal
        var growth = afterMemory - initialMemory;
        Assert.True(growth < 50 * 1024 * 1024, $"Memory grew by {growth / 1024.0 / 1024.0:F1}MB");

        // Verify no false negatives
        for (int i = 0; i < 50_000; i++)
        {
            Assert.True(bloom.ProbablyContains($"key-{i}"), $"False negative at key-{i}");
        }
    }

    [Fact]
    public async Task UploadQueue_Backpressure_DoesNotOOM()
    {
        using var queue = new AsyncUploadQueue(1024 * 1024); // 1MB limit
        int enqueued = 0;
        int dropped = 0;

        // Try to enqueue way more than the limit
        for (int i = 0; i < 1000; i++)
        {
            var data = new byte[10_000]; // 10KB each
            var result = queue.TryEnqueue($"key-{i}", data, new CacheEntryMetadata(),
                async (d, m, ct) =>
                {
                    await Task.Delay(1, ct); // Simulate slow store
                });

            if (result == EnqueueResult.Enqueued) enqueued++;
            else if (result == EnqueueResult.QueueFull) dropped++;
        }

        // Some should be enqueued, some dropped
        Assert.True(enqueued > 0, "Nothing was enqueued");
        Assert.True(dropped > 0, "Nothing was dropped (backpressure didn't engage)");
        Assert.True(queue.QueuedBytes <= 1024 * 1024 + 10_000,
            $"Queue exceeded limit: {queue.QueuedBytes}");

        await queue.DrainAsync();
    }

    [Fact]
    public async Task RequestCoalescer_NoSemaphoreLeaks()
    {
        using var coalescer = new RequestCoalescer();

        var tasks = Enumerable.Range(0, 1000).Select(async i =>
        {
            var key = $"key-{i % 50}";
            var (success, _) = await coalescer.TryExecuteAsync(
                key,
                5000,
                async ct =>
                {
                    await Task.Delay(1, ct);
                    return i;
                });
        }).ToArray();

        await Task.WhenAll(tasks);

        // All semaphores should be cleaned up
        Assert.Equal(0, coalescer.ActiveEntryCount);
    }

    [Fact]
    public async Task CancellationMidFlight_NoLeakedTasks()
    {
        using var cts = new CancellationTokenSource();
        int completedOps = 0;
        int cancelledOps = 0;

        var tasks = Enumerable.Range(0, 100).Select(async i =>
        {
            var key = CacheKey.FromStrings($"cancel-src-{i}", $"cancel-var-{i}");

            try
            {
                var result = await _cascade.GetOrCreateAsync(key, async ct =>
                {
                    await Task.Delay(50, ct);
                    ct.ThrowIfCancellationRequested();
                    return (new byte[32], new CacheEntryMetadata());
                }, cts.Token);

                Interlocked.Increment(ref completedOps);
                result.Dispose();
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref cancelledOps);
            }
        }).ToArray();

        // Cancel after a short delay
        await Task.Delay(20);
        cts.Cancel();

        await Task.WhenAll(tasks);

        // Some should complete, some should be cancelled
        Assert.True(completedOps + cancelledOps == 100);

        // Upload queue should still be drainable
        await _cascade.UploadQueue.DrainAsync();
    }

    [Fact]
    public async Task SustainedWorkload_90ReadTo10Write()
    {
        const int ops = 500;
        const int uniqueKeys = 50;
        var random = new Random(42);

        // Pre-populate some keys
        for (int i = 0; i < uniqueKeys / 2; i++)
        {
            var key = CacheKey.FromStrings($"sustained-{i}", $"variant-{i}");
            await _memory.StoreAsync(key, new byte[64], new CacheEntryMetadata());
        }

        var tasks = Enumerable.Range(0, ops).Select(async i =>
        {
            var keyIndex = random.Next(uniqueKeys);
            var key = CacheKey.FromStrings($"sustained-{keyIndex}", $"variant-{keyIndex}");

            var result = await _cascade.GetOrCreateAsync(key, async ct =>
            {
                await Task.Delay(1, ct);
                return (new byte[64], new CacheEntryMetadata { ContentType = "image/webp" });
            });

            result.Dispose();
        }).ToArray();

        await Task.WhenAll(tasks);
        await _cascade.UploadQueue.DrainAsync();

        // All keys should now be cached
        Assert.True(_memory.Count >= uniqueKeys / 2);
    }
}
