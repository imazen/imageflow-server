using Imazen.Common.Extensibility.Support;

namespace Imazen.HybridCache
{
    internal readonly struct CacheEntry
    {
        
        private CacheEntry(byte[] hash, string relativePath, string physicalPath, string hashString)
        {
            Hash = hash;
            RelativePath = relativePath;
            PhysicalPath = physicalPath;
            HashString = hashString;
        }

        public byte[] Hash { get; }
        public string PhysicalPath { get; }
        public string HashString { get; }
        
        public string RelativePath { get; }
        
        internal static CacheEntry FromHash(byte[] hash, string hashString, HashBasedPathBuilder builder)
        {
            var relative = builder.GetRelativePathFromHash(hash);
            return new CacheEntry(hash, relative, builder.GetPhysicalPathFromRelativePath(relative), hashString);
        }
        
    }
}