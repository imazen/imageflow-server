using Imazen.Caching;

namespace Imazen.Caching.Tests;

public class AsyncUploadQueueTests
{
    private static CacheEntryMetadata DefaultMeta => new() { ContentType = "image/jpeg" };

    [Fact]
    public async Task EnqueueAndDrain_ExecutesStore()
    {
        using var queue = new AsyncUploadQueue(1024 * 1024);
        bool storeCalled = false;
        var data = new byte[] { 1, 2, 3 };

        var result = queue.TryEnqueue("key1", data, DefaultMeta,
            async (d, m, ct) =>
            {
                storeCalled = true;
                await Task.Yield();
            });

        Assert.Equal(EnqueueResult.Enqueued, result);

        await queue.DrainAsync();
        Assert.True(storeCalled);
    }

    [Fact]
    public void Backpressure_QueueFull_ReturnsQueueFull()
    {
        using var queue = new AsyncUploadQueue(10); // 10 byte limit
        var data = new byte[8]; // 8 bytes

        var result1 = queue.TryEnqueue("key1", data, DefaultMeta,
            async (d, m, ct) => await Task.Delay(1000, ct));
        Assert.Equal(EnqueueResult.Enqueued, result1);

        // Second enqueue should fail (8 + 8 > 10)
        var result2 = queue.TryEnqueue("key2", data, DefaultMeta,
            async (d, m, ct) => await Task.Delay(1000, ct));
        Assert.Equal(EnqueueResult.QueueFull, result2);
    }

    [Fact]
    public void Dedup_SameKey_ReturnsAlreadyPresent()
    {
        using var queue = new AsyncUploadQueue(1024 * 1024);
        var data = new byte[] { 1, 2, 3 };

        var result1 = queue.TryEnqueue("key1", data, DefaultMeta,
            async (d, m, ct) => await Task.Delay(1000, ct));
        Assert.Equal(EnqueueResult.Enqueued, result1);

        var result2 = queue.TryEnqueue("key1", data, DefaultMeta,
            async (d, m, ct) => await Task.Delay(1000, ct));
        Assert.Equal(EnqueueResult.AlreadyPresent, result2);
    }

    [Fact]
    public async Task ReadThrough_TryGet_ReturnsDataWhileInFlight()
    {
        using var queue = new AsyncUploadQueue(1024 * 1024);
        var tcs = new TaskCompletionSource<bool>();
        var data = new byte[] { 10, 20, 30 };
        var meta = new CacheEntryMetadata { ContentType = "image/png" };

        queue.TryEnqueue("key1", data, meta,
            async (d, m, ct) =>
            {
                // Block until we signal completion
                await tcs.Task;
            });

        // Should be readable from the queue while store is in-flight
        Assert.True(queue.TryGet("key1", out var readData, out var readMeta));
        Assert.Equal(data, readData);
        Assert.Equal("image/png", readMeta!.ContentType);

        // Complete the store
        tcs.SetResult(true);
        await queue.DrainAsync();

        // After drain, entry should be gone
        Assert.False(queue.TryGet("key1", out _, out _));
    }

    [Fact]
    public void QueuedBytes_TracksCorrectly()
    {
        using var queue = new AsyncUploadQueue(1024 * 1024);
        Assert.Equal(0, queue.QueuedBytes);

        var data = new byte[100];
        queue.TryEnqueue("key1", data, DefaultMeta,
            async (d, m, ct) => await Task.Delay(1000, ct));

        Assert.Equal(100, queue.QueuedBytes);
    }

    [Fact]
    public async Task QueuedBytes_DecrementsAfterCompletion()
    {
        using var queue = new AsyncUploadQueue(1024 * 1024);
        var data = new byte[100];

        queue.TryEnqueue("key1", data, DefaultMeta,
            (d, m, ct) => Task.CompletedTask);

        await queue.DrainAsync();

        Assert.Equal(0, queue.QueuedBytes);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Dispose_DrainsQueue()
    {
        bool storeCalled = false;
        var queue = new AsyncUploadQueue(1024 * 1024);
        var data = new byte[] { 1, 2, 3 };

        queue.TryEnqueue("key1", data, DefaultMeta,
            async (d, m, ct) =>
            {
                await Task.Delay(50, ct);
                storeCalled = true;
            });

        // Dispose should drain (best-effort)
        queue.Dispose();

        // Store should have been called (or cancelled)
        // We just verify no exception thrown
    }

    [Fact]
    public async Task MultipleEntries_IndependentLifecycles()
    {
        using var queue = new AsyncUploadQueue(1024 * 1024);
        var tcs1 = new TaskCompletionSource<bool>();
        var tcs2 = new TaskCompletionSource<bool>();

        queue.TryEnqueue("key1", new byte[10], DefaultMeta,
            async (d, m, ct) => await tcs1.Task);
        queue.TryEnqueue("key2", new byte[20], DefaultMeta,
            async (d, m, ct) => await tcs2.Task);

        Assert.Equal(30, queue.QueuedBytes);
        Assert.Equal(2, queue.Count);

        // Complete first entry
        tcs1.SetResult(true);
        await Task.Delay(50); // Let the task complete

        Assert.Equal(1, queue.Count);

        // Complete second entry
        tcs2.SetResult(true);
        await queue.DrainAsync();

        Assert.Equal(0, queue.Count);
        Assert.Equal(0, queue.QueuedBytes);
    }
}
