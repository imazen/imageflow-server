using System.Text;
using Imageflow.Server;
using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Blobs.LegacyProviders;

using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Concurrency.BoundedTaskCollection;
using Imazen.Common.Instrumentation;
using Imazen.Common.Instrumentation.Support.InfoAccumulators;
using Imazen.Common.Licensing;
using Imazen.Routing.Caching;
using Imazen.Routing.Engine;
using Imazen.Routing.Health;
using Imazen.Routing.Helpers;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Layers;
using Imazen.Routing.Promises;
using Imazen.Routing.Promises.Pipelines;
using Imazen.Routing.Promises.Pipelines.Watermarking;
using Imazen.Routing.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Imazen.Routing.Serving;

// Everything that implements IHostedService must register as a IHostedService singleton


public static class ImageServerExtensions
{

    internal static IServiceCollection AddImageflowLoggingSupport(this IServiceCollection services, ReLogStoreOptions? logStorageOptions = null)
    {
        if (logStorageOptions != null)
        {
            services.AddSingleton(logStorageOptions);
        }
        services.AddSingleton<IReLogStore>((container) => new ReLogStore(container.GetService<ReLogStoreOptions>() ?? new ReLogStoreOptions()));
        services.AddSingleton<IReLoggerFactory>((container) =>
            new ReLoggerFactory(container.GetRequiredService<ILoggerFactory>(), container.GetRequiredService<IReLogStore>()));
        services.AddSingleton(typeof(IReLogger<>), typeof(ReLogger<>));
        return services;
    }

    internal static IServiceCollection TryAddImageflowLoggingSupport(this IServiceCollection services, ReLogStoreOptions? logStorageOptions = null)
    {
        services.TryAddSingleton(logStorageOptions ?? new ReLogStoreOptions());
        services.TryAddSingleton<IReLogStore>((container) => new ReLogStore(container.GetRequiredService<ReLogStoreOptions>()));
        services.TryAddSingleton<IReLoggerFactory>((container) =>
            new ReLoggerFactory(container.GetRequiredService<ILoggerFactory>(), container.GetRequiredService<IReLogStore>()));
        services.TryAddSingleton(typeof(IReLogger<>), typeof(ReLogger<>));
        return services;
    }
    public static void AddImageServer<TRequest, TResponse, TContext>(this IServiceCollection services)
        where TRequest : IHttpRequestStreamAdapter where TResponse : IHttpResponseStreamAdapter
    {
        services.TryAddDiagnosticsPageReportAndStartup();
        services.TryAddImageflowLoggingSupport();
        services.TryAddSingleton<IPerformanceTracker, NullPerformanceTracker>();
        // Register WatermarkingLogicOptions if not already present
        services.TryAddSingleton(new WatermarkingLogicOptions(null, null));

        // Register CacheHealthTracker with a factory.
        // The DI container will call this factory when someone asks for a CacheHealthTracker.
        services.TryAddSingleton<CacheHealthTracker>(provider =>
        {
            // Now you can safely resolve the logger factory.
            // GetRequiredService will throw if it's not registered, which is what we want.
            var loggerFactory = provider.GetRequiredService<IReLoggerFactory>();
            var logger = loggerFactory.CreateReLogger("CacheHealthTracker"); // Create a logger for this service
            return new CacheHealthTracker(logger);
        });


        services.TryAddSingleton(new MemoryCacheOptions(
            "memCache", 100,
            1000, 1000 * 10, TimeSpan.FromSeconds(10)));

        // Register MemoryCache if not already present
        services.TryAddSingleton<MemoryCache>();


        services.AddSingleton<IBlobCache>(p => p.GetRequiredService<MemoryCache>());
        services.AddSingleton<IHostedImageServerService>(p => p.GetRequiredService<MemoryCache>());
        services.AddSingleton<IHostedService>(p => p.GetRequiredService<MemoryCache>());

        // Register BoundedTaskCollection
        services.TryAddSingleton<BoundedTaskCollection<BlobTaskItem>>(_ =>
        {
            // Note: You'll need a way to manage the CancellationTokenSource's lifecycle.
            // A singleton might not be the right lifetime for this if it needs to be reset.
            var uploadCancellationTokenSource = new CancellationTokenSource();
            return new BoundedTaskCollection<BlobTaskItem>(150 * 1024 * 1024, uploadCancellationTokenSource);
        });

        services.AddSingleton<IHostedService>(p => p.GetRequiredService<BoundedTaskCollection<BlobTaskItem>>());

        // Finally, register the main ImageServer itself, which depends on all the above services.
        // The container will automatically inject them into the constructor.
        services.AddSingleton<IImageServer<TRequest, TResponse, TContext>, ImageServer<TRequest, TResponse, TContext>>();

        // Add as IHostedService via provider lookup
        services.AddSingleton<IHostedService>(p => (IHostedService)p.GetRequiredService<IImageServer<TRequest, TResponse, TContext>>());
    }
}



