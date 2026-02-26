using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Abstractions.Blobs;
using Imazen.Caching;
using Imazen.Common.Concurrency;
using Imazen.Common.Extensibility.Support;
using Imazen.HybridCache.MetaStore;

namespace Imazen.HybridCache;

/// <summary>
/// Adapts a HybridCache instance (disk cache with LRU eviction) to the ICacheProvider interface
/// for use with CacheCascade.
///
/// FetchAsync returns a FileStream (not byte[]) for zero-copy streaming to clients.
/// StoreAsync writes to disk with space reservation and LRU eviction.
/// PurgeBySourceAsync uses HybridCache's tag-based search (source prefix stored as a tag).
/// </summary>
public class HybridCacheProvider : ICacheProvider
{
    private readonly HashBasedPathBuilder _pathBuilder;
    private readonly CacheFileWriter _fileWriter;
    private readonly ICacheCleanupManager _cleanupManager;
    private readonly ICacheDatabase<ICacheDatabaseRecord> _database;
    private readonly AsyncLockProvider _evictAndWriteLocks;

    /// <summary>
    /// Tag key used to store the source prefix for purge-by-source support.
    /// </summary>
    internal const string SourcePrefixTagKey = "source_prefix";

    public string Name { get; }
    public CacheProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Create from an existing HybridCache instance.
    /// The adapter shares all infrastructure (MetaStore, file writer, cleanup manager).
    /// </summary>
    public HybridCacheProvider(HybridCache cache, string? name = null)
    {
        if (cache == null) throw new ArgumentNullException(nameof(cache));

        var asyncCache = cache.AsyncCache;
        _pathBuilder = asyncCache.PathBuilder;
        _fileWriter = asyncCache.FileWriter;
        _cleanupManager = asyncCache.CleanupManager;
        _database = asyncCache.Database;
        _evictAndWriteLocks = asyncCache.EvictAndWriteLocks;

        Name = name ?? cache.UniqueName;
        Capabilities = new CacheProviderCapabilities
        {
            RequiresInlineExecution = false,
            LatencyZone = "local"
        };
    }

    /// <summary>
    /// Convert a CacheKey to a HybridCache CacheEntry.
    /// Concatenates SourceHash (16 bytes) + VariantHash (16 bytes) = 32 bytes,
    /// which matches HybridCache's expected SHA256 hash length.
    /// </summary>
    private CacheEntry ToCacheEntry(CacheKey key)
    {
        var hash = new byte[32];
        key.SourceHash.Span.CopyTo(hash.AsSpan(0, 16));
        key.VariantHash.Span.CopyTo(hash.AsSpan(16, 16));
        var hashString = HashBasedPathBuilder.GetStringFromHashStatic(hash);
        return CacheEntry.FromHash(hash, hashString, _pathBuilder);
    }

    public async ValueTask<CacheFetchResult?> FetchAsync(CacheKey key, CancellationToken ct = default)
    {
        var entry = ToCacheEntry(key);

        // Notify cleanup manager of access (updates LRU)
        _cleanupManager.NotifyUsed(entry);

        if (!File.Exists(entry.PhysicalPath))
            return null;

        try
        {
            var fs = new FileStream(entry.PhysicalPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);

            // Try to get content type from metadata store
            string? contentType = null;
            try
            {
                var record = await _cleanupManager.GetRecordReference(entry, ct);
                contentType = record?.ContentType;
            }
            catch
            {
                // Metadata lookup failure is non-fatal — we still have the file
            }

            var metadata = new CacheEntryMetadata
            {
                ContentType = contentType,
                ContentLength = fs.Length,
            };
            return new CacheFetchResult(fs, metadata);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            // File locked or other I/O error — treat as miss
            return null;
        }
    }

