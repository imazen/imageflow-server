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
        private class ClosureBit<T> where T : class
        {
            IHostedImageServerService? Service { get; set; }
            internal T SetService(T service)
            {
                Service = service as IHostedImageServerService;
                if (Service == null) throw new NotSupportedException();
                return service;
            }

            internal IHostedImageServerService GetImageServerService(IServiceProvider container)
            {
                var ensureOneCreated = container.GetRequiredService<T>();
                var ensureOthersCreated = container.GetServices<T>();
                return Service ?? throw new NotSupportedException();
            }
            internal IHostedService GetHostedService(IServiceProvider container) => GetImageServerService(container);
        }

        public static IServiceCollection AddImageflowHybridCache(this IServiceCollection services, HybridCacheOptions options)
        {


            //TODO: services.AddImageflowLoggingSupport();
            var closure = new ClosureBit<IBlobCacheProvider>();

            //TODO: Use keyed for multiple caches, otherwise add the options to the container
            services.AddSingleton<IBlobCacheProvider>((container) =>
            {
                var loggerFactory = container.GetRequiredService<IReLoggerFactory>();
                return closure.SetService(new HybridCacheService(options, loggerFactory));
            });
            // Add as IHostedImageServerService via GetRequiredService
            services.AddSingleton<IHostedImageServerService>(container => closure.GetImageServerService(container));
            services.AddSingleton<IHostedService>(container => closure.GetHostedService(container));


            return services;
        }

    }
}
