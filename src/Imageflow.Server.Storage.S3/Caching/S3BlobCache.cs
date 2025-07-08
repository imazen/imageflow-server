using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Imazen.Abstractions.BlobCache;
using Microsoft.Extensions.Logging;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using BlobFetchResult = Imazen.Abstractions.Resulting.IResult<Imazen.Abstractions.Blobs.IBlobWrapper, Imazen.Abstractions.BlobCache.IBlobCacheFetchFailure>;

namespace Imageflow.Server.Storage.S3.Caching
{

    internal class S3BlobCache : IBlobCache
    {
        private readonly NamedCacheConfiguration config;
        private readonly IAmazonS3 s3Client;
        private readonly IReLoggerFactory loggerFactory;

        private readonly S3LifecycleUpdater lifecycleUpdater;
        private readonly IReLogger logger;

        public S3BlobCache(NamedCacheConfiguration config, IAmazonS3 s3Client, IReLoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateReLogger($"S3BlobCache('{config.CacheName}')");
            this.config = config;
            this.s3Client = s3Client;
            this.loggerFactory = loggerFactory;
            lifecycleUpdater = new S3LifecycleUpdater(config, s3Client, loggerFactory.CreateReLogger($"S3BlobCache('{config.CacheName}')"));
            InitialCacheCapabilities = new BlobCacheCapabilities
            {
                CanFetchMetadata = true,
                CanFetchData = true,
                CanConditionalFetch = false,
                CanPut = true,
                CanConditionalPut = false,
                CanDelete = true,
                CanSearchByTag = true,
                CanPurgeByTag = true,
                CanReceiveEvents = true,
                SupportsHealthCheck = true,
                SubscribesToRecentRequest = false,
                SubscribesToExternalHits = false,
                FixedSize = false,
                SubscribesToFreshResults = true,
                RequiresInlineExecution = false
            };
        }


        public string UniqueName => config.CacheName;

        private BlobGroupConfiguration GetConfigFor(BlobGroup group)
        {
            if (config.BlobGroupConfigurations.TryGetValue(group, out var groupConfig))
            {
                return groupConfig;
            }
            throw new Exception($"No configuration for blob group {group} in cache {UniqueName}");
        }

        private string TransformKey(string key)
        {
            switch (config.KeyTransform)
            {
                case KeyTransform.Identity:
                    return key;
                default:
                    throw new Exception($"Unknown key transform {config.KeyTransform}");
            }
        }

        private string GetKeyFor(IBlobCacheRequest request)
        {
            var groupConfig = GetConfigFor(request.BlobCategory);
            return groupConfig.Location.BlobPrefix + TransformKey(request.CacheKeyHashString); // We don't add an extension because we can't verify the first few bytes.
        }
        
        internal S3BlobStorageReference GetReferenceFor(IBlobCacheRequest request)
        {
            var groupConfig = GetConfigFor(request.BlobCategory);
            return new S3BlobStorageReference(groupConfig.Location.BucketName, GetKeyFor(request));
        }
        
