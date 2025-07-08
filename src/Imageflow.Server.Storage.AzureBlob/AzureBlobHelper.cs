using System;
using System.IO;
using System.Linq;
using Azure.Storage.Blobs.Models;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Logging;

namespace Imageflow.Server.Storage.AzureBlob
{
    internal static class AzureBlobHelper
    {
        internal static StreamBlob CreateConsumableBlob(AzureBlobStorageReference reference, BlobDownloadStreamingResult r, IReLogger? logger)
        {
            // metadata starting with t_ is a tag

            var attributes = new BlobAttributes()
            {
                EstimatedBlobByteCount = r.Content.CanSeek ? r.Content.Length : null,
                ContentType = r.Details.ContentType,
                Etag = r.Details.ETag.ToString(),
                LastModifiedDateUtc = r.Details.LastModified.UtcDateTime,
                BlobStorageReference = reference,
                StorageTags = r.Details.Metadata.Where(kvp => kvp.Key.StartsWith("t_"))
                    .Select(kvp => SearchableBlobTag.CreateUnvalidated(kvp.Key.Substring(2), kvp.Value)).ToList()
            };
            return new StreamBlob(attributes, r.Content, logger?.WithReScopeData("azure",reference.GetFullyQualifiedRepresentation()),r );
        }
    }
}