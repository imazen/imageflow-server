using System;
using System.Collections.Generic;
using Imazen.Abstractions.BlobCache;

namespace Imageflow.Server.Storage.AzureBlob.Caching
{
    internal enum KeyTransform{
        Identity
    }
    public readonly struct NamedCacheConfiguration 
    {

        internal readonly string CacheName;

        internal readonly KeyTransform KeyTransform;
        internal readonly BlobClientOrName? BlobClient;
        internal readonly Dictionary<BlobGroup, BlobGroupConfiguration> BlobGroupConfigurations;


        /// <summary>
        /// Creates a new named cache configuration with the specified blob Group configurations
        /// </summary>
        /// <param name="cacheName"></param>
        /// <param name="defaultClient">"></param>
        /// <param name="blobGroupConfigurations"></param>
        internal NamedCacheConfiguration(string cacheName, BlobClientOrName defaultClient, Dictionary<BlobGroup, BlobGroupConfiguration> blobGroupConfigurations)
        {
            CacheName = cacheName;
            BlobClient = defaultClient;
            BlobGroupConfigurations = blobGroupConfigurations;
            KeyTransform = KeyTransform.Identity;
        }


        /// <summary>
        /// Creates a new named cache configuration with the specified sliding expiry days
        /// </summary>
        /// <param name="cacheName">The name of the cache, for use in the rest of the configuration</param>
        /// <param name="defaultClient">If null, will use the default client.</param>
        /// <param name="cacheContainerName">The bucket to use for the cache. Must be in the same region or performance will be terrible.</param>
        /// <param name="slidingExpiryDays">If null, then no expiry will occur</param>
        /// <param name="createIfMissing">If true, missing buckets will be created in the default region for the S3 client</param>
        /// <param name="updateLifecycleRules">If true, lifecycle rules will be synchronized.</param>
        /// <exception cref="ArgumentException"></exception>
        internal NamedCacheConfiguration(string cacheName, BlobClientOrName defaultClient, string cacheContainerName, CacheContainerCreation createIfMissing, CacheContainerLifecycleRules updateLifecycleRules, int? slidingExpiryDays)
        {
            // slidingExpiryDays cannot be less than 3, if specified 
            if (slidingExpiryDays is < 3)
            {
                throw new ArgumentException("slidingExpiryDays cannot be less than 3, if specified", nameof(slidingExpiryDays));
            }
            var blobMetadataExpiry = slidingExpiryDays * 4;
            var createContainerIfMissing = createIfMissing == CacheContainerCreation.CreateIfMissing;
            var configureExpiry = updateLifecycleRules == CacheContainerLifecycleRules.ConfigureExpiryForCacheFolders;

            KeyTransform = KeyTransform.Identity;
            CacheName = cacheName;
            BlobClient = defaultClient;
            BlobGroupConfigurations = new Dictionary<BlobGroup, BlobGroupConfiguration>
            {
                { BlobGroup.GeneratedCacheEntry, new BlobGroupConfiguration(new BlobGroupLocation(cacheContainerName, "imageflow-cache/blobs/", defaultClient), BlobGroupLifecycle.SlidingExpiry(slidingExpiryDays), createContainerIfMissing, configureExpiry) },
                { BlobGroup.SourceMetadata, new BlobGroupConfiguration(new BlobGroupLocation(cacheContainerName, "imageflow-cache/source-metadata/", defaultClient), BlobGroupLifecycle.NonExpiring, createContainerIfMissing, configureExpiry) },
                { BlobGroup.CacheEntryMetadata, new BlobGroupConfiguration(new BlobGroupLocation(cacheContainerName, "imageflow-cache/blob-metadata/", defaultClient), BlobGroupLifecycle.SlidingExpiry(blobMetadataExpiry), createContainerIfMissing, configureExpiry) },
                { BlobGroup.Essential, new BlobGroupConfiguration(new BlobGroupLocation(cacheContainerName, "imageflow-cache/essential/", defaultClient), BlobGroupLifecycle.NonExpiring, createContainerIfMissing, configureExpiry) }
            };
        }

        /// <summary>
        /// Creates a new named cache configuration with the specified cache name and container name and createIfMissing (no expiry)
        /// </summary>
        /// <param name="cacheName">The name of the cache, for use in the rest of the configuration</param>
        /// <param name="defaultClient">If null, will use the default client.</param>
        /// <param name="cacheContainerName">The container to use for the cache. Must be in the same region or performance will be terrible.</param>
        /// <param name="createIfMissing">If true, missing containers will be created in the default region for the Azure client</param>
        internal NamedCacheConfiguration(string cacheName, BlobClientOrName defaultClient, string cacheContainerName, CacheContainerCreation createIfMissing)
            : this(cacheName, defaultClient, cacheContainerName, createIfMissing, CacheContainerLifecycleRules.DoNotUpdate, null)
        {
        }
        /// <summary>
        /// Creates a new named cache configuration with the specified cache name and container name and createIfMissing (no expiry)
        /// </summary>
        /// <param name="cacheName">The name of the cache, for use in the rest of the configuration</param>
        /// <param name="blobClientName">If null, will use the default client.</param>
        /// <param name="cacheContainerName">The container to use for the cache. Must be in the same region or performance will be terrible.</param>
        /// <param name="createIfMissing">If true, missing containers will be created in the default region for the Azure client</param>
        public NamedCacheConfiguration(string cacheName, string cacheContainerName, string blobClientName, CacheContainerCreation createIfMissing)
            : this(cacheName, new BlobClientOrName(blobClientName), cacheContainerName, createIfMissing, CacheContainerLifecycleRules.DoNotUpdate, null)
        {
        }

    }
}