using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Imazen.Routing.Engine;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Layers;
using Imazen.Routing.Requests;
using Microsoft.Extensions.Logging;

namespace Imazen.Routing.Promises.Pipelines;

public record BlobPipelineHarnessOptions(
    IBlobPromisePipeline Pipeline, 
    Func<IRequestSnapshot, CancellationToken, ValueTask<CodeResult<IBlobWrapper>>> BlobProvider, 
    IReLogger Logger, LatencyTrackingZone BlobOriginLatencyZone);

public class BlobPipelineHarness
{
    readonly IRoutingEngine router;
    IBlobPromisePipeline pipeline;
    IReLogger logger;

    public BlobPipelineHarness(RoutingEngine router, IBlobPromisePipeline pipeline, IReLogger logger)
    {
        this.router = router;
        this.pipeline = pipeline;
        this.logger = logger;
    }
    public BlobPipelineHarness(BlobPipelineHarnessOptions options)
    {
        var routingBuilder = new RoutingBuilder().AddEndpointLayer(
            new SimpleLayer("BlobEndpoint", req =>
            {
                var endpoint =
                    new PromiseWrappingEndpoint(
                        new CacheableBlobPromise(req, options.BlobOriginLatencyZone, options.BlobProvider));
                return CodeResult<IRoutingEndpoint>.Ok(endpoint);
            }, null));
        router = routingBuilder.Build(options.Logger);
        pipeline = options.Pipeline;
        logger = options.Logger;
        
    }

    public async ValueTask<CodeResult<IBlobWrapper>> RequestBlobWrapper(string path, string query = "",
        CancellationToken cancellationToken = default)
    {
        var request = new EmptyHttpRequest(path, query);
        var mutableRequest = MutableRequest.OriginalRequest(request);
        return await Request(mutableRequest, cancellationToken);
    }
    
    public async ValueTask<CodeResult<IBlobWrapper>> Request(MutableRequest mutableRequest, CancellationToken cancellationToken = default)
    {
        var result = await router.RouteToPromiseAsync(mutableRequest, cancellationToken);
        if (result == null)
        {
            return CodeResult<IBlobWrapper>.Err((404, "No route found"));
        }
        if (result.IsError)
        {
            return CodeResult<IBlobWrapper>.Err(result.Error);
        }
        
        var outerRequest = mutableRequest.OriginatingRequest ?? new EmptyHttpRequest(mutableRequest);
        
        var pipelineResult = await pipeline.GetFinalPromiseAsync(
            result.Unwrap(),router, pipeline, outerRequest,cancellationToken);
            
        var finalPromise = pipelineResult.Unwrap();
                
        if (finalPromise.HasDependencies)
        {
            var dependencyResult = await finalPromise.RouteDependenciesAsync(router, cancellationToken);
            if (dependencyResult.IsError)
            {
                return CodeResult<IBlobWrapper>.Err(dependencyResult.Error);
            }
        }
        var blobResult =
            await finalPromise.TryGetBlobAsync(mutableRequest, router, pipeline, cancellationToken);
        if (blobResult.IsError)
        {
            return CodeResult<IBlobWrapper>.Err(blobResult.Error);
        }
        return CodeResult<IBlobWrapper>.Ok(blobResult.Unwrap());
    }
    
}
