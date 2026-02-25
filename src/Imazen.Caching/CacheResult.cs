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
/// </summary>
public sealed class CacheResult : IDisposable
{
    public CacheResultStatus Status { get; init; }
    public Stream? Data { get; init; }
    public string? ContentType { get; init; }
    public string? ProviderName { get; init; }
    public TimeSpan? Latency { get; init; }
    public string? ErrorDetail { get; init; }

    public bool IsHit => Status is CacheResultStatus.MemoryHit
        or CacheResultStatus.DiskHit
        or CacheResultStatus.CloudHit
        or CacheResultStatus.QueueHit;

    public void Dispose()
    {
        Data?.Dispose();
    }

    public static CacheResult Hit(CacheResultStatus status, byte[] data, string? contentType, string providerName, TimeSpan? latency = null)
    {
        return new CacheResult
        {
            Status = status,
            Data = new MemoryStream(data, writable: false),
            ContentType = contentType,
            ProviderName = providerName,
            Latency = latency
        };
    }

    public static CacheResult Created(byte[] data, string? contentType, TimeSpan? latency = null)
    {
        return new CacheResult
        {
            Status = CacheResultStatus.Created,
            Data = new MemoryStream(data, writable: false),
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
