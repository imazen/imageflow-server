using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Imazen.Abstractions.BlobCache;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Azure;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Resulting;

namespace Imageflow.Server.Storage.AzureBlob.Caching
{

    /// <summary>
    ///  Best used with Azure Block Blob Premium Storage (SSD) (LRS and Flat Namespace are fine; GRS is not needed for caches). Costs ~20% more but is much faster.
    /// </summary>
    internal class AzureBlobCache : IBlobCache
    {
        private NamedCacheConfiguration config;
        private BlobServiceClient defaultBlobServiceClient;
        private ILogger logger;
        private ContainerExistenceCache containerExists;
        private Dictionary<BlobGroup, BlobServiceClient> serviceClients;


// https://devblogs.microsoft.com/azure-sdk/best-practices-for-using-azure-sdk-with-asp-net-core/

        public AzureBlobCache(NamedCacheConfiguration config, Func<string?, BlobServiceClient> blobServiceFactory,
            ILoggerFactory loggerFactory)
        {
            this.config = config;
            // Map BlobGroupConfigurations dict, replacing those keys with the .Location.BlobClient value

            this.serviceClients = config.BlobGroupConfigurations.Select(p =>
                new KeyValuePair<BlobGroup, BlobServiceClient>(p.Key,
                    p.Value.Location.AzureClient.Resolve(blobServiceFactory))).ToDictionary(x => x.Key, x => x.Value);

            this.defaultBlobServiceClient = blobServiceFactory(null);
            this.logger = loggerFactory.CreateLogger("AzureBlobCache");

            this.containerExists =
                new ContainerExistenceCache(config.BlobGroupConfigurations.Values.Select(x => x.Location.ContainerName));
            
              
            this.InitialCacheCapabilities = new BlobCacheCapabilities
            {
                CanFetchMetadata = true,
                CanFetchData = true,
                CanConditionalFetch = false,
                CanPut = true,
                CanConditionalPut = false,
                CanDelete = true,
                CanSearchByTag = true,
                CanPurgeByTag = true,
                CanReceiveEvents = false,
                SupportsHealthCheck = false,
                SubscribesToRecentRequest = false,
                SubscribesToExternalHits = true,
                SubscribesToFreshResults = true,
                RequiresInlineExecution = false,
                FixedSize = false
            };
        }

    

        public string UniqueName => config.CacheName;

        internal BlobServiceClient GetClientFor(BlobGroup group)
        {
            return serviceClients[group];
        }
        internal BlobGroupConfiguration GetConfigFor(BlobGroup group)
        {
            if (config.BlobGroupConfigurations.TryGetValue(group, out var groupConfig))
            {
                return groupConfig;
            }
            throw new Exception($"No configuration for blob Group {group} in cache {UniqueName}");
        }

        internal string TransformKey(string key)
        {
            switch (config.KeyTransform)
            {
                case KeyTransform.Identity:
                    return key;
                default:
                    throw new Exception($"Unknown key transform {config.KeyTransform}");
            }
        }

        internal string GetKeyFor(BlobGroup group, string key)
        {
            var groupConfig = GetConfigFor(group);
            return groupConfig.Location.BlobPrefix + TransformKey(key);
        }


        
        public async Task<CodeResult> CachePut(ICacheEventDetails e, CancellationToken cancellationToken = default)
        {
            if (e.Result == null || e.Result.IsError) return CodeResult.Err(HttpStatus.BadRequest.WithMessage("CachePut cannot be called with an invalid result"));
            var group = e.BlobCategory;
            var key = e.OriginalRequest.CacheKeyHashString;
            var groupConfig = GetConfigFor(group);
            var azureKey = GetKeyFor(group, key);
            // TODO: validate key eventually
            var container = GetClientFor(group).GetBlobContainerClient(groupConfig.Location.ContainerName);
            var blob = container.GetBlobClient(azureKey);
            
            if (!containerExists.Maybe(groupConfig.Location.ContainerName) && !groupConfig.CreateContainerIfMissing)
            {
                try
                {
                    await container.CreateIfNotExistsAsync();
                    containerExists.Set(groupConfig.Location.ContainerName, true);
                }
                catch (Azure.RequestFailedException ex)
                {
                    LogIfSerious(ex, groupConfig.Location.ContainerName, key);
                    return CodeResult.Err(new HttpStatus(ex.Status).WithAppend(ex.Message));
                }
            }
            try
            {
                using var consumable = await e.Result.Unwrap().GetConsumablePromise().IntoConsumableBlob();
                using var data = consumable.BorrowStream(DisposalPromise.CallerDisposesStreamThenBlob);
                var options = new BlobUploadOptions();
                var tags = e.Result.Unwrap().Attributes.StorageTags;
                if (tags != null && tags.Count > 0)
                {
                    options.Tags = tags.ToDictionary(t => t.Key, t => t.Value);
                }
                await blob.UploadAsync(data, options, cancellationToken);
                
                return CodeResult.Ok();
            }
            catch (Azure.RequestFailedException ex)
            {

                LogIfSerious(ex, groupConfig.Location.ContainerName, key);
                return CodeResult.Err(new HttpStatus(ex.Status).WithAppend(ex.Message));
            }
        }


        public void Initialize(BlobCacheSupportData supportData)
        {
            
        }

