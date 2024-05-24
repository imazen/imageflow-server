using System.Diagnostics;
using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Extensibility.Support;
using Imazen.Common.Issues;
using Imazen.HybridCache.MetaStore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Imazen.HybridCache
{
    public class HybridCache : IBlobCache, IHostedService
    {
        private readonly IReLogger logger;
        private HashBasedPathBuilder PathBuilder { get; }
        internal AsyncCache AsyncCache { get; }
        private CleanupManager CleanupManager { get; }
        private ICacheDatabase<ICacheDatabaseRecord> Database { get; }
        
        private BlobCacheSupportData? SupportData { get; set; }

        public string UniqueName { get; }
        
        
        public HybridCache(HybridCacheAdvancedOptions advancedOptions, IReLogger logger)
        {
            this.logger = logger;
            UniqueName = advancedOptions.UniqueName;

            Database = new MetaStore.MetaStore(new MetaStoreOptions(advancedOptions.PhysicalCacheDir)
            {
                Shards = advancedOptions.Shards,
                MaxLogFilesPerShard = advancedOptions.MaxLogFilesPerShard
            }, advancedOptions, logger);;
            
            PathBuilder = new HashBasedPathBuilder(advancedOptions.PhysicalCacheDir, advancedOptions.Subfolders,
                Path.DirectorySeparatorChar, ".jpg");
            
            CleanupManager = new CleanupManager(advancedOptions.CleanupManagerOptions, Database, logger, PathBuilder);
            AsyncCache = new AsyncCache(advancedOptions.AsyncCacheOptions, CleanupManager, Database, PathBuilder, logger); 
        }
        
        public HybridCache(HybridCacheOptions options, IReLoggerFactory loggerFactory)
        {
            
            this.logger = loggerFactory.CreateReLogger("Imazen.HybridCache['" + options.UniqueName + "']");
            var advancedOptions = new Imazen.HybridCache.HybridCacheAdvancedOptions(options.UniqueName, options.DiskCacheDirectory)
            {
                
                
                CleanupManagerOptions = new CleanupManagerOptions()
                {
                    MaxCacheBytes = Math.Max(0, options.CacheSizeLimitInBytes),
                    MinCleanupBytes = Math.Max(0, options.MinCleanupBytes),
                    MinAgeToDelete = options.MinAgeToDelete.Ticks > 0 ? options.MinAgeToDelete : TimeSpan.Zero,
                },
                Shards = Math.Max(1, options.DatabaseShards),
                MaxLogFilesPerShard = 3
            };
            UniqueName = advancedOptions.UniqueName;

            Database = new MetaStore.MetaStore(new MetaStoreOptions(advancedOptions.PhysicalCacheDir)
            {
                Shards = advancedOptions.Shards,
                MaxLogFilesPerShard = advancedOptions.MaxLogFilesPerShard
            }, advancedOptions, logger);;
            
            PathBuilder = new HashBasedPathBuilder(advancedOptions.PhysicalCacheDir, advancedOptions.Subfolders,
                Path.DirectorySeparatorChar, ".jpg");
            
            CleanupManager = new CleanupManager(advancedOptions.CleanupManagerOptions, Database, logger, PathBuilder);
            AsyncCache = new AsyncCache(advancedOptions.AsyncCacheOptions, CleanupManager, Database, PathBuilder, logger);
        }
        
        internal HybridCache(ICacheDatabase<ICacheDatabaseRecord> database, HybridCacheAdvancedOptions options, IReLogger logger)
        {
            this.logger = logger;
            UniqueName = options.UniqueName;

            Database = database;
            PathBuilder = new HashBasedPathBuilder(options.PhysicalCacheDir, options.Subfolders,
                Path.DirectorySeparatorChar, ".jpg");
            
            CleanupManager = new CleanupManager(options.CleanupManagerOptions, Database, logger, PathBuilder);
            AsyncCache = new AsyncCache(options.AsyncCacheOptions, CleanupManager, Database, PathBuilder, logger);
        }

      
        private void FillMoveFileOverwriteFunc(HybridCacheAdvancedOptions options)
        {
            var moveFileOverwriteFunc = delegate(string from, string to)
            {
#if (NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER)
                File.Move(from, to, true);
#else
                File.Move(from, to);
#endif
            };
            options.CleanupManagerOptions.MoveFileOverwriteFunc ??= options.AsyncCacheOptions.MoveFileOverwriteFunc ?? moveFileOverwriteFunc;
            options.AsyncCacheOptions.MoveFileOverwriteFunc ??= options.CleanupManagerOptions.MoveFileOverwriteFunc ?? moveFileOverwriteFunc;
        }
        

        public IEnumerable<IIssue> GetIssues()
        {
            return Enumerable.Empty<IIssue>();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Database.StartAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            logger?.LogInformation("HybridCache is shutting down...");
            var sw = Stopwatch.StartNew();
            if (SupportData != null)
            {
                await SupportData.AwaitBeforeShutdown();
            }
            await Database.StopAsync(cancellationToken);
            sw.Stop();
            logger?.LogInformation("HybridCache shut down in {ShutdownTime}", sw.Elapsed);
        }
        

        public void Initialize(BlobCacheSupportData supportData)
        {
            SupportData = supportData;
        }

        public Task<IResult<IBlobWrapper, IBlobCacheFetchFailure>> CacheFetch(IBlobCacheRequest request, CancellationToken cancellationToken = default)
        {
            return AsyncCache.CacheFetch(request, cancellationToken);
        }

        public Task<CodeResult> CachePut(ICacheEventDetails e, CancellationToken cancellationToken = default)
        {
            return AsyncCache.CachePut(e, cancellationToken);
        }

        public Task<CodeResult<IAsyncEnumerable<IBlobStorageReference>>> CacheSearchByTag(SearchableBlobTag tag, CancellationToken cancellationToken = default)
        {
            return AsyncCache.CacheSearchByTag(tag, cancellationToken);
        }

        public Task<CodeResult<IAsyncEnumerable<CodeResult<IBlobStorageReference>>>> CachePurgeByTag(SearchableBlobTag tag,
            CancellationToken cancellationToken = default)
        {
            return AsyncCache.CachePurgeByTag(tag, cancellationToken);
        }

        public Task<CodeResult> CacheDelete(IBlobStorageReference reference, CancellationToken cancellationToken = default)
        {
            return AsyncCache.CacheDelete(reference, cancellationToken);
        }

        public Task<CodeResult> OnCacheEvent(ICacheEventDetails e, CancellationToken cancellationToken = default)
        {
            return AsyncCache.OnCacheEvent(e, cancellationToken);
        }

        public BlobCacheCapabilities InitialCacheCapabilities => AsyncCache.InitialCacheCapabilities;

        public ValueTask<IBlobCacheHealthDetails> CacheHealthCheck(CancellationToken cancellationToken = default)
        {
            return AsyncCache.CacheHealthCheck(cancellationToken);
        }
    }
}