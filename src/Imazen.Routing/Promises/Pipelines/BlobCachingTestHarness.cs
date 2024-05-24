using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Concurrency.BoundedTaskCollection;
using Imazen.Routing.Caching;
using Imazen.Routing.Health;
using Imazen.Routing.Requests;
using Microsoft.Extensions.Hosting;

namespace Imazen.Routing.Promises.Pipelines;

public record BlobCachingTestHarnessOptions(
    long? MaxUploadQueueBytes,
    MemoryCacheOptions? MemoryCacheOptions,
    bool DelayRequestUntilUploadsComplete,
    List<List<IBlobCache>> SeriesOfCacheGroups,
    List<IBlobCache> SaveToCaches,
    Func<IRequestSnapshot, CancellationToken, ValueTask<CodeResult<IBlobWrapper>>> BlobProvider,
    LatencyTrackingZone BlobProviderLatencyZone,
    IReLogger Logger,
    bool LockByUniqueRequest,
    bool ShutdownServices)
{
    public static BlobCachingTestHarnessOptions TestSingleCacheSync(IBlobCache cache, Func<IRequestSnapshot, CancellationToken, ValueTask<CodeResult<IBlobWrapper>>> blobProvider, IReLogger logger)
    {
        return new BlobCachingTestHarnessOptions(
            null,
            null,
            true,
            new List<List<IBlobCache>> { new List<IBlobCache> { cache } },
            new List<IBlobCache> { cache },
            blobProvider,
            new LatencyTrackingZone("TestBlobProvider", 10000,true),
            logger,
            false,
            true
        );
    }
}


public class BlobCachingTestHarness: IHostedService
{
    BlobCachingTestHarnessOptions options;
    BlobPipelineHarness blobPipelineHarness;
    BoundedTaskCollection<BlobTaskItem>? uploadQueue;
    CacheHealthTracker cacheHealthTracker;
    CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();
    public BlobCachingTestHarness(BlobCachingTestHarnessOptions options)
    {
        this.options = options;
        if (options.MaxUploadQueueBytes != null)
        {
            uploadQueue = new BoundedTaskCollection<BlobTaskItem>(options.MaxUploadQueueBytes.Value, CancellationTokenSource);
            // Now ensure caches wait for uploads to write before shutting down.
            foreach(var c in options.SaveToCaches)
                c.Initialize(new BlobCacheSupportData(() => uploadQueue!.AwaitAllCurrentTasks()));
        }
        cacheHealthTracker = new CacheHealthTracker(options.Logger);
        var cacheEngineOptions = new CacheEngineOptions
        {
            HealthTracker = cacheHealthTracker,
            SeriesOfCacheGroups = options.SeriesOfCacheGroups,
            SaveToCaches = options.SaveToCaches,
            Logger = options.Logger,
            UploadQueue = uploadQueue,
            DelayRequestUntilUploadsComplete = options.DelayRequestUntilUploadsComplete,
            LockByUniqueRequest = options.LockByUniqueRequest,
            BlobFactory = new SimpleReusableBlobFactory()
        };
        var cacheEngine = new CacheEngine(null, cacheEngineOptions);
        blobPipelineHarness = new BlobPipelineHarness(new BlobPipelineHarnessOptions(
           cacheEngine,
              options.BlobProvider,
            options.Logger,
            options.BlobProviderLatencyZone));

    }

    public async ValueTask<CodeResult<IBlobWrapper>> RequestBlobWrapper(string path, string query = "",
        CancellationToken cancellationToken = default)
    {
        return await blobPipelineHarness.RequestBlobWrapper(path, query, cancellationToken);
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (uploadQueue != null)
        {
            await uploadQueue.StopAsync(cancellationToken);
        }

        await cacheHealthTracker.StopAsync(cancellationToken);
        if (options.ShutdownServices)
        {
            var allCaches = options.SeriesOfCacheGroups.SelectMany(x => x).Concat(options.SaveToCaches);
            foreach (var cache in allCaches)
            {
                if (cache is IHostedService service)
                {
                    await service.StopAsync(cancellationToken);
                }
            }
        }
    }

    public async Task AwaitEnqueuedTasks()
    {
        if (uploadQueue != null)
        {
            await uploadQueue.AwaitAllCurrentTasks();
        }
    }
}