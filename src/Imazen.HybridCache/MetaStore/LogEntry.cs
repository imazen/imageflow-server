using System;

namespace Imazen.HybridCache.MetaStore
{
    internal enum LogEntryType: byte
    {
        Create = 0,
        Update = 1,
        Delete = 2
    }

    /// <summary>
    /// Returns a shared static string instance for known MIME types,
    /// so millions of cache records don't each hold a separate copy
    /// of "image/jpeg". Unknown types pass through as-is.
    /// </summary>
    internal static class ContentTypePool
    {
        internal static string Deduplicate(string contentType)
        {
            if (contentType == null) return null;
            switch (contentType)
            {
                // Images
                case "image/jpeg": return "image/jpeg";
                case "image/png": return "image/png";
                case "image/gif": return "image/gif";
                case "image/webp": return "image/webp";
                case "image/avif": return "image/avif";
                case "image/jxl": return "image/jxl";
                case "image/svg+xml": return "image/svg+xml";
                case "image/tiff": return "image/tiff";
                case "image/bmp": return "image/bmp";
                case "image/x-icon": return "image/x-icon";
                case "image/vnd.microsoft.icon": return "image/vnd.microsoft.icon";
                case "image/heic": return "image/heic";
                case "image/heif": return "image/heif";
                // Video
                case "video/mp4": return "video/mp4";
                case "video/webm": return "video/webm";
                case "video/ogg": return "video/ogg";
                case "video/quicktime": return "video/quicktime";
                // Audio
                case "audio/mpeg": return "audio/mpeg";
                case "audio/ogg": return "audio/ogg";
                case "audio/wav": return "audio/wav";
                case "audio/webm": return "audio/webm";
                case "audio/aac": return "audio/aac";
                case "audio/flac": return "audio/flac";
                // Documents
                case "application/pdf": return "application/pdf";
                case "application/json": return "application/json";
                case "application/xml": return "application/xml";
                case "application/zip": return "application/zip";
                case "application/gzip": return "application/gzip";
                case "application/octet-stream": return "application/octet-stream";
                case "application/javascript": return "application/javascript";
                case "application/wasm": return "application/wasm";
                // Text
                case "text/html": return "text/html";
                case "text/css": return "text/css";
                case "text/plain": return "text/plain";
                case "text/xml": return "text/xml";
                case "text/javascript": return "text/javascript";
                case "text/csv": return "text/csv";
                // Fonts
                case "font/woff": return "font/woff";
                case "font/woff2": return "font/woff2";
                case "font/ttf": return "font/ttf";
                case "font/otf": return "font/otf";
                case "application/font-woff": return "application/font-woff";
                case "application/font-woff2": return "application/font-woff2";
                default: return contentType;
            }
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