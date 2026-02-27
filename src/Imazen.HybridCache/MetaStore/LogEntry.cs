using System;
using System.Collections.Concurrent;

namespace Imazen.HybridCache.MetaStore
{
    internal enum LogEntryType: byte
    {
        Create = 0,
        Update = 1,
        Delete = 2
    }

    /// <summary>
    /// Bounded string pool for deduplicating content types across cache records.
    /// Once the pool reaches its capacity, new strings pass through without pooling.
    /// This avoids the unbounded memory growth of string.Intern while still saving
    /// ~45 bytes per record for the common MIME types (image/jpeg, image/png, etc.).
    /// </summary>
    internal static class ContentTypePool
    {
        private static readonly ConcurrentDictionary<string, string> Pool = new ConcurrentDictionary<string, string>();
        private const int MaxPoolSize = 128;

        internal static string Deduplicate(string contentType)
        {
            if (contentType == null) return null;
            if (Pool.TryGetValue(contentType, out var existing))
                return existing;
            if (Pool.Count >= MaxPoolSize)
                return contentType; // Stop pooling, don't grow unbounded
            return Pool.GetOrAdd(contentType, contentType);
        }
    }

    internal struct LogEntry
    {
        internal LogEntryType EntryType;
        internal int AccessCountKey;
        internal DateTime CreatedAt;
        internal DateTime LastDeletionAttempt;
        internal long DiskSize;
        internal string RelativePath;
        internal string ContentType;

        public LogEntry(LogEntryType entryType, ICacheDatabaseRecord record)
        {
            EntryType = entryType;
            AccessCountKey = record.AccessCountKey;
            ContentType = record.ContentType;
            RelativePath = record.RelativePath;
            DiskSize = record.DiskSize;
            LastDeletionAttempt = record.LastDeletionAttempt;
            CreatedAt = record.CreatedAt;
        }

        public CacheDatabaseRecord ToRecord()
        {
            return new CacheDatabaseRecord()
            {
                AccessCountKey = AccessCountKey,
                ContentType = ContentTypePool.Deduplicate(ContentType),
                CreatedAt = CreatedAt,
                DiskSize = DiskSize,
                LastDeletionAttempt = LastDeletionAttempt,
                RelativePath = RelativePath
            };
        }
    }
}