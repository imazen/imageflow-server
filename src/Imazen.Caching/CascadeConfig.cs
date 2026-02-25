using System;
using System.Collections.Generic;

namespace Imazen.Caching;

/// <summary>
/// Configuration for a CacheCascade instance.
/// </summary>
public sealed class CascadeConfig
{
    /// <summary>
    /// Flat ordered list of provider names, fast to slow (e.g., ["memory", "disk", "s3-cache"]).
    /// </summary>
    public required List<string> Providers { get; init; }

    /// <summary>
    /// Enable request coalescing (thundering herd prevention).
    /// When true, concurrent requests for the same cache key share a single factory invocation.
    /// </summary>
    public bool EnableRequestCoalescing { get; init; } = true;

    /// <summary>
    /// Maximum time in milliseconds to wait for another request's factory to complete
    /// before timing out. On timeout, returns a Timeout result (503).
    /// </summary>
    public int CoalescingTimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Maximum bytes of data queued for async store operations (disk/cloud).
    /// When exceeded, new store operations are dropped (backpressure).
    /// </summary>
    public long MaxUploadQueueBytes { get; init; } = 150L * 1024 * 1024; // 150MB

    /// <summary>
    /// Estimated number of items the bloom filter should handle.
    /// Size the bloom filter for millions of cached files.
    /// </summary>
    public int BloomFilterEstimatedItems { get; init; } = 10_000_000;

    /// <summary>
    /// Target false positive rate for the bloom filter.
    /// </summary>
    public double BloomFilterFalsePositiveRate { get; init; } = 0.01;

    /// <summary>
    /// Number of bloom filter slots to rotate through. Higher values = more memory
    /// but less data loss on rotation.
    /// </summary>
    public int BloomFilterSlots { get; init; } = 4;

    /// <summary>
    /// Events callback for monitoring/diagnostics. Called synchronously.
    /// </summary>
    public Action<CacheEvent>? OnCacheEvent { get; init; }
}