    public async ValueTask StoreAsync(CacheKey key, byte[] data, CacheEntryMetadata metadata,
        CancellationToken ct = default)
    {
        var entry = ToCacheEntry(key);
        var sourcePrefix = key.SourcePrefix();

        var record = new CacheDatabaseRecord
        {
            AccessCountKey = _cleanupManager.GetAccessCountKey(entry),
            // CreatedAt must be in the future for TryReserveSpace (it checks this)
            CreatedAt = DateTimeOffset.UtcNow.AddDays(1),
            LastDeletionAttempt = DateTime.MinValue,
            EstDiskSize = _cleanupManager.EstimateFileSizeOnDisk(data.Length),
            RelativePath = entry.RelativePath,
            ContentType = metadata.ContentType,
            Flags = CacheEntryFlags.Generated,
            Tags = new[] { SearchableBlobTag.CreateUnvalidated(SourcePrefixTagKey, sourcePrefix) }
        };

        // Reserve space (may trigger LRU eviction)
        var reserveResult = await _cleanupManager.TryReserveSpace(
            entry, record, allowEviction: true, _evictAndWriteLocks, ct);

        if (!reserveResult.Success)
            return; // Silently drop — eviction couldn't free enough space

        // Write the file
        var writeResult = await _fileWriter.TryWriteFile(entry,
            async (stream, ct2) =>
            {
#if NET8_0_OR_GREATER
                await stream.WriteAsync(data, ct2);
#else
                await stream.WriteAsync(data, 0, data.Length, ct2);
#endif
            },
            recheckFileSystemFirst: true,
            timeoutMs: 15000,
            ct);

        if (writeResult == CacheFileWriter.FileWriteStatus.FileCreated)
        {
            var capturedRecord = record;
            await _cleanupManager.MarkFileCreated(entry, DateTime.UtcNow, () => capturedRecord);
        }
    }

    public bool WantsToStore(CacheKey key, long sizeBytes, CacheStoreReason reason)
    {
        // Disk cache accepts freshly created and missed entries.
        // NotQueried: we're local, so if we weren't queried it means a faster
        // local tier (memory) hit first — we probably already have it on disk.
        return reason switch
        {
            CacheStoreReason.FreshlyCreated => true,
            CacheStoreReason.Missed => true,
            CacheStoreReason.NotQueried => false,
            _ => true
        };
    }

    public async ValueTask<bool> InvalidateAsync(CacheKey key, CancellationToken ct = default)
    {
        var entry = ToCacheEntry(key);
        var result = await _cleanupManager.CacheDelete(entry.RelativePath, _evictAndWriteLocks, ct);
        return result.IsOk;
    }

    public async ValueTask<int> PurgeBySourceAsync(ReadOnlyMemory<byte> sourceHash, CancellationToken ct = default)
    {
        // Build the source prefix hex string from the raw hash
        var sourcePrefix = SourceHashToPrefix(sourceHash);
        var tag = SearchableBlobTag.CreateUnvalidated(SourcePrefixTagKey, sourcePrefix);

        var purgeResult = await _cleanupManager.CachePurgeByTag(tag, _evictAndWriteLocks, ct);
        if (!purgeResult.IsOk || purgeResult.Value == null) return 0;

        int count = 0;
        await foreach (var _ in purgeResult.Value)
        {
            count++;
        }
        return count;
    }

    /// <summary>
    /// Convert raw source hash bytes to the hex prefix string.
    /// Must match CacheKey.SourcePrefix() output format.
    /// </summary>
    private static string SourceHashToPrefix(ReadOnlyMemory<byte> sourceHash)
    {
#if NET8_0_OR_GREATER
        return Convert.ToHexString(sourceHash.Span).ToLowerInvariant();
#else
        var span = sourceHash.Span;
        var sb = new System.Text.StringBuilder(span.Length * 2);
        for (int i = 0; i < span.Length; i++)
        {
            sb.Append(span[i].ToString("x2"));
        }
        return sb.ToString();
#endif
    }

    public bool ProbablyContains(CacheKey key)
    {
        // Local disk provider — always return true.
        // The cascade's bloom filter handles cloud gating; local providers are always checked.
        return true;
    }

    public async ValueTask<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            var rootResult = await _database.TestRootDirectory();
            if (!rootResult.IsOk) return false;

            var metaResult = await _database.TestMetaStore();
            return metaResult.IsOk;
        }
        catch
        {
            return false;
        }
    }
}
