using Azure.Storage.Blobs;
using Imazen.Abstractions.BlobCache;
using Microsoft.Extensions.Logging;

namespace Imageflow.Server.Storage.AzureBlob.Caching;

internal record HealthCheck(IBlobCache Cache, NamedCacheConfiguration Config, ILogger Logger, Dictionary<BlobGroup, BlobServiceClient> ServiceClients, 
    ContainerExistenceCache ContainerExists) 
{
    
    // this.InitialCacheCapabilities = new BlobCacheCapabilities
    // {
    //     CanFetchMetadata = true,
    //     CanFetchData = true,
    //     CanConditionalFetch = false,
    //     CanPut = true,
    //     CanConditionalPut = false,
    //     CanDelete = false,
    //     CanSearchByTag = false,
    //     CanPurgeByTag = false,
    //     CanReceiveEvents = false,
    //     SupportsHealthCheck = false,
    //     SubscribesToRecentRequest = false,
    //     SubscribesToExternalHits = true,
    //     SubscribesToFreshResults = true,
    //     RequiresInlineExecution = false,
    //     FixedSize = false
    // };
    //

    public ValueTask<IBlobCacheHealthDetails> CacheHealthCheck(CancellationToken cancellationToken = default)
    {
        
    }
    
    internal record BasicHealthDetails(bool CanFetchData, bool CanFetchMetadata, bool CanConditionalFetch, bool CanPut, bool CanConditionalPut, bool CanSearchByTag, bool CanPurgeByTag);
    
    internal ValueTask<BasicHealthDetails> CheckGroup(BlobGroup group, CancellationToken cancellationToken = default)
    {
        
    }
}