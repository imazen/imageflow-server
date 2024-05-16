using Imazen.Common.Persistence;

namespace Imazen.Common.Tests.Licensing;

internal class StringCacheEmpty : IPersistentStringCache
{
    public string? Get(string key) => null;

    public DateTime? GetWriteTimeUtc(string key) => null;

    public StringCachePutResult TryPut(string key, string value) => StringCachePutResult.WriteFailed;
}