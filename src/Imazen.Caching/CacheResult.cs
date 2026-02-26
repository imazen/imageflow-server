using System;
using System.IO;

namespace Imazen.Caching;

public enum CacheResultStatus
{
    MemoryHit,
    DiskHit,
    CloudHit,
    /// <summary>
    /// Served from upload queue (in-flight write).
    /// </summary>
    QueueHit,
    /// <summary>
    /// Cache miss, factory produced the result.
    /// </summary>
    Created,
    /// <summary>
    /// Coalescing timeout — caller should return 503.
    /// </summary>
    Timeout,
    /// <summary>
    /// Factory or cache failure.
    /// </summary>
    Error
}

/// <summary>
/// The result of a cache lookup or creation. Caller must dispose.
///
/// May contain either a Stream (for direct streaming) or a byte[] (when data
/// was buffered for distribution to cache subscribers). Both are exposed;
/// callers should prefer Data when present (zero-copy from buffer),
/// falling back to DataStream for the streaming path.
/// </summary>
public sealed class CacheResult : IDisposable
{
    public CacheResultStatus Status { get; init; }

    /// <summary>
    /// Buffered data. Non-null when the cascade buffered for subscribers or the data
    /// was already in memory (memory hit, queue hit, factory result).
    /// Prefer this over DataStream when available — avoids a MemoryStream wrapper.
    /// </summary>
    public byte[]? Data { get; init; }

    /// <summary>
    /// Stream-based data. Non-null on the pure streaming path (cache hit, no subscribers).
    /// Caller must dispose when done.
    /// </summary>
    public Stream? DataStream { get; init; }

    public string? ContentType { get; init; }
    public string? ProviderName { get; init; }
    public TimeSpan? Latency { get; init; }
    public string? ErrorDetail { get; init; }

    public bool IsHit => Status is CacheResultStatus.MemoryHit
        or CacheResultStatus.DiskHit
        or CacheResultStatus.CloudHit
        or CacheResultStatus.QueueHit;

    /// <summary>
    /// Returns a Stream for the data, regardless of whether this is a buffered or streaming result.
    /// </summary>
    public Stream? GetStream()
    {
        if (DataStream != null) return DataStream;
        if (Data != null) return new MemoryStream(Data, writable: false);
        return null;
    }

    public void Dispose()
    {
        DataStream?.Dispose();
    }

    // --- Factory methods for the cascade ---

    /// <summary>
    /// Path A: Stream-through hit. No buffering, no subscribers wanted the data.
    /// The caller streams from DataStream and disposes it.
    /// </summary>
    internal static CacheResult StreamHit(CacheResultStatus status, byte[] data,
        string? contentType, string providerName, TimeSpan? latency = null)
    {
        // For now, providers return byte[]. When providers return Stream in the future,
        // this factory will set DataStream instead. The cascade decides the path.
        return new CacheResult
        {
            Status = status,
            Data = data,
            ContentType = contentType,
            ProviderName = providerName,
            Latency = latency
        };
    }

    /// <summary>
    /// Path B: Buffered hit. Data was buffered because subscribers wanted it.
    /// The cascade has already distributed copies to subscribers.
    /// </summary>
    internal static CacheResult BufferedHit(CacheResultStatus status, byte[] data,
        string? contentType, string providerName, TimeSpan? latency = null)
    {
        return new CacheResult
        {
            Status = status,
            Data = data,
            ContentType = contentType,
            ProviderName = providerName,
            Latency = latency
        };
    }

    /// <summary>
    /// Factory-created result. Data is always buffered (factory returns byte[]).
    /// </summary>
    internal static CacheResult Created(byte[] data, string? contentType, TimeSpan? latency = null)
    {
        return new CacheResult
        {
            Status = CacheResultStatus.Created,
            Data = data,
            ContentType = contentType,
            Latency = latency
        };
    }

    public static CacheResult TimeoutResult()
    {
        return new CacheResult
        {
            Status = CacheResultStatus.Timeout
        };
    }

    public static CacheResult ErrorResult(string detail)
    {
        return new CacheResult
        {
            Status = CacheResultStatus.Error,
            ErrorDetail = detail
        };
    }
}
