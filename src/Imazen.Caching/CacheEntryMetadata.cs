using System;

namespace Imazen.Caching;

/// <summary>
/// Metadata stored alongside a cached blob.
/// </summary>
public sealed class CacheEntryMetadata
{
    public string? ContentType { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static CacheEntryMetadata Default => new();
}
