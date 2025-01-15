using Imazen.Common.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Imazen.Abstractions.Blobs.LegacyProviders;

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
    }
}