        public async Task<CodeResult> CachePut(ICacheEventDetails e, CancellationToken cancellationToken = default)
        {
            if (e.Result == null) throw new ArgumentNullException(nameof(e), "CachePut requires a non-null eventDetails.Result");
            // Create a consumable copy (we assume all puts require usability)
            using var input = await e.Result.Unwrap().GetConsumablePromise().IntoConsumableBlob();
            // First make sure everything is in order, bucket exists, lifecycle is set, etc.
            await lifecycleUpdater.UpdateIfIncompleteAsync();
            
            var groupConfig = GetConfigFor(e.OriginalRequest.BlobCategory);
            var s3Key = GetKeyFor(e.OriginalRequest);
            // TODO: validate keys 
            var bucket = groupConfig.Location.BucketName;
            var client = groupConfig.Location.S3Client ?? s3Client;
#if NETSTANDARD2_1_OR_GREATER
            await using var stream = input.BorrowStream(DisposalPromise.CallerDisposesStreamThenBlob);
#else
            using var stream = input.BorrowStream(DisposalPromise.CallerDisposesStreamThenBlob);
#endif
            // create put requests for each tag as well, /tagindex/tagkey/tagvalue/mainkey
            try{
                if (input.Attributes.StorageTags != null)
                {
                    // This makes it possible to use prefix queries to find all blobs with a given tag
                    var tagTasks = input.Attributes.StorageTags
                        .Select(tag => $"{groupConfig.Location.BlobTagsPrefix}/{tag.Key}/{tag.Value}/{s3Key}")
                        .Select(tagKey => new PutObjectRequest()
                        {
                            BucketName = bucket, Key = tagKey, InputStream = stream, ContentType = input.Attributes.ContentType,
                        })
                        .Select(req => client.PutObjectAsync(req, cancellationToken))
                        .ToList();
                    var tagPutResults = await Task.WhenAll(tagTasks);
                    if (tagPutResults.Any(r => r.HttpStatusCode != HttpStatusCode.OK))
                    {
                        return CodeResult.ErrFrom(tagPutResults.First(r => r.HttpStatusCode != HttpStatusCode.OK).HttpStatusCode,
                            $"S3 (tag simulation book-keeping) PutObject {bucket}/{s3Key}");
                    }
                }
                var objReq = new PutObjectRequest()
                {
                    BucketName = bucket, Key = s3Key, 
                    InputStream =  stream, ContentType = input.Attributes.ContentType,
                };
                var response = await client.PutObjectAsync(objReq, cancellationToken);
                return response.HttpStatusCode != HttpStatusCode.OK ? 
                    CodeResult.ErrFrom(response.HttpStatusCode, $"S3 PutObject {bucket}/{s3Key}") : CodeResult.Ok(response.HttpStatusCode);
            } catch (AmazonS3Exception se) {
                var converted = ConvertIfRoutine(se, bucket, s3Key, "PutObject");
                LogIfSerious(se, bucket, s3Key);
                if (converted != null) return CodeResult.Err(converted.Value);
                throw;
            }
        }
        
        internal HttpStatus? ConvertIfRoutine(AmazonS3Exception se, string bucket, string key, string action)
        {
            var normalizedCode =  (HttpStatus)se.StatusCode;
            if ("NoSuchBucket".Equals(se.ErrorCode, StringComparison.OrdinalIgnoreCase) ||
                "NoSuchKey".Equals(se.ErrorCode, StringComparison.OrdinalIgnoreCase))
            {
                normalizedCode = HttpStatus.NotFound;
            }
            if ("AccessDenied".Equals(se.ErrorCode, StringComparison.OrdinalIgnoreCase))
            {
                normalizedCode = HttpStatus.Forbidden;
            }
            if (normalizedCode == HttpStatus.Forbidden || normalizedCode == HttpStatus.NotFound)
            {
                return normalizedCode.WithAddFrom($"S3 {action} {bucket}/{key} error code {se.ErrorCode} exception {se.Message}");
            }
            return null;
        }

        public void Initialize(BlobCacheSupportData supportData)
        {
            
        }

