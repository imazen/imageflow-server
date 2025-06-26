using System;
using System.IO;
using Amazon.S3.Model;
using Imazen.Common.Storage;

namespace Imageflow.Server.Storage.S3
{
    internal class S3Blob : IBlobData, IDisposable
    {
        private readonly GetObjectResponse response;

        internal S3Blob(GetObjectResponse r)
        {
            response = r;
        }

        public bool? Exists => true;
        public DateTime? LastModifiedDateUtc => response.LastModified.ToUniversalTime();

        public DateTimeOffset? EstimatedExpiry => response.Expiration?.ExpiryDateUtc;


        public Stream OpenRead()
        {
            return response.ResponseStream;
        }

        public void Dispose()
        {
            response?.Dispose();
        }
    }
}
