using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Common.Extensibility.StreamCache;
using Imazen.Common.Issues;
using Microsoft.Extensions.Logging;

namespace Imazen.HybridCache
{
    public class HybridCache : IStreamCache
    {

        internal HashBasedPathBuilder PathBuilder { get; }
        internal AsyncCache AsyncCache { get; }
        internal CleanupManager CleanupManager { get; }
        internal ICacheDatabase Database { get; }
        public HybridCache(ICacheDatabase cacheDatabase, HybridCacheOptions options, ILogger logger)
        {
            Database = cacheDatabase;
            PathBuilder = new HashBasedPathBuilder(options.PhysicalCacheDir, options.Subfolders,
                Path.DirectorySeparatorChar, ".jpg");
            CleanupManager = new CleanupManager(options.CleanupManagerOptions, Database, logger, PathBuilder);
            AsyncCache = new AsyncCache(options.AsyncCacheOptions, CleanupManager,PathBuilder, logger);
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
            await AsyncCache.AwaitEnqueuedTasks();
            await Database.StopAsync(cancellationToken);
        }
        
        public async Task<IStreamCacheResult> GetOrCreateBytes(byte[] key, AsyncBytesResult dataProviderCallback, CancellationToken cancellationToken, bool retrieveContentType)
        {
            return await AsyncCache.GetOrCreateBytes(key, dataProviderCallback, cancellationToken, retrieveContentType);
        }
    }
}