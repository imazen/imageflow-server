using System;

namespace Imazen.Caching;

public enum CacheEventKind
{
    Hit,
    Miss,
    Store,
    StoreDropped,
    Error
}

public readonly struct CacheEvent
{
    public CacheEventKind Kind { get; }
    public CacheKey Key { get; }
    public string ProviderName { get; }
    public TimeSpan? Latency { get; }
    public string? Detail { get; }

    public CacheEvent(CacheEventKind kind, CacheKey key, string providerName, TimeSpan? latency = null, string? detail = null)
    {
        Kind = kind;
        Key = key;
        ProviderName = providerName;
        Latency = latency;
        Detail = detail;
    }
}
