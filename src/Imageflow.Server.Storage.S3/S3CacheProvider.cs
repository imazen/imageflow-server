using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Imazen.Caching;

namespace Imageflow.Server.Storage.S3;

/// <summary>
/// Adapts Amazon S3 to the ICacheProvider interface for use as a cloud tier in CacheCascade.
///
/// Object layout: {prefix}{CacheKey.ToStoragePath()} → e.g. "cache/ab12/ab12...f3/de45...89"
/// Metadata: content-type in S3 object metadata, source_prefix as S3 user metadata for purge support.
///
/// No eviction logic — configure S3 Object Lifecycle rules to auto-expire stale cached objects.
/// PurgeBySourceAsync uses prefix listing (CacheKey.ToStoragePath() groups variants under source prefix).
/// </summary>
public sealed class S3CacheProvider : ICacheProvider
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly string _prefix;

    /// <summary>
    /// Metadata key for storing the source prefix (used by purge-by-source for verification).
    /// Stored as S3 user metadata (x-amz-meta-source-prefix).
    /// </summary>
    private const string SourcePrefixMetaKey = "source-prefix";

    public string Name { get; }
    public CacheProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Create an S3 cache provider.
    /// </summary>
    /// <param name="name">Provider name for cascade identification.</param>
    /// <param name="client">Amazon S3 client (caller owns lifecycle).</param>
    /// <param name="bucket">S3 bucket name.</param>
    /// <param name="prefix">Optional key prefix (e.g., "cache/"). Include trailing slash if desired.</param>
    /// <param name="region">Region hint for latency zone naming (e.g., "us-east-1").</param>
    public S3CacheProvider(string name, IAmazonS3 client, string bucket, string? prefix = null, string? region = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _bucket = bucket ?? throw new ArgumentNullException(nameof(bucket));
        _prefix = prefix ?? "";

        var zone = $"s3:{region ?? "unknown"}:{bucket}";
        Capabilities = new CacheProviderCapabilities
        {
            RequiresInlineExecution = false,
            LatencyZone = zone
        };
    }

    private string ObjectKey(CacheKey key) => _prefix + key.ToStoragePath();

    /// <summary>
    /// S3 prefix for all variants of a given source hash.
    /// CacheKey.ToStoragePath() = "{sourceHex[0..3]}/{sourceHex}/{variantHex}"
    /// So prefix = "{prefix}{sourceHex[0..3]}/{sourceHex}/" lists all variants.
    /// </summary>
    private string SourceObjectPrefix(ReadOnlyMemory<byte> sourceHash)
    {
        var sourceHex = HexFromHash(sourceHash);
        return $"{_prefix}{sourceHex.Substring(0, 4)}/{sourceHex}/";
    }

    public async ValueTask<CacheFetchResult?> FetchAsync(CacheKey key, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucket,
                Key = ObjectKey(key)
            }, ct);

            if (response.HttpStatusCode != HttpStatusCode.OK)
                return null;

            var metadata = new CacheEntryMetadata
            {
                ContentType = response.Headers.ContentType,
                ContentLength = response.ContentLength
            };
            return new CacheFetchResult(response.ResponseStream, metadata);
        }
        catch (AmazonS3Exception ex) when (
            ex.StatusCode == HttpStatusCode.NotFound ||
            "NoSuchKey".Equals(ex.ErrorCode, StringComparison.OrdinalIgnoreCase) ||
            "NoSuchBucket".Equals(ex.ErrorCode, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    public async ValueTask StoreAsync(CacheKey key, byte[] data, CacheEntryMetadata metadata,
        CancellationToken ct = default)
    {
        using var stream = new MemoryStream(data, writable: false);
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = ObjectKey(key),
            InputStream = stream,
            ContentType = metadata.ContentType ?? "application/octet-stream",
        };
        // Store source prefix as user metadata for diagnostic/verification purposes
        request.Metadata.Add(SourcePrefixMetaKey, key.SourcePrefix());

        await _client.PutObjectAsync(request, ct);
    }

    public bool WantsToStore(CacheKey key, long sizeBytes, CacheStoreReason reason)
    {
        // Cloud provider: accept everything offered to us.
        // The cascade handles gating (bloom filter, etc.)
        return reason switch
        {
            CacheStoreReason.FreshlyCreated => true,
            CacheStoreReason.Missed => true,
            CacheStoreReason.NotQueried => false, // Bloom says we probably have it
            _ => true
        };
    }

    public async ValueTask<bool> InvalidateAsync(CacheKey key, CancellationToken ct = default)
    {
        try
        {
            await _client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucket,
                Key = ObjectKey(key)
            }, ct);
            return true; // S3 DeleteObject is idempotent, no way to know if it existed
        }
        catch (AmazonS3Exception)
        {
            return false;
        }
    }

    public async ValueTask<int> PurgeBySourceAsync(ReadOnlyMemory<byte> sourceHash, CancellationToken ct = default)
    {
        var prefix = SourceObjectPrefix(sourceHash);
        int count = 0;
        string? continuationToken = null;

        do
        {
            var listResponse = await _client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken
            }, ct);

            if (listResponse.S3Objects.Count == 0)
                break;

            // Batch delete (up to 1000 per request, which is the S3 limit)
            var deleteRequest = new DeleteObjectsRequest
            {
                BucketName = _bucket,
                Objects = listResponse.S3Objects
                    .ConvertAll(obj => new KeyVersion { Key = obj.Key })
            };

            await _client.DeleteObjectsAsync(deleteRequest, ct);
            count += listResponse.S3Objects.Count;
            continuationToken = listResponse.NextContinuationToken;
        } while (continuationToken != null);

        return count;
    }

    public bool ProbablyContains(CacheKey key)
    {
        // Cloud provider — the cascade's bloom filter handles gating.
        // Return true so the cascade will try to fetch when bloom says "maybe".
        return true;
    }

    public async ValueTask<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            // ListObjectsV2 with MaxKeys=1 verifies bucket access without mutation
            await _client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucket,
                MaxKeys = 1
            }, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string HexFromHash(ReadOnlyMemory<byte> hash)
    {
#if NET8_0_OR_GREATER
        return Convert.ToHexString(hash.Span).ToLowerInvariant();
#else
        var span = hash.Span;
        var sb = new System.Text.StringBuilder(span.Length * 2);
        for (int i = 0; i < span.Length; i++)
        {
            sb.Append(span[i].ToString("x2"));
        }
        return sb.ToString();
#endif
    }
}