        public async Task<BlobFetchResult> CacheFetch(IBlobCacheRequest request, CancellationToken cancellationToken = default)
        {
            
            var groupConfig = GetConfigFor(request.BlobCategory);
            var s3Key = GetKeyFor(request);
            var bucket = groupConfig.Location.BucketName;
            var client = groupConfig.Location.S3Client ?? s3Client;
            var req = new GetObjectRequest()
            {
                BucketName = bucket, Key = s3Key,
                EtagToMatch = request.Conditions?.IfMatch,
                EtagToNotMatch = request.Conditions?.IfNoneMatch?.FirstOrDefault(),
                
            };
            try{
                var result = await client.GetObjectAsync(req, cancellationToken);
                if (result.HttpStatusCode == HttpStatusCode.OK)
                {
                    var latencyZone = new LatencyTrackingZone($"s3::bucket/{bucket}", 100);
                    return BlobCacheFetchFailure.OkResult(
                        new BlobWrapper(latencyZone,S3BlobHelpers.CreateS3Blob(result, logger)));
                }
                
                // 404/403 are cache misses and return these
                return BlobCacheFetchFailure.ErrorResult(
                        ((HttpStatus)result.HttpStatusCode).WithAddFrom($"S3 (cache fetch) GetObject {bucket}/{s3Key}"));
                

            } catch (AmazonS3Exception se) {
                LogIfSerious(se, bucket, s3Key);
                var converted = ConvertIfRoutine(se, bucket, s3Key, "GetObject");
                if (converted != null) return BlobCacheFetchFailure.ErrorResult(converted.Value);
                throw; 
            }
        }

        public async Task<CodeResult> CacheDelete(IBlobStorageReference reference, CancellationToken cancellationToken = default)
        {
            var (bucket, key) = (S3BlobStorageReference) reference;
            var client = s3Client;
            
            // TODO: also delete tag pointer blobs (first fetch metadata head to get tags, the delete each tag)

            try{
                var result = await client.DeleteObjectAsync(new DeleteObjectRequest()
                {
                    BucketName = bucket, Key = key,
                }, cancellationToken);
                return result.HttpStatusCode == HttpStatusCode.OK ? 
                    CodeResult.Ok() : CodeResult.ErrFrom(result.HttpStatusCode, $"S3 DeleteObject {bucket}/{key}");
            } catch (AmazonS3Exception se) {
                LogIfSerious(se, bucket, key);
                var converted = ConvertIfRoutine(se, bucket, key, "S3 DeleteObject {bucket}/{key}");
                if (converted != null) return CodeResult.Err(converted.Value);
                throw; 
            }
        }

        internal void LogIfSerious(AmazonS3Exception se, string bucketName, string key){
            var logger = loggerFactory.CreateLogger<S3BlobCache>();
            // if the bucket is missing, it's notable. 
            if ("NoSuchBucket".Equals(se.ErrorCode, StringComparison.OrdinalIgnoreCase)) 
                logger.LogError(se, $"Amazon S3 bucket \"{bucketName}\" not found. The bucket may not exist or you may not have permission to access it.\n({se.Message})");
            
            // if there's an authorization or authentication error, it's notable
            if (se.StatusCode == HttpStatusCode.Forbidden || "AccessDenied".Equals(se.ErrorCode, StringComparison.OrdinalIgnoreCase)) 
                logger.LogError(se, $"Amazon S3 blob \"{key}\" in bucket \"{bucketName}\" not accessible. The blob may not exist or you may not have permission to access it.\n({se.Message})");

            // InvalidAccessKeyId and InvalidSecurity
            if ("InvalidAccessKeyId".Equals(se.ErrorCode, StringComparison.OrdinalIgnoreCase) || "InvalidSecurity".Equals(se.ErrorCode, StringComparison.OrdinalIgnoreCase)) 
                logger.LogError(se, $"Your S3 credentials are not working. Caching to bucket \"{bucketName}\" is not operational.\n({se.Message})");
        }


