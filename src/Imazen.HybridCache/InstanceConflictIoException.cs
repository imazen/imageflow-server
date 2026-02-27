using System;
using System.IO;

namespace Imazen.HybridCache
{
    /// <summary>
    /// Thrown when multiple HybridCache instances attempt to write to the same database directory,
    /// which is not supported.
    /// </summary>
    public class HybridCacheInstanceConflictException : IOException
    {
        public HybridCacheInstanceConflictException(string message, string path, int shardId, IOException innerException)
            : base(message, innerException)
        {
            ConflictPath = path;
            ShardId = shardId;
        }

        public string ConflictPath { get; }
        public int ShardId { get; }

        public override string ToString()
        {
            return $"{Message} Path: {ConflictPath} ShardId: {ShardId} Inner: {InnerException}";
        }
    }
}
