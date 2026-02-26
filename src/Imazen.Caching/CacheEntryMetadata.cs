using System;

namespace Imazen.Caching;

/// <summary>
/// Metadata stored alongside a cached blob.
/// </summary>
public sealed class CacheEntryMetadata
{
    public string? ContentType { get; init; }

    /// <summary>
    /// Content length in bytes. Required for stream-based providers so the cascade
    /// can make WantsToStore decisions without buffering. -1 if unknown.
    /// </summary>
    public long ContentLength { get; init; } = -1;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static CacheEntryMetadata Default => new();
}
