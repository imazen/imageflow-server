using Imazen.Common.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Imazen.Abstractions.Blobs.LegacyProviders;
using Imazen.Abstractions.Logging;

namespace Imageflow.Server.Storage.RemoteReader
{
    public static class RemoteReaderServiceExtensions
    {
        public static IServiceCollection AddImageflowRemoteReaderService(this IServiceCollection services
            , RemoteReaderServiceOptions options
            )
        {
            services.AddSingleton<IBlobWrapperProviderZoned>((container) =>
            {
                var logger = container.GetRequiredService<ILogger<RemoteReaderService>>();
                var http = container.GetRequiredService<IHttpClientFactory>();
                return new RemoteReaderService(options, logger, http);
            });

            return services;
        }

        public static IServiceCollection AddImageflowDomainProxyService(this IServiceCollection services
            , DomainProxyServiceOptions options
            )
        {
            services.AddImageflowReLogStoreAndReLoggerFactoryIfMissing();
            services.AddSingleton<IBlobWrapperProvider>((container) =>
            {
                var loggerFactory = container.GetRequiredService<IReLoggerFactory>();
                var http = container.GetRequiredService<IHttpClientFactory>();
                return new DomainProxyService(options, http, loggerFactory);
            });

            return services;
        }
    }
}
