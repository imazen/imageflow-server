using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Imazen.Caching;

/// <summary>
/// Prevents thundering herd by ensuring that concurrent requests for the same key
/// share a single factory invocation. The first request runs the factory; subsequent
/// requests wait for its result.
///
/// Uses ConcurrentDictionary of SemaphoreSlim with proper cleanup.
/// </summary>
public sealed class RequestCoalescer : IDisposable
{
    private readonly ConcurrentDictionary<string, CoalescingEntry> _entries = new();
    private volatile bool _disposed;

    private sealed class CoalescingEntry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int WaiterCount;
    }

    /// <summary>
    /// Execute the factory under a per-key lock. If another caller is already running
    /// the factory for this key, waits up to timeoutMs for it to complete, then
    /// executes the factory again (in case the first caller's result was consumed).
    ///
    /// Returns default(T) if the timeout expires.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="key">Coalescing key.</param>
    /// <param name="timeoutMs">Max wait time in ms. Use -1 for infinite.</param>
    /// <param name="factory">The work to perform.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The factory result, or default(T) on timeout.</returns>
    public async ValueTask<(bool Success, T? Result)> TryExecuteAsync<T>(
        string key,
        int timeoutMs,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RequestCoalescer));

        var entry = _entries.GetOrAdd(key, _ => new CoalescingEntry());
        Interlocked.Increment(ref entry.WaiterCount);

        try
        {
            bool acquired;
            try
            {
                acquired = await entry.Semaphore.WaitAsync(timeoutMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return (false, default);
            }

            if (!acquired)
            {
                return (false, default);
            }

            try
            {
                var result = await factory(ct).ConfigureAwait(false);
                return (true, result);
            }
            finally
            {
                entry.Semaphore.Release();
            }
        }
        finally
        {
            if (Interlocked.Decrement(ref entry.WaiterCount) == 0)
            {
                // Last waiter cleans up. Race-safe: if another thread just incremented,
                // TryRemove will fail because GetOrAdd already replaced the entry.
                _entries.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Returns the number of active coalescing entries (keys with in-flight work or waiters).
    /// </summary>
    public int ActiveEntryCount => _entries.Count;

    public void Dispose()
    {
        _disposed = true;
        foreach (var entry in _entries.Values)
        {
            entry.Semaphore.Dispose();
        }
        _entries.Clear();
    }
}
