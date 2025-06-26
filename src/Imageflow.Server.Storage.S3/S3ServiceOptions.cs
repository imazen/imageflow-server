using System;
using System.Collections.Generic;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;

namespace Imageflow.Server.Storage.S3
{
    public class S3ServiceOptions
    {
        internal readonly List<PrefixMapping> Mappings = new List<PrefixMapping>();

        public S3ServiceOptions MapPrefix(string prefix, string bucket)
            => MapPrefix(prefix, bucket, "");

        public S3ServiceOptions MapPrefix(string prefix, string bucket, bool ignorePrefixCase,
            bool lowercaseBlobPath)
            => MapPrefix(prefix, bucket, "", ignorePrefixCase, lowercaseBlobPath);
        public S3ServiceOptions MapPrefix(string prefix, string bucket, string blobPrefix)
            => MapPrefix(prefix, bucket, blobPrefix, false, false);


        /// <summary>
        /// Maps a given prefix to a specified location within a bucket
        /// </summary>
        /// <param name="prefix">The prefix to capture image requests within</param>
        /// <param name="s3Config">The configuration (such as region endpoint or service URL, etc) to use</param>
        /// <param name="bucket">The bucket to serve images from</param>
        /// <param name="blobPrefix">The path within the bucket to serve images from. Can be an empty string to serve
        /// from root of bucket.</param>
        /// <param name="ignorePrefixCase">Whether to be cases sensitive about requests matching 'prefix'</param>
        /// <param name="lowercaseBlobPath">Whether to lowercase all incoming paths to allow for case insensitivity
        /// (requires that actual blobs all be lowercase).</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public S3ServiceOptions MapPrefix(string prefix, string bucket, string blobPrefix,
            bool ignorePrefixCase, bool lowercaseBlobPath)
            => MapPrefix(prefix, null, bucket, blobPrefix, ignorePrefixCase, lowercaseBlobPath);

        /// <summary>
        /// Maps a given prefix to a specified location within a bucket
        /// </summary>
        /// <param name="prefix">The prefix to capture image requests within</param>
        /// <param name="s3Client">The S3 client to use</param>
        /// <param name="bucket">The bucket to serve images from</param>
        /// <param name="blobPrefix">The path within the bucket to serve images from. Can be an empty string to serve
        /// from root of bucket.</param>
        /// <param name="ignorePrefixCase">Whether to be cases sensitive about requests matching 'prefix'</param>
        /// <param name="lowercaseBlobPath">Whether to lowercase all incoming paths to allow for case insensitivity
        /// (requires that actual blobs all be lowercase).</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public S3ServiceOptions MapPrefix(string prefix, IAmazonS3 s3Client, string bucket, string blobPrefix, bool ignorePrefixCase, bool lowercaseBlobPath)
        {
            prefix = prefix.TrimStart('/').TrimEnd('/');
            if (prefix.Length == 0)
            {
                throw new ArgumentException("Prefix cannot be /", nameof(prefix));
            }

            prefix = '/' + prefix + '/';
            blobPrefix = blobPrefix.Trim('/');

            Mappings.Add(new PrefixMapping
            {
                Bucket = bucket,
                Prefix = prefix,
                S3Client = s3Client,
                BlobPrefix = blobPrefix,
                IgnorePrefixCase = ignorePrefixCase,
                LowercaseBlobPath = lowercaseBlobPath

            });
            return this;
        }


    }
}
