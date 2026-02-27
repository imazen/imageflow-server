using Imazen.Common.Extensibility.Support;

namespace Imazen.Abstractions.Blobs;

// TODO, maybe move to IBlobAttributes for delegation?

public interface IBlobAttributes : IEstimateAllocatedBytesRecursive
{
    string? Etag { get; }
    string? ContentType { get; }
    
    long? EstimatedBlobByteCount { get; }

    public DateTimeOffset? LastModifiedDateUtc { get; }

    /// <summary>
    /// Tags to apply when caching this blob or auditing access (in addition to those specified in the originating request).
    /// </summary>
    IReadOnlyList<SearchableBlobTag>? StorageTags { get; }

    /// <summary>
    /// Useful if your blob storage provider requires manual renewal of blobs to keep them from being evicted. Use with IBlobCacheWithRenewals.
    /// </summary>
    DateTimeOffset? EstimatedExpiry { get; }

    // intrinsic metadata, 
    // custom metadata (e.g. EXIF)

    // Task<IBlobMetadata> GetMetadataAsync()

    
    IBlobStorageReference? BlobStorageReference { get; }

}

public record class BlobAttributes : IBlobAttributes
{
    public string? Etag { get; init; }
    public string? ContentType { get; init; }
    
    public long? EstimatedBlobByteCount { get; init; }
    public DateTimeOffset? LastModifiedDateUtc { get; init; }
    public IReadOnlyList<SearchableBlobTag>? StorageTags { get; init; }
    public DateTimeOffset? EstimatedExpiry { get; init; }
    
    public IBlobStorageReference? BlobStorageReference { get; set; }
    
    public int EstimateAllocatedBytesRecursive =>
        Etag.EstimateMemorySize(true) 
        + ContentType.EstimateMemorySize(true) 
        + EstimatedBlobByteCount.EstimateMemorySize(true)
        + LastModifiedDateUtc.EstimateMemorySize(true)
        + EstimatedExpiry.EstimateMemorySize(true)
        + 8 + (BlobStorageReference?.EstimateAllocatedBytesRecursive ?? 0)
        + StorageTags.EstimateMemorySize(true);
    
}