﻿using Imazen.Common.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
#pragma warning disable CS0618 // Type or member is obsolete

namespace Imageflow.Server.LegacyBlob
{
    public static class RemoteReaderServiceExtensions
    {
        public static IServiceCollection AddImageflowRemoteReaderService(this IServiceCollection services
            , RemoteReaderServiceOptions options
            )
        {
            services.AddSingleton<IBlobProvider>((container) =>
            {
                var logger = container.GetRequiredService<ILogger<RemoteReaderService>>();
                var http = container.GetRequiredService<IHttpClientFactory>();
                return new RemoteReaderService(options, logger, http);
            });

            return services;
        }
    }
}
