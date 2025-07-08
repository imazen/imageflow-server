using Azure;
using Azure.Storage.Blobs;
using Imageflow.Server.Storage.AzureBlob.Caching;
using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Blobs.LegacyProviders;
using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Microsoft.Extensions.Azure;

namespace Imageflow.Server.Storage.AzureBlob
{
    public class AzureBlobService : IBlobWrapperProvider, IBlobCacheProvider, IBlobWrapperProviderZoned
    {
        private readonly List<PrefixMapping> mappings = new List<PrefixMapping>();

        private readonly List<IBlobCache> caches = new List<IBlobCache>();
        public IEnumerable<IBlobCache> GetBlobCaches() => caches;

        private readonly BlobServiceClient client;

        private readonly IAzureClientFactory<BlobServiceClient> clientFactory;

        private readonly IReLogger logger;
        
        public string UniqueName { get; }
        public IEnumerable<BlobWrapperPrefixZone> GetPrefixesAndZones()
        {
            return mappings.Select(m => new BlobWrapperPrefixZone(m.UrlPrefix, 
                new LatencyTrackingZone($"azure::blob/{m.Container}", 100)));
        }

        public LatencyTrackingZone GetLatencyZone(string virtualPath)
        {
            var mapping = mappings.FirstOrDefault(s => virtualPath.StartsWith(s.UrlPrefix, 
                s.IgnorePrefixCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            return new LatencyTrackingZone($"azure::blob/{mapping.Container}", 100);
        }


        public AzureBlobService(AzureBlobServiceOptions options, IReLoggerFactory loggerFactory,  BlobServiceClient defaultClient, IAzureClientFactory<BlobServiceClient> clientFactory)
        {
            UniqueName = options.UniqueName ?? "azure-blob";
            logger = loggerFactory.CreateReLogger(UniqueName);
            var nameOrInstance = options.GetOrCreateClient();
            if (nameOrInstance.HasValue)
            {   
                if (nameOrInstance.Value.Client != null)
                    client = nameOrInstance.Value.Client;
                else
                    client = clientFactory.CreateClient(nameOrInstance.Value.Name);
            }
            else
            {
                client = defaultClient;
            }
            this.clientFactory = clientFactory;

            foreach (var m in options.Mappings)
            {
                mappings.Add(m);
            }

            mappings.Sort((a, b) => b.UrlPrefix.Length.CompareTo(a.UrlPrefix.Length));

            options.NamedCaches.ForEach(c =>
            {
                var cache = new AzureBlobCache(c, clientName => clientName == null ? client : clientFactory.CreateClient(clientName), loggerFactory); //TODO! get logging working
                caches.Add(cache);
            });
        }



        public IEnumerable<string> GetPrefixes()
        {
            return mappings.Select(m => m.UrlPrefix);
        }

        public bool SupportsPath(string virtualPath)
        {
            return mappings.Any(s => virtualPath.StartsWith(s.UrlPrefix, 
                s.IgnorePrefixCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        }
        

        public async Task<CodeResult<IBlobWrapper>> Fetch(string virtualPath)
        {
            var mapping = mappings.FirstOrDefault(s => virtualPath.StartsWith(s.UrlPrefix, 
                s.IgnorePrefixCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            if (mapping.UrlPrefix == null)
            {
                return CodeResult<IBlobWrapper>.Err(HttpStatus.NotFound.WithAddFrom($"Azure blob mapping not found for \"{virtualPath}\""));
            }

            var partialKey = virtualPath.Substring(mapping.UrlPrefix.Length).TrimStart('/');

            if (mapping.LowercaseBlobPath)
            {
                partialKey = partialKey.ToLowerInvariant();
            }

            var key = string.IsNullOrEmpty(mapping.BlobPrefix)
                    ? partialKey
                    : mapping.BlobPrefix + "/" + partialKey;

            
            try
            {
                var containerClient = client.GetBlobContainerClient(mapping.Container);
                var blobClient = containerClient.GetBlobClient(key);
                var reference = new AzureBlobStorageReference(containerClient.Uri.AbsoluteUri, key);
                var s = await blobClient.DownloadStreamingAsync();
                var latencyZone = new LatencyTrackingZone($"azure::blob/{mapping.Container}", 100);
                return CodeResult<IBlobWrapper>.Ok(new BlobWrapper(latencyZone,AzureBlobHelper.CreateConsumableBlob(reference, s.Value, logger)));

            }
            catch (RequestFailedException e)
            {
                if (e.Status == 404)
                {
                    return CodeResult<IBlobWrapper>.Err(HttpStatus.NotFound.WithAddFrom($"Azure blob \"{key}\" not found.\n({e.Message})"));
                }
                throw;
            }
        }


    }
}