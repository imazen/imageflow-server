using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Logging;
using Imazen.Common.Extensibility.Support;
using Imazen.Routing.Serving;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Imageflow.Server.HybridCache
{
    public static class HybridCacheServiceExtensions
    {

        public static IServiceCollection AddImageflowHybridCache(this IServiceCollection services, HybridCacheOptions options)
        {
            // TODO: Refactor to use IOptionsMonitor<HybridCacheOptions> for hot-reload support
            services.RegisterSingletonByThreeInterfaces<IBlobCacheProvider, IHostedImageServerService, IHostedService>(
                container => new HybridCacheService(options, container.GetRequiredService<IReLoggerFactory>()));
            return services;
        }

    }
}
