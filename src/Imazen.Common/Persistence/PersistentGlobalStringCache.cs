using System.Text;
using System.Collections.Concurrent;
using Imazen.Common.Issues;
using System.Security.Cryptography;
using System.Globalization;

namespace Imazen.Common.Persistence
{
    /// <summary>
    /// Provides a disk-persisted (hopefully, if it can successfully write/read) cache for a tiny number of keys. (one file per key/value). 
    /// Provides no consistency or guarantees whatsoever. You hope something gets written to disk, and that it can be read after app reboot. In the meantime you have a ConcurrentDictionary that doesn't sync to disk. 
    /// Errors reported via IIssueProvider, not exceptions.
    /// Designed for license files
    /// </summary>
    class WriteThroughCache : IIssueProvider
    {
        private readonly string prefix = "resizer_key_";
        private readonly string sinkSource = "LicenseCache";
        private readonly string dataKind = "license";


        private readonly IIssueReceiver sink;
        private readonly MultiFolderStorage store;
        private readonly ConcurrentDictionary<string, string?> cache = new();
        
        /// <summary>
        /// Here's how the original version calculated candidateFolders.
        ///
        /// <code>
        /// var appPath = System.Web.Hosting.HostingEnvironment.ApplicationPhysicalPath;
        /// var candidates = ((appPath != null) ? 
        ///    new string[] { Path.Combine(appPath, "imagecache"),
        ///        Path.Combine(appPath, "App_Data"), Path.GetTempPath() } 
        ///    : new string[] { Path.GetTempPath() }).ToArray();
        /// </code>
        /// </summary>
        /// <param name="keyPrefix"></param>
        /// <param name="candidateFolders"></param>
        internal WriteThroughCache(string? keyPrefix, string[] candidateFolders)
        {
            prefix = keyPrefix ?? prefix;
            sink = new IssueSink(sinkSource);
            store = new MultiFolderStorage(sinkSource, dataKind, sink, candidateFolders, FolderOptions.Default);
        }
        
        string HashToBase16(string data)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(new UTF8Encoding().GetBytes(data));
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x", NumberFormatInfo.InvariantInfo).PadLeft(2, '0'));
            return sb.ToString();
        }


        string FilenameKeyFor(string key)
        {
            if (key.Any(c => !Char.IsLetterOrDigit(c) && c != '_') || key.Length + prefix.Length > 200)
            {
                return this.prefix + HashToBase16(key) + ".txt";
            }
            else
            {
                return this.prefix + key + ".txt";
            }
        }

        
        /// <summary>
        /// Write-through mem cache
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal StringCachePutResult TryPut(string key, string? value)
        {
            if (cache.TryGetValue(key, out var current) && current == value)
            {
                return StringCachePutResult.Duplicate;
            }
            cache[key] = value;
            return store.TryDiskWrite(FilenameKeyFor(key), value) ? StringCachePutResult.WriteComplete : StringCachePutResult.WriteFailed;
        }

        /// <summary>
        /// Read-through mem cache
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        internal string? TryGet(string key)
        {
            if (cache.TryGetValue(key, out var current))
            {
                return current;
            }
            var disk = store.TryDiskRead(FilenameKeyFor(key));
            if (disk != null)
            {
                cache[key] = disk;
            }
            return disk;
        }


        internal DateTime? GetWriteTimeUtc(string key)
        {
            return store.TryGetLastWriteTimeUtc(FilenameKeyFor(key));
        }

        public IEnumerable<IIssue> GetIssues()
        {
            return ((IIssueProvider)sink).GetIssues();
        }
    }

    /// <summary>
    /// Not for you. Don't use this. It creates a separate file for every key. Wraps a singleton
    /// </summary>
    internal class PersistentGlobalStringCache : IPersistentStringCache, IIssueProvider
    {
        private static WriteThroughCache? _processCache;


        private readonly WriteThroughCache cache;
        public PersistentGlobalStringCache(string keyPrefix, string[] candidateFolders)
        {
            _processCache ??= new WriteThroughCache(keyPrefix, candidateFolders);
            cache = _processCache;
        }

        public string? Get(string key)
        {
            return cache.TryGet(key);
        }

        public StringCachePutResult TryPut(string key, string? value)
        {
            return cache.TryPut(key, value);
        }

        public IEnumerable<IIssue> GetIssues()
        {
            return cache.GetIssues();
        }

        public DateTime? GetWriteTimeUtc(string key)
        {
            return cache.GetWriteTimeUtc(key);
        }
    }
}