internal class ImageServer<TRequest, TResponse, TContext> : IImageServer<TRequest,TResponse, TContext>, IHostedService
    where TRequest : IHttpRequestStreamAdapter 
    where TResponse : IHttpResponseStreamAdapter
{
    private readonly IReLogger logger;
    private readonly ILicenseChecker licenseChecker;
    private readonly IRoutingEngine routingEngine;
    private readonly IBlobPromisePipeline pipeline;
    private readonly IPerformanceTracker perf;
    private readonly CancellationTokenSource uploadCancellationTokenSource = new();
    private readonly BoundedTaskCollection<BlobTaskItem> uploadQueue;
    private readonly bool shutdownRegisteredServices = true;
    private readonly CacheHealthTracker cacheHealthTracker;
    private readonly IList<IHostedImageServerService> registeredServices;
    private readonly IServiceProvider serviceProvider;
    public ImageServer(
        IServiceProvider serviceProvider,
        IEnumerable<IInfoProvider> infoProviders,
        ILicenseChecker licenseChecker,
        LicenseOptions licenseOptions,
        IRoutingEngine routingEngine, 
        IPerformanceTracker perfTracker,
        IReLoggerFactory loggerFactory,
        WatermarkingLogicOptions watermarkingLogic,
        CacheHealthTracker cacheHealthTracker,
        IEnumerable<IBlobCache> blobCaches,
        IEnumerable<IBlobCacheProvider> blobCacheProviders,
        MemoryCache memoryCache,
        BoundedTaskCollection<BlobTaskItem> uploadQueue,
        IEnumerable<IHostedImageServerService> registeredServices,
        StartupDiagnostics startupDiagnostics)
    {
        perf = perfTracker;
        this.logger = loggerFactory.CreateReLogger("ImageServer");
        this.routingEngine = routingEngine;
        this.licenseChecker = licenseChecker;
        this.registeredServices = registeredServices.ToList();
        this.serviceProvider = serviceProvider;
        this.uploadQueue = uploadQueue;
        this.perf = perfTracker;
        this.cacheHealthTracker = cacheHealthTracker;
        licenseChecker.Initialize(licenseOptions);
                     
        licenseChecker.FireHeartbeat();
        GlobalPerf.Singleton.SetInfoProviders(infoProviders.ToList());
        
        
        var blobFactory = new SimpleReusableBlobFactory();

        var allCachesFromProviders = blobCacheProviders
            ?.SelectMany(p => p.GetBlobCaches());
        
        var allIndependentCaches = blobCaches;
        List<IBlobCache> allCaches = (allCachesFromProviders ?? []).Concat(allIndependentCaches ?? []).ToList();
        
        foreach (var cache in allCaches)
        {
            cache.Initialize(new BlobCacheSupportData(this.AwaitBeforeShutdown));
        }
        
        var allCachesExceptMemory = allCaches?.Where(c => c != memoryCache)?.ToList();

        logger.LogInformation("Caches: {Caches}", string.Join(",", allCaches?.Select(c => c.UniqueName).ToArray() ?? [])); //string.Format("Caches: {Caches}",

        var sourceCacheOptions = new CacheEngineOptions
        {
            HealthTracker = cacheHealthTracker,
            SeriesOfCacheGroups =
            [
                ..new[] { [memoryCache], allCachesExceptMemory ?? [] }
            ],
            SaveToCaches = allCaches!,
            BlobFactory = blobFactory,
            UploadQueue = uploadQueue,
            Logger = logger
        };
        var imagingOptions = new ImagingMiddlewareOptions
        {
            Logger = logger,
            BlobFactory = blobFactory,
            WatermarkingLogic = watermarkingLogic
        };

        pipeline = new CacheEngine(null, sourceCacheOptions);
        pipeline = new ImagingMiddleware(pipeline, imagingOptions);
        pipeline = new CacheEngine(pipeline, sourceCacheOptions);

        startupDiagnostics.LogIssues(logger);


    }

    private Task AwaitBeforeShutdown()
    {
        return uploadQueue.AwaitAllCurrentTasks();
    }

    public string GetDiagnosticsPageSection(DiagnosticsPageArea area)
    {
        if (area != DiagnosticsPageArea.Start)
        {
            return "";
        }
        var s = new StringBuilder();
        s.AppendLine("\nInstalled Caches");
        s.AppendLine("\nInstalled Providers and Caches");
        // imageServer.GetInstalledProvidersDiag();
        return s.ToString();
    }

    public bool MightHandleRequest<TQ>(string? path, TQ query, TContext context) where TQ : IReadOnlyQueryWrapper
    {
        if (path == null) return false;
        return routingEngine.MightHandleRequest(path, query);
    }
    
    private ValueTask WriteHttpStatusErrAsync(TResponse response, HttpStatus error, CancellationToken cancellationToken)
    {
        perf.IncrementCounter($"http_{error.StatusCode}");
        return SmallHttpResponse.NoStoreNoRobots(error).WriteAsync(response, cancellationToken);
    }
    
    private string CreateEtag(ICacheableBlobPromise promise)
    {
        if (!promise.ReadyToWriteCacheKeyBasisData)
        {
            throw new InvalidOperationException("Promise is not ready to write cache key basis data");
        }
        var weakEtag = promise.CopyCacheKeyBytesTo(stackalloc byte[32])
            .ToHexLowercaseWith("W\"".AsSpan(), "\"".AsSpan());
        return weakEtag;
    }
    public async ValueTask<bool> TryHandleRequestAsync(TRequest request, TResponse response, TContext context, CancellationToken cancellationToken = default)
    {
        licenseChecker?.FireHeartbeat(); // Perhaps limit this to imageflow-handled requests?
        try
        {

            var mutableRequest = MutableRequest.OriginalRequest(request);
            var result = await routingEngine.Route(mutableRequest, cancellationToken);
            if (result == null)
                return false; // We don't have matching routing for this. Let the rest of the app handle it.
            if (result.IsError)
            {
                await WriteHttpStatusErrAsync(response, result.UnwrapError(), cancellationToken);
                return true;
            }

            var snapshot = mutableRequest.ToSnapshot(true);
            var endpoint = result.Unwrap();
            var promise = await endpoint.GetInstantPromise(snapshot, cancellationToken);
            if (promise is ICacheableBlobPromise blobPromise)
            {
                var pipelineResult =
                    await pipeline.GetFinalPromiseAsync(blobPromise, routingEngine, pipeline, request, cancellationToken);
                if (pipelineResult.IsError)
                {
                    await WriteHttpStatusErrAsync(response, pipelineResult.UnwrapError(), cancellationToken);
                    return true;
                }
                
                var finalPromise = pipelineResult.Unwrap();
                
                if (finalPromise.HasDependencies)
                {
                    var dependencyResult = await finalPromise.RouteDependenciesAsync(routingEngine, cancellationToken);
                    if (dependencyResult.IsError)
                    {
                        await WriteHttpStatusErrAsync(response, dependencyResult.UnwrapError(), cancellationToken);
                        return true;
                    }
                }
                
                string? promisedEtag = null;
                // Check for If-None-Match
                if (request.TryGetHeader(HttpHeaderNames.IfNoneMatch, out var conditionalEtag))
                {
                    promisedEtag = CreateEtag(finalPromise);
                    if (promisedEtag == conditionalEtag)
                    {
                        perf.IncrementCounter("etag_hit");
                        response.SetContentLength(0);
                        response.SetStatusCode(304);
                        return true;
                    }

                    perf.IncrementCounter("etag_miss");
                }

                // Now, let's get the actual blob. 
                var blobResult =
                    await finalPromise.TryGetBlobAsync(snapshot, routingEngine, pipeline, cancellationToken);
                if (blobResult.IsError)
                {
                    await WriteHttpStatusErrAsync(response, blobResult.UnwrapError(), cancellationToken);
                    return true;
                }

                using var blobWrapper = blobResult.Unwrap();

                // TODO: if the blob provided an etag, it could be from blob storage, or it could be from a cache.
                // TODO: TryGetBlobAsync already calculates the cache if it's a serverless promise...
                // Since cache provider has to calculate the cache key anyway, can't we figure out how to improve this?
                promisedEtag ??= CreateEtag(finalPromise);

                if (blobWrapper.Attributes.Etag != null && blobWrapper.Attributes.Etag != promisedEtag)
                {
                    perf.IncrementCounter("etag_internal_external_mismatch");
                }

                response.SetHeader(HttpHeaderNames.ETag, promisedEtag);

                // TODO: Do routing layers configure this stuff? Totally haven't thought about it.
                //   if (options.DefaultCacheControlString != null)
                //       response.SetHeader(HttpHeaderNames.CacheControl, options.DefaultCacheControlString);

                if (blobWrapper.Attributes.ContentType != null)
                {
                    response.SetContentType(blobWrapper.Attributes.ContentType);
                    using var consumable = await blobWrapper.GetConsumablePromise().IntoConsumableBlob();
                    await response.WriteBlobWrapperBody(consumable, cancellationToken);
                }
                else
                {
                    using var consumable = await blobWrapper.GetConsumablePromise().IntoConsumableBlob();
                    using var stream = consumable.BorrowStream(DisposalPromise.CallerDisposesStreamThenBlob);
                    await MagicBytes.ProxyToStream(stream, response, cancellationToken);
                }

                perf.IncrementCounter("middleware_ok");
                return true;
            }
            else
            {
                var nonBlobResponse =
                    await promise.CreateResponseAsync(snapshot, routingEngine, pipeline, cancellationToken);
                await nonBlobResponse.WriteAsync(response, cancellationToken);
                perf.IncrementCounter("middleware_ok");
                return true;
            }



            
        }
        catch (BlobMissingException e)
        {
            perf.IncrementCounter("http_404");
            await SmallHttpResponse.Text(404, "The specified resource does not exist.\r\n" + e.Message)
                .WriteAsync(response, cancellationToken);
            return true;

        }
        catch (Exception e)
        {
            var errorName = e.GetType().Name;
            var errorCounter = "middleware_" + errorName;
            perf.IncrementCounter(errorCounter);
            perf.IncrementCounter("middleware_errors");
            throw;
        }
        finally
        {
            // Increment counter for type of file served
            var imageExtension = PathHelpers.GetImageExtensionFromContentType(response.ContentType);
            if (imageExtension != null)
            {
                perf.IncrementCounter("module_response_ext_" + imageExtension);
            }
        }

        throw new NotImplementedException("Unreachable");
        
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        //DependencyRegistrationHealth.ThrowOnProblems(serviceProvider);
        await uploadQueue.StartAsync(cancellationToken);
        await cacheHealthTracker.StartAsync(cancellationToken);
        // startup
        foreach (var service in this.registeredServices)
        {
            await service.StartAsync(cancellationToken);
        }
    }

    public async Task AwaitAllCurrentTasks(CancellationToken cancellationToken)
    {
        await uploadQueue.AwaitAllCurrentTasks();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        //TODO: error handling or no?
        //await uploadCancellationTokenSource.CancelAsync();
        //await uploadQueue.AwaitAllCurrentTasks(); // Too slow?
        await uploadQueue.StopAsync(cancellationToken);
        await cacheHealthTracker.StopAsync(cancellationToken);
        if (shutdownRegisteredServices)
        {
            foreach (var service in this.registeredServices)
            {
                logger.LogInformation("ImageServer is shutting down service {ServiceName}", service.GetType().Name);
                await service.StopAsync(cancellationToken);
            }
        }
#if DEBUG
        if (BlobWrapperCore.GetActiveInstanceCount(this.logger) > 0)
        {
            //TODO: Make a better exception - or a better way to handle this
            throw new InvalidOperationException(BlobWrapperCore.GetActiveInstanceInfo(this.logger));
        }
#endif
    }
}
