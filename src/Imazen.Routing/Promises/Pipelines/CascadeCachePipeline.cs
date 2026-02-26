using System.Buffers;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Resulting;
using Imazen.Caching;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Promises;
using Imazen.Routing.Requests;

namespace Imazen.Routing.Promises.Pipelines;

/// <summary>
/// An IBlobPromisePipeline that uses ICacheEngine (CacheCascade) for caching.
/// Drop-in replacement for CacheEngine in the pipeline when using the new caching system.
///
/// Pipeline composition is the same as with CacheEngine:
///   CascadeCachePipeline(null, engine)           // output cache
///   ImagingMiddleware(^, imagingOptions)          // image processing
///   CascadeCachePipeline(^, engine)              // source cache
/// </summary>
public class CascadeCachePipeline : IBlobPromisePipeline
{
    private readonly IBlobPromisePipeline? _next;
    private readonly ICacheEngine _cacheEngine;

    public CascadeCachePipeline(IBlobPromisePipeline? next, ICacheEngine cacheEngine)
    {
        _next = next;
        _cacheEngine = cacheEngine ?? throw new ArgumentNullException(nameof(cacheEngine));
    }

    public async ValueTask<CodeResult<ICacheableBlobPromise>> GetFinalPromiseAsync(
        ICacheableBlobPromise promise,
        IBlobRequestRouter router,
        IBlobPromisePipeline promisePipeline,
        IHttpRequestStreamAdapter outerRequest,
        CancellationToken cancellationToken = default)
    {
        var wrappedPromise = promise;
        if (_next != null)
        {
            var result = await _next.GetFinalPromiseAsync(promise, router, promisePipeline, outerRequest, cancellationToken);
            if (result.IsError) return result;
            wrappedPromise = result.Unwrap();
        }

        return CodeResult<ICacheableBlobPromise>.Ok(
            new CascadeCachePromise(wrappedPromise.FinalRequest, wrappedPromise, _cacheEngine));
    }
}

/// <summary>
/// Promise that delegates to ICacheEngine for cache-or-create semantics.
/// Converts between the promise pipeline's IBlobWrapper world and ICacheEngine's byte[]/Stream world.
/// </summary>
internal record CascadeCachePromise(
    IRequestSnapshot FinalRequest,
    ICacheableBlobPromise FreshPromise,
    ICacheEngine CacheEngine) : ICacheableBlobPromise
{
    public bool IsCacheSupporting => true;
    public bool HasDependencies => FreshPromise.HasDependencies;
    public bool ReadyToWriteCacheKeyBasisData => FreshPromise.ReadyToWriteCacheKeyBasisData;
    public bool SupportsPreSignedUrls => FreshPromise.SupportsPreSignedUrls;

    public LatencyTrackingZone? LatencyZone => null;

    private byte[]? _cacheKey32Bytes;
    public byte[] GetCacheKey32Bytes()
    {
        return _cacheKey32Bytes ??= this.GetCacheKey32BytesUncached();
    }

    public ValueTask<CodeResult> RouteDependenciesAsync(IBlobRequestRouter router, CancellationToken cancellationToken = default)
    {
        return FreshPromise.RouteDependenciesAsync(router, cancellationToken);
    }

    public void WriteCacheKeyBasisPairsToRecursive(IBufferWriter<byte> writer)
    {
        FreshPromise.WriteCacheKeyBasisPairsToRecursive(writer);
    }

    public async ValueTask<IAdaptableHttpResponse> CreateResponseAsync(
        IRequestSnapshot request, IBlobRequestRouter router,
        IBlobPromisePipeline pipeline, CancellationToken cancellationToken = default)
    {
        return new BlobResponse(await TryGetBlobAsync(request, router, pipeline, cancellationToken));
    }

    public async ValueTask<CodeResult<IBlobWrapper>> TryGetBlobAsync(
        IRequestSnapshot request, IBlobRequestRouter router,
        IBlobPromisePipeline pipeline, CancellationToken cancellationToken = default)
    {
        // Resolve dependencies first (watermarks, etc.)
        if (!FreshPromise.ReadyToWriteCacheKeyBasisData)
        {
            var res = await FreshPromise.RouteDependenciesAsync(router, cancellationToken);
            if (res.TryUnwrapError(out var err))
            {
                if (err.StatusCode == HttpStatus.NotFound)
                    return CodeResult<IBlobWrapper>.Err(err);
                throw new InvalidOperationException("Promise has dependencies but could not be routed: " + err);
            }
        }

        var cacheKey = CacheKey.FromRaw32(GetCacheKey32Bytes());

        var result = await CacheEngine.GetOrCreateAsync(cacheKey, async ct =>
        {
            // Factory: execute the fresh promise (image processing or source fetch)
            var freshResult = await FreshPromise.TryGetBlobAsync(
                FreshPromise.FinalRequest, router, pipeline, ct);
            if (freshResult.IsError) return null;

            var blob = freshResult.Unwrap();
            try
            {
                // Buffer blob to bytes for the cache engine
                await blob.EnsureReusable(ct);
                using var consumable = await blob.GetConsumablePromise().IntoConsumableBlob();
                using var stream = consumable.BorrowStream(DisposalPromise.CallerDisposesStreamThenBlob);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, 81920, ct);
                var data = ms.ToArray();

                var metadata = new CacheEntryMetadata
                {
                    ContentType = blob.Attributes.ContentType,
                    ContentLength = data.Length
                };
                return (data, metadata);
            }
            finally
            {
                blob.Dispose();
            }
        }, cancellationToken);

        // Convert CacheResult to IBlobWrapper
        return CacheResultToBlobWrapper(result);
    }

    private static CodeResult<IBlobWrapper> CacheResultToBlobWrapper(CacheResult result)
    {
        if (result.Status == CacheResultStatus.Timeout)
            return CodeResult<IBlobWrapper>.Err(HttpStatus.ServiceUnavailable.WithAppend("Cache coalescing timeout"));
        if (result.Status == CacheResultStatus.Error)
            return CodeResult<IBlobWrapper>.Err(HttpStatus.ServerError.WithAppend(result.ErrorDetail ?? "Cache error"));

        if (result.Data != null)
        {
            var attrs = new BlobAttributes { ContentType = result.ContentType };
            var memoryBlob = new MemoryBlob(result.Data, attrs, result.Latency ?? TimeSpan.Zero);
            return CodeResult<IBlobWrapper>.Ok(new BlobWrapper(null, memoryBlob));
        }

        if (result.DataStream != null)
        {
            var attrs = new BlobAttributes { ContentType = result.ContentType };
            // Transfer stream ownership — StreamBlob owns the stream, CacheResult must NOT dispose it.
            var streamBlob = new StreamBlob(attrs, result.DataStream);
            return CodeResult<IBlobWrapper>.Ok(new BlobWrapper(null, streamBlob));
        }

        return CodeResult<IBlobWrapper>.Err(HttpStatus.ServerError.WithAppend("Cache returned no data"));
    }
}
