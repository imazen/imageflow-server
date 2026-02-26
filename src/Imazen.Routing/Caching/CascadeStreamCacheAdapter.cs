using Imazen.Caching;
using Imazen.Common.Extensibility.StreamCache;
using Imazen.Common.Issues;
using Microsoft.Extensions.Hosting;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Imazen.Routing.Caching;

/// <summary>
/// Adapts ICacheEngine (CacheCascade) to the legacy IStreamCache interface.
/// Allows existing code that depends on IStreamCache to use the new caching system.
/// </summary>
public class CascadeStreamCacheAdapter : IStreamCache
{
    private readonly ICacheEngine _engine;

    public CascadeStreamCacheAdapter(ICacheEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public async Task<IStreamCacheResult> GetOrCreateBytes(
        byte[] key,
        AsyncBytesResult dataProviderCallback,
        CancellationToken cancellationToken,
        bool retrieveContentType)
    {
        var cacheKey = CacheKey.FromRaw32(key);

        var result = await _engine.GetOrCreateAsync(cacheKey, async ct =>
        {
            var cacheInput = await dataProviderCallback(ct);
            if (cacheInput?.Bytes.Array == null)
                throw new InvalidOperationException("Null bytes from dataProviderCallback");

            var data = new byte[cacheInput.Bytes.Count];
            Buffer.BlockCopy(cacheInput.Bytes.Array, cacheInput.Bytes.Offset, data, 0, cacheInput.Bytes.Count);

            var metadata = new CacheEntryMetadata
            {
                ContentType = cacheInput.ContentType,
                ContentLength = data.Length
            };
            return (data, metadata);
        }, cancellationToken);

        var stream = result.GetStream() ?? Stream.Null;
        var contentType = retrieveContentType ? result.ContentType : null;
        var status = result.Status.ToString();

        return new SimpleStreamCacheResult(stream, contentType, status);
    }

    public IEnumerable<IIssue> GetIssues() => [];

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class SimpleStreamCacheResult : IStreamCacheResult, IDisposable
{
    public SimpleStreamCacheResult(Stream data, string? contentType, string status)
    {
        Data = data;
        ContentType = contentType;
        Status = status;
    }

    public Stream Data { get; }
    public string? ContentType { get; }
    public string Status { get; }

    public void Dispose()
    {
        Data.Dispose();
    }
}
