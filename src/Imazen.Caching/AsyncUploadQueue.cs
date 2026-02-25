using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Imazen.Caching;

public enum EnqueueResult
{
    Enqueued,
    QueueFull,
    AlreadyPresent
}

/// <summary>
/// A bounded, byte-counted async upload queue with deduplication and read-through.
///
/// Entries can be served from the queue while their upload is in-flight (read-through),
/// acting as a short-lived memory cache for pending writes.
///
/// Backpressure: when MaxQueueBytes is exceeded, TryEnqueue returns QueueFull
/// and the caller drops the write (the data was already served to the client).
/// </summary>
public sealed class AsyncUploadQueue : IDisposable
{
    private readonly long _maxQueueBytes;
    private readonly ConcurrentDictionary<string, UploadEntry> _entries = new();
    private long _queuedBytes;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _disposed;

    private sealed class UploadEntry
    {
        public readonly byte[] Data;
        public readonly CacheEntryMetadata Metadata;
        public readonly long Size;
        public Task? Task;

        public UploadEntry(byte[] data, CacheEntryMetadata metadata)
        {
            Data = data;
            Metadata = metadata;
            Size = data.Length;
        }
    }

    public AsyncUploadQueue(long maxQueueBytes)
    {
        if (maxQueueBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxQueueBytes));
        _maxQueueBytes = maxQueueBytes;
    }

    /// <summary>
    /// Enqueue a store operation. The storeFunc runs in the background.
    /// Returns Enqueued, QueueFull, or AlreadyPresent.
    /// </summary>
    /// <param name="key">Dedup key (e.g., CacheKey.ToStringKey() + ":" + providerName).</param>
    /// <param name="data">The data to store. The queue takes ownership of this array.</param>
    /// <param name="metadata">Cache entry metadata.</param>
    /// <param name="storeFunc">The async store operation to run in the background.</param>
    /// <returns>Enqueue result.</returns>
    public EnqueueResult TryEnqueue(string key, byte[] data, CacheEntryMetadata metadata,
        Func<byte[], CacheEntryMetadata, CancellationToken, Task> storeFunc)
    {
        if (_disposed) return EnqueueResult.QueueFull;

        var entry = new UploadEntry(data, metadata);

        // Check capacity before adding
        var newTotal = Interlocked.Add(ref _queuedBytes, entry.Size);
        if (newTotal > _maxQueueBytes)
        {
            Interlocked.Add(ref _queuedBytes, -entry.Size);
            return EnqueueResult.QueueFull;
        }

        if (!_entries.TryAdd(key, entry))
        {
            Interlocked.Add(ref _queuedBytes, -entry.Size);
            return EnqueueResult.AlreadyPresent;
        }

        // Start background task
        entry.Task = Task.Run(async () =>
        {
            try
            {
                if (!_cts.IsCancellationRequested)
                {
                    await storeFunc(entry.Data, entry.Metadata, _cts.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                if (_entries.TryRemove(key, out _))
                {
                    Interlocked.Add(ref _queuedBytes, -entry.Size);
                }
            }
        });

        return EnqueueResult.Enqueued;
    }

    /// <summary>
    /// Try to read data from the queue (in-flight read-through).
    /// Returns true if the key is in the queue and the data is available.
    /// </summary>
    public bool TryGet(string key,
#if NET8_0_OR_GREATER
        [NotNullWhen(true)]
#endif
        out byte[]? data,
#if NET8_0_OR_GREATER
        [NotNullWhen(true)]
#endif
        out CacheEntryMetadata? metadata)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            data = entry.Data;
            metadata = entry.Metadata;
            return true;
        }

        data = null;
        metadata = null;
        return false;
    }

    /// <summary>
    /// Current number of bytes queued for upload.
    /// </summary>
    public long QueuedBytes => Interlocked.Read(ref _queuedBytes);

    /// <summary>
    /// Current number of entries in the queue.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Await all currently in-flight uploads. New items can still be added while waiting.
    /// </summary>
    public async Task DrainAsync()
    {
        var tasks = _entries.Values
            .Select(e => e.Task)
            .Where(t => t != null)
            .Cast<Task>()
            .ToArray();
        if (tasks.Length > 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();

        // Best-effort drain: don't wait forever
        try
        {
            DrainAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Swallow exceptions during dispose
        }

        _cts.Dispose();
    }
}
