using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3;
using Imageflow.Server.Storage.S3.Caching;
using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Blobs.LegacyProviders;
using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;

namespace Imageflow.Server.Storage.S3
{
    public class S3Service : IBlobWrapperProvider, IDisposable, IBlobCacheProvider, IBlobWrapperProviderZoned
    {
        private readonly List<PrefixMapping> mappings = [];
        private readonly List<S3BlobCache> namedCaches = [];

        private readonly IAmazonS3 s3Client;

        public S3Service(S3ServiceOptions options, IAmazonS3 s3Client, IReLoggerFactory loggerFactory)
        {
            this.s3Client = s3Client;
            UniqueName = options.UniqueName;
            foreach (var m in options.Mappings)
            {
                mappings.Add(m);
            }
            foreach (var m in options.NamedCaches)
            {
                namedCaches.Add(new S3BlobCache(m, s3Client, loggerFactory));
            }
            //TODO: verify this sorts longest first
            mappings.Sort((a,b) => b.Prefix.Length.CompareTo(a.Prefix.Length));
        }

        public string UniqueName { get; }
        public IEnumerable<BlobWrapperPrefixZone> GetPrefixesAndZones()
        {
            return mappings.Select(m => new BlobWrapperPrefixZone(m.Prefix, 
                new LatencyTrackingZone($"s3::bucket/{m.Bucket}", 100)));
        }
        
        public LatencyTrackingZone GetLatencyZone(string virtualPath)
        {
            var mapping =  mappings.FirstOrDefault(m => virtualPath.StartsWith(m.Prefix, 
                m.IgnorePrefixCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            if (mapping.Prefix == null)
            {
                return new LatencyTrackingZone("s3::unknown", 100);
            }
            return new LatencyTrackingZone($"s3::bucket/{mapping.Bucket}", 100);
        }

        public IEnumerable<string> GetPrefixes()
        {
            return mappings.Select(m => m.Prefix);
        }

        public bool SupportsPath(string virtualPath)
        {
            return mappings.Any(m => virtualPath.StartsWith(m.Prefix, 
                m.IgnorePrefixCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        }

        public async Task<CodeResult<IBlobWrapper>> Fetch(string virtualPath)
        {
            var mapping =  mappings.FirstOrDefault(m => virtualPath.StartsWith(m.Prefix, 
                m.IgnorePrefixCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            if (mapping.Prefix == null)
            {
                return CodeResult<IBlobWrapper>.Err((HttpStatus.NotFound, $"No S3 mapping found for virtual path \"{virtualPath}\""));
                
            }

            var partialKey = virtualPath.Substring(mapping.Prefix.Length).TrimStart('/');
            if (mapping.LowercaseBlobPath)
            {
                partialKey = partialKey.ToLowerInvariant();
            }
           
            var key = string.IsNullOrEmpty(mapping.BlobPrefix)
                ? partialKey
                : mapping.BlobPrefix + "/" + partialKey;

            try
            {
                var client = mapping.S3Client ?? this.s3Client;
                var req = new Amazon.S3.Model.GetObjectRequest() { BucketName = mapping.Bucket, Key = key };

                var latencyZone = new LatencyTrackingZone($"s3::bucket/{mapping.Bucket}", 100);
                var s = await client.GetObjectAsync(req);
                return new BlobWrapper(latencyZone,S3BlobHelpers.CreateS3Blob(s));

            } catch (AmazonS3Exception se) {
                if (se.StatusCode == System.Net.HttpStatusCode.NotFound || "NoSuchKey".Equals(se.ErrorCode, StringComparison.OrdinalIgnoreCase)) 
                    throw new BlobMissingException($"Amazon S3 blob \"{key}\" not found.\n({se.Message})", se);
                if ( se.StatusCode == System.Net.HttpStatusCode.Forbidden || "AccessDenied".Equals(se.ErrorCode, StringComparison.OrdinalIgnoreCase)) 
                    throw new BlobMissingException($"Amazon S3 blob \"{key}\" not accessible. The blob may not exist or you may not have permission to access it.\n({se.Message})", se);
                throw;
            }
        }

        public void Dispose()
        {
            try { s3Client?.Dispose(); } catch { }
        }

        IEnumerable<IBlobCache> IBlobCacheProvider.GetBlobCaches()
        {
            return namedCaches;
        }

    }
}