        async IAsyncEnumerable<IBlobStorageReference> SearchByTag(SearchableBlobTag tag, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // We need to search within each group, and iterate with the continuation token
            foreach (var group in config.BlobGroupConfigurations.Keys)
            {
                var groupConfig = GetConfigFor(group);
                var bucket = groupConfig.Location.BucketName;
                var client = groupConfig.Location.S3Client ?? s3Client;
                var prefix = $"{groupConfig.Location.BlobTagsPrefix}/{tag.Key}/{tag.Value}/";
                var req = new ListObjectsV2Request()
                {
                    BucketName = bucket, Prefix = prefix,
                };
                var continuationToken = "";
                do
                {
                    req.ContinuationToken = continuationToken;
                    var result = await client.ListObjectsV2Async(req, cancellationToken);
                    foreach (var obj in result.S3Objects)
                    {
                        // subtract prefix, verify tag matches
                        if (!obj.Key.StartsWith(prefix)) throw new InvalidOperationException($"Unexpected key {obj.Key} in S3 bucket {bucket}");
                        var mainKey = obj.Key.Substring(prefix.Length);
                        yield return new S3BlobStorageReference(bucket, mainKey);
                    }
                    continuationToken = result.NextContinuationToken;
                } while (continuationToken != null);
            }
        }

        public Task<CodeResult<IAsyncEnumerable<IBlobStorageReference>>> CacheSearchByTag(SearchableBlobTag tag,
            CancellationToken cancellationToken = default)
        {
            var enumerable = SearchByTag(tag, cancellationToken);

            return Task.FromResult(CodeResult<IAsyncEnumerable<IBlobStorageReference>>.Ok(enumerable));
            // catch (AmazonS3Exception e)
            // TODO: determine if there are any routine errors
        }

        public Task<CodeResult<IAsyncEnumerable<CodeResult<IBlobStorageReference>>>> CachePurgeByTag(SearchableBlobTag tag, CancellationToken cancellationToken = default)
        {
           
            var enumerable = PurgeByTag(tag, cancellationToken);

            return Task.FromResult(CodeResult<IAsyncEnumerable<CodeResult<IBlobStorageReference>>>.Ok(enumerable));
            // catch (AmazonS3Exception e)
            // TODO: determine if there are any routine errors
        }
        
        private async IAsyncEnumerable<CodeResult<IBlobStorageReference>> PurgeByTag(SearchableBlobTag tag, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Use search + delete
            var enumerable = SearchByTag(tag, cancellationToken);
            await foreach  (var reference in enumerable)
            {
                var result = await CacheDelete(reference, cancellationToken);
                yield return result.IsError ? CodeResult<IBlobStorageReference>.Err(result.UnwrapError()) : CodeResult<IBlobStorageReference>.Ok(reference);
            }
        }


        public Task<CodeResult> OnCacheEvent(ICacheEventDetails e, CancellationToken cancellationToken = default)
        {
            // TODO: handle renewals
            return Task.FromResult(CodeResult.Ok());
        }

        public BlobCacheCapabilities InitialCacheCapabilities { get; }
        public async ValueTask<IBlobCacheHealthDetails> CacheHealthCheck(CancellationToken cancellationToken = default)
        {
            var testResults = await lifecycleUpdater.CreateAndReadTestFilesAsync(true);
            var newCaps = InitialCacheCapabilities;
            if (testResults.WritesFailed)
                newCaps = newCaps with
                {
                    CanPut = false,
                    CanConditionalPut = false
                };
            if (testResults.ReadsFailed)
                newCaps = newCaps with
                {
                    CanFetchData = false,
                    CanFetchMetadata = false,
                    CanConditionalFetch = false
                };
            if (testResults.DeleteFailed)
                newCaps = newCaps with
                {
                    CanDelete = false,
                    CanPurgeByTag = false
                };
            if (testResults.ListFailed)
                newCaps = newCaps with
                {
                    CanSearchByTag = false,
                    CanPurgeByTag = false
                };
            if (testResults.Results.Count != 0)
            {
                return BlobCacheHealthDetails.Errors(
                    $"S3 health and access failed: {testResults.Results.Count} errors (CanGet={!testResults.ReadsFailed}, CanPut={!testResults.WritesFailed}, CanDelete={!testResults.DeleteFailed}, CanList={!testResults.ListFailed})"
                    , testResults.Results, newCaps);
            }

            return BlobCacheHealthDetails.FullHealth(newCaps);

        }
    }
}