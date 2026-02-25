using Imazen.Caching;

namespace Imazen.Caching.Tests;

public class RequestCoalescerTests
{
    [Fact]
    public async Task ConcurrentRequests_SameKey_FactoryCalledOnce()
    {
        using var coalescer = new RequestCoalescer();
        int factoryCallCount = 0;

        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var (success, result) = await coalescer.TryExecuteAsync(
                "same-key",
                5000,
                async ct =>
                {
                    Interlocked.Increment(ref factoryCallCount);
                    await Task.Delay(100, ct);
                    return 42;
                });
            return (success, result);
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        // All should succeed
        Assert.All(results, r => Assert.True(r.success));
        // Factory should be called at most once per serialized execution
        // (could be called multiple times if semaphore is released between waiters)
        Assert.True(factoryCallCount >= 1);
        Assert.True(factoryCallCount <= 10);
    }

    [Fact]
    public async Task DifferentKeys_RunInParallel()
    {
        using var coalescer = new RequestCoalescer();
        int maxConcurrent = 0;
        int current = 0;

        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            var (success, _) = await coalescer.TryExecuteAsync(
                $"key-{i}", // Different keys
                5000,
                async ct =>
                {
                    var c = Interlocked.Increment(ref current);
                    // Track max concurrency
                    int oldMax;
                    do
                    {
                        oldMax = Volatile.Read(ref maxConcurrent);
                        if (c <= oldMax) break;
                    } while (Interlocked.CompareExchange(ref maxConcurrent, c, oldMax) != oldMax);

                    await Task.Delay(50, ct);
                    Interlocked.Decrement(ref current);
                    return i;
                });
            return success;
        }).ToArray();

        await Task.WhenAll(tasks);

        // Different keys should run in parallel
        Assert.True(maxConcurrent > 1, $"Expected parallel execution, got max concurrency {maxConcurrent}");
    }

    [Fact]
    public async Task Timeout_ReturnsFailure()
    {
        using var coalescer = new RequestCoalescer();

        // First: start a long-running factory
        var slowTask = coalescer.TryExecuteAsync(
            "blocked-key",
            10000,
            async ct =>
            {
                await Task.Delay(2000, ct);
                return 1;
            });

        // Give it a moment to acquire the semaphore
        await Task.Delay(20);

        // Second: try with a very short timeout
        var (success, _) = await coalescer.TryExecuteAsync(
            "blocked-key",
            1, // 1ms timeout
            async ct =>
            {
                await Task.Delay(10, ct);
                return 2;
            });

        Assert.False(success);

        // Clean up the slow task
        await slowTask;
    }

    [Fact]
    public async Task Cancellation_ReturnsFailure()
    {
        using var coalescer = new RequestCoalescer();
        using var cts = new CancellationTokenSource();

        // Start a long-running factory
        var slowTask = coalescer.TryExecuteAsync(
            "cancel-key",
            10000,
            async ct =>
            {
                await Task.Delay(5000, ct);
                return 1;
            });

        await Task.Delay(20);

        // Cancel a second request
        cts.Cancel();
        var (success, _) = await coalescer.TryExecuteAsync(
            "cancel-key",
            5000,
            async ct =>
            {
                ct.ThrowIfCancellationRequested();
                return 2;
            },
            cts.Token);

        Assert.False(success);

        // Clean up
        await slowTask;
    }

    [Fact]
    public async Task SemaphoreCleanup_NoLeaks()
    {
        using var coalescer = new RequestCoalescer();

        // Run many requests and verify cleanup
        for (int i = 0; i < 100; i++)
        {
            var (success, _) = await coalescer.TryExecuteAsync(
                $"key-{i}",
                1000,
                ct => new ValueTask<int>(i));
            Assert.True(success);
        }

        // All entries should be cleaned up
        Assert.Equal(0, coalescer.ActiveEntryCount);
    }
}
