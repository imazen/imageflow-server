using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Blobs.LegacyProviders;
using Imazen.Abstractions.Resulting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Imazen.Abstractions.Logging;

namespace Imageflow.Server.Example
{
    
    public static class CustomBlobServiceExtensions
    {
        public static IServiceCollection AddImageflowCustomBlobService(this IServiceCollection services,
            CustomBlobServiceOptions options)
        {
            services.AddSingleton<IBlobWrapperProvider>((container) =>
            {
                var loggerFactory = container.GetRequiredService<IReLoggerFactory>();
                return new CustomBlobService(options, loggerFactory);
            });

            return services;
        }
    }

    public class CustomBlobServiceOptions
    {
        public string Name { get; set; } = "CustomBlobService";
        public BlobClientOptions BlobClientOptions { get; set; } = new BlobClientOptions();

        /// <summary>
        /// Can block container/key pairs by returning null
        /// </summary>
        public Func<string, string, Tuple<string,string>> ContainerKeyFilterFunction { get; set; } 
            = Tuple.Create;
        public string ConnectionString { get; set; }
        public bool IgnorePrefixCase { get; set; } = true;
        private string prefix = "/blob/";
        /// <summary>
        /// Ensures the prefix begins and ends with a slash
        /// </summary>
        /// <exception cref="ArgumentException">If prefix is / </exception>
        public string Prefix
        {
            get => prefix;
            set
            {
                var p = value.TrimStart('/').TrimEnd('/');
                if (p.Length == 0)
                {
                    throw new ArgumentException("Prefix cannot be /", nameof(p));
                }
                prefix = '/' + p + '/';
            }
        }
    }
    
    
    public class CustomBlobService : IBlobWrapperProvider
    {
        private readonly BlobServiceClient client;

        private readonly CustomBlobServiceOptions options;
        private readonly IReLoggerFactory loggerFactory;
        private readonly IReLogger logger;
        
        public CustomBlobService(CustomBlobServiceOptions options, IReLoggerFactory loggerFactory)
        {
            this.options = options;
            this.loggerFactory = loggerFactory;
            this.logger = loggerFactory.CreateReLogger("CustomBlobService");
            client = new BlobServiceClient(options.ConnectionString, options.BlobClientOptions);
        }

        public string UniqueName => options.Name;
        public IEnumerable<string> GetPrefixes()
        {
            return Enumerable.Repeat(options.Prefix, 1);
        }

        public bool SupportsPath(string virtualPath)
            => virtualPath.StartsWith(options.Prefix,
                options.IgnorePrefixCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        

        public async Task<CodeResult<IBlobWrapper>> Fetch(string virtualPath)
        {
            if (!SupportsPath(virtualPath))
            {
                return null;
            }
            var path = virtualPath.Substring(options.Prefix.Length).TrimStart('/');
            var indexOfSlash = path.IndexOf('/');
            if (indexOfSlash < 1) return null;

            var container = path.Substring(0, indexOfSlash);
            var blobKey = path.Substring(indexOfSlash + 1);

            var filtered = options.ContainerKeyFilterFunction(container, blobKey);
            
            if (filtered == null)
            {
                return null;
            }
            
            container = filtered.Item1;
            blobKey = filtered.Item2;
            
            try
            {
                var blobClient = client.GetBlobContainerClient(container).GetBlobClient(blobKey);
                var latencyZone = new LatencyTrackingZone($"azure::blob/{container}", 100);
                var s = await blobClient.DownloadAsync();
                return CodeResult<IBlobWrapper>.Ok(new BlobWrapper(latencyZone,CustomAzureBlobHelpers.CreateAzureBlob(s, logger.WithReScopeData("virtualPath", virtualPath))));

            }
            catch (RequestFailedException e)
            {
                if (e.Status == 404)
                {
                    return CodeResult<IBlobWrapper>.Err(HttpStatus.NotFound.WithMessage(
                        $"Azure blob \"{blobKey}\" not found."));
                }

                throw;

            }
        }

    }
    internal static class CustomAzureBlobHelpers
    {
        public static StreamBlob CreateAzureBlob(Response<BlobDownloadInfo> response, IReLogger logger)
        {
            var a = new BlobAttributes()
            {
                LastModifiedDateUtc = response.Value.Details.LastModified.UtcDateTime,
                ContentType = response.Value.ContentType,
                Etag = response.Value.Details.ETag.ToString(),
                
            };
            var stream = response.Value.Content;
            return new StreamBlob(a, stream, logger, null);
        }
    }
}