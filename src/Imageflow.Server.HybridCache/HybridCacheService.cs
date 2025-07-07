using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Logging;
using Imazen.Common.Issues;
using Imazen.Routing.Serving;
using Microsoft.Extensions.Hosting;

namespace Imageflow.Server.HybridCache
{
    public class HybridCacheService : IBlobCacheProvider, IHostedService, IHostedImageServerService
    {
        private readonly List<HybridNamedCache> namedCaches = new List<HybridNamedCache>();
        public HybridCacheService(IEnumerable<HybridCacheOptions> namedCacheConfigurations, IReLoggerFactory loggerFactory)
        {
            namedCaches.AddRange(namedCacheConfigurations.Select(c => new HybridNamedCache(c, loggerFactory)));
        }

        public HybridCacheService(HybridCacheOptions options, IReLoggerFactory loggerFactory)
        {
            namedCaches.Add(new HybridNamedCache(options, loggerFactory));
        }

        public IEnumerable<IIssue> GetIssues() => namedCaches.SelectMany(c => c.GetIssues());
        

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.WhenAll(namedCaches.Select(c => c.StartAsync(cancellationToken)));
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.WhenAll(namedCaches.Select(c => c.StopAsync(cancellationToken)));
        }

        public IEnumerable<IBlobCache> GetBlobCaches() => namedCaches;
    }
}
