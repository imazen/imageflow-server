using System;
using System.IO;
using System.Linq;
using Amazon.S3.Model;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Logging;

namespace Imageflow.Server.Storage.S3
{
    internal static class S3BlobHelpers
    {
        public static StreamBlob CreateS3Blob(GetObjectResponse r, IReLogger logger)
        {
            if (r.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception($"S3 returned {r.HttpStatusCode} for {r.BucketName}/{r.Key}");
            }

            // metadata starting with t_ is a tag
            var tags = r.Metadata.Keys.Where(k => k.StartsWith("t_"))
                .Select(key => SearchableBlobTag.CreateUnvalidated(key.Substring(2), r.Metadata[key])).ToList();
            var a = new BlobAttributes()
            {
                EstimatedBlobByteCount = r.ContentLength,
                ContentType = r.Headers?.ContentType,
                Etag = r.ETag,
                LastModifiedDateUtc = r.LastModified.ToUniversalTime(),
                StorageTags = tags,
                EstimatedExpiry = r.Expiration?.ExpiryDateUtc,
                BlobStorageReference = new S3BlobStorageReference(r.BucketName, r.Key)
            };
            return new StreamBlob(a, r.ResponseStream, logger?.WithReScopeData("s3",a.BlobStorageReference.GetFullyQualifiedRepresentation()), r);
        }
    }
}