        public async Task<IResult<IBlobWrapper, IBlobCacheFetchFailure>> CacheFetch(IBlobCacheRequest request, CancellationToken cancellationToken = default)
        {
            var group = request.BlobCategory;
            var key = request.CacheKeyHashString;
            var groupConfig = GetConfigFor(group);
            var azureKey = GetKeyFor(group, key);
            var container = GetClientFor(group).GetBlobContainerClient(groupConfig.Location.ContainerName);
            var blob = container.GetBlobClient(azureKey);
            var storage = new AzureBlobStorageReference(groupConfig.Location.ContainerName, azureKey);
            try
            {
                var response = await blob.DownloadStreamingAsync(new BlobDownloadOptions(), cancellationToken);
                containerExists.Set(groupConfig.Location.ContainerName, true);
                return BlobCacheFetchFailure.OkResult(new BlobWrapper(null,AzureBlobHelper.CreateConsumableBlob(storage, response)));

            }
            catch (Azure.RequestFailedException ex)
            {
                //For cache misses, just return a null blob. 
                if (ex.Status == 404)
                {
                    return BlobCacheFetchFailure.MissResult(this, this);
                }
                LogIfSerious(ex, groupConfig.Location.ContainerName, key);
                return BlobCacheFetchFailure.ErrorResult(new HttpStatus(ex.Status).WithAppend(ex.Message), this, this);
            }
        }
    
        
        public async Task<CodeResult> CacheDelete(IBlobStorageReference reference, CancellationToken cancellationToken = default)
        {
            var azureRef = (AzureCacheBlobReference) reference;
            var client = GetClientFor(azureRef.Group);
            var container = client.GetBlobContainerClient(azureRef.ContainerName);
            var blob = container.GetBlobClient(azureRef.BlobName);
            try
            {
                await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, null, cancellationToken);
                return CodeResult.Ok();
            }
            catch (Azure.RequestFailedException ex)
            {
                LogIfSerious(ex, azureRef.ContainerName, azureRef.BlobName);
                return CodeResult.Err(new HttpStatus(ex.Status).WithAppend(ex.Message));
            }
        }
        internal void LogIfSerious(Azure.RequestFailedException ex, string containerName, string key)
        {
            // Implement similar logging as in the S3 version, adjusted for Azure exceptions and error codes.
            logger.LogError(ex, "AzureBlobCache error for {ContainerName}/{Key}: {Message}", containerName, key, ex.Message);
        }

  

        public async Task<CodeResult<IAsyncEnumerable<IBlobStorageReference>>> CacheSearchByTag(SearchableBlobTag tag, CancellationToken cancellationToken = default)
        {
            return CodeResult<IAsyncEnumerable<IBlobStorageReference>>.Ok(CacheSearchByTagInner(tag, cancellationToken));
        }
        
        public async IAsyncEnumerable<IBlobStorageReference> CacheSearchByTagInner(SearchableBlobTag tag,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var group in config.BlobGroupConfigurations.Keys)
            {
                var client = GetClientFor(group);
                var accountName = client.AccountName;
                await foreach (var item in SearchByTag(tag,
                                   client.GetBlobContainerClient(GetConfigFor(group).Location.ContainerName),
                                   accountName, cancellationToken))
                {
                    yield return item;
                }
            }
        }
        
        private static string CreateTagQuery(SearchableBlobTag tag) => $"\"{tag.Key.Replace("\"", "\\\"")}\"='{tag.Value.Replace("'", "\\'")}'";

        private async IAsyncEnumerable<IBlobStorageReference> SearchByTag(SearchableBlobTag tag, BlobContainerClient client, string accountName, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var item in client.FindBlobsByTagsAsync(CreateTagQuery(tag), cancellationToken))
            {
                yield return AzureCacheBlobReference.FromTaggedBlobItem(item, BlobGroup.GeneratedCacheEntry, accountName);
            }
        }

        public async Task<CodeResult<IAsyncEnumerable<CodeResult<IBlobStorageReference>>>> CachePurgeByTag(SearchableBlobTag tag, CancellationToken cancellationToken = default)
        {
            try
            {
                return CodeResult<IAsyncEnumerable<CodeResult<IBlobStorageReference>>>.Ok(CachePurgeByTagInner(tag, cancellationToken));
            }
            catch (RequestFailedException ex)
            {
                return CodeResult<IAsyncEnumerable<CodeResult<IBlobStorageReference>>>.Err(ex.ToHttpStatus());
            }
        }
        public async IAsyncEnumerable<CodeResult<IBlobStorageReference>> CachePurgeByTagInner(SearchableBlobTag tag,[EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach(var item in CacheSearchByTagInner(tag, cancellationToken))
            {
                var result = await CacheDelete(item, cancellationToken);
                yield return result.IsOk ? CodeResult<IBlobStorageReference>.Ok(item) : CodeResult<IBlobStorageReference>.Err(result.UnwrapError());
            }
        }
        public Task<CodeResult> OnCacheEvent(ICacheEventDetails e, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CodeResult.Err(HttpStatus.NotImplemented));
        }

        public BlobCacheCapabilities InitialCacheCapabilities { get; }
        public ValueTask<IBlobCacheHealthDetails> CacheHealthCheck(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
    
    internal record AzureCacheBlobReference(BlobGroup Group, string ContainerName, string BlobName, string FullyQualified) : IBlobStorageReference
    {
        public string GetFullyQualifiedRepresentation()
        {
            return FullyQualified;
        }

        public int EstimateAllocatedBytesRecursive => 24 + BlobName.Length * 2 + FullyQualified.Length * 2;
        
        public static AzureCacheBlobReference FromTaggedBlobItem(TaggedBlobItem item, BlobGroup group, string accountName)
        {
            return new AzureCacheBlobReference(group, item.BlobContainerName, item.BlobName, 
                   $"azure://{accountName}/{item.BlobContainerName}/{item.BlobName}");
        }
    }
    internal static class HttpStatusFromAzureExtensions
    {
        public static HttpStatus ToHttpStatus(this RequestFailedException ex)
        {
            return new HttpStatus(ex.Status).WithAppend(ex.Message);
        }
    }
 
}
