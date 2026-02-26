using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Imazen.Caching;

namespace Imageflow.Server.Storage.AzureBlob;

/// <summary>
/// Adapts Azure Blob Storage to the ICacheProvider interface for use as a cloud tier in CacheCascade.
///
/// Object layout: {prefix}{CacheKey.ToStoragePath()} → e.g. "cache/ab12/ab12...f3/de45...89"
/// Tags: source_prefix stored as a blob index tag, enabling native tag-based purge via FindBlobsByTagsAsync.
///
/// No eviction logic — configure Azure Blob Lifecycle Management policies to auto-expire stale cached objects.
/// PurgeBySourceAsync uses Azure's native blob index tag query for efficient purge-by-source.
/// </summary>
public sealed class AzureBlobCacheProvider : ICacheProvider
{
    private readonly BlobContainerClient _container;
    private readonly string _prefix;

    /// <summary>
    /// Tag key for source prefix, used by Azure's native tag-based query (FindBlobsByTagsAsync).
    /// </summary>
    private const string SourcePrefixTagKey = "source_prefix";

    public string Name { get; }
    public CacheProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Create an Azure Blob Storage cache provider.
    /// </summary>
    /// <param name="name">Provider name for cascade identification.</param>
    /// <param name="container">Azure blob container client (caller owns lifecycle).</param>
    /// <param name="prefix">Optional blob name prefix (e.g., "cache/"). Include trailing slash if desired.</param>
    public AzureBlobCacheProvider(string name, BlobContainerClient container, string? prefix = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _prefix = prefix ?? "";

        var zone = $"azure:{container.AccountName}:{container.Name}";
        Capabilities = new CacheProviderCapabilities
        {
            RequiresInlineExecution = false,
            LatencyZone = zone
        };
    }

    private string BlobName(CacheKey key) => _prefix + key.ToStoragePath();

    public async ValueTask<CacheFetchResult?> FetchAsync(CacheKey key, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(BlobName(key));
        try
        {
            var response = await blob.DownloadStreamingAsync(new BlobDownloadOptions(), ct);
            var details = response.Value.Details;

            var metadata = new CacheEntryMetadata
            {
                ContentType = details.ContentType,
                ContentLength = details.ContentLength
            };
            return new CacheFetchResult(response.Value.Content, metadata);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async ValueTask StoreAsync(CacheKey key, byte[] data, CacheEntryMetadata metadata,
        CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(BlobName(key));
        using var stream = new MemoryStream(data, writable: false);

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = metadata.ContentType ?? "application/octet-stream"
            },
            Tags = new Dictionary<string, string>
            {
                [SourcePrefixTagKey] = key.SourcePrefix()
            }
        };

        await blob.UploadAsync(stream, options, ct);
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
        var blob = _container.GetBlobClient(BlobName(key));
        try
        {
            var response = await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, null, ct);
            return response.Value; // true if the blob existed and was deleted
        }
        catch (RequestFailedException)
        {
            return false;
        }
    }

    public async ValueTask<int> PurgeBySourceAsync(ReadOnlyMemory<byte> sourceHash, CancellationToken ct = default)
    {
        var sourcePrefix = HexFromHash(sourceHash);
        var tagQuery = $"\"{SourcePrefixTagKey}\"='{sourcePrefix}'";

        int count = 0;
        await foreach (var item in _container.FindBlobsByTagsAsync(tagQuery, ct))
        {
            var blob = _container.GetBlobClient(item.BlobName);
            try
            {
                await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, null, ct);
                count++;
            }
            catch (RequestFailedException)
            {
                // Best-effort deletion; continue with remaining blobs
            }
        }
        return count;
    }

    public bool ProbablyContains(CacheKey key)
    {
        // Cloud provider — the cascade's bloom filter handles gating.
        return true;
    }

    public async ValueTask<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ExistsAsync(ct);
            return response.Value;
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
