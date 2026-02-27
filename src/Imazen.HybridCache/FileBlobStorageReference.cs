using Imazen.Abstractions.Blobs;
using Imazen.Common.Extensibility.Support;

namespace Imazen.HybridCache
{
    internal class FileBlobStorageReference : IBlobStorageReference
    {
        public FileBlobStorageReference(string relativePath, HashBasedPathBuilder pathBuilder, ICacheDatabaseRecord? record)
        {
            Record = record;
            RelativePath = relativePath;
            PathBuilder = pathBuilder;
        }

        // Record
        private ICacheDatabaseRecord? Record { get; set; }

        internal string RelativePath { get; set; }
        
        private HashBasedPathBuilder PathBuilder { get; set; }
        
        public int EstimateAllocatedBytesRecursive =>
            RelativePath.EstimateMemorySize(true) +
            24 + 8 +
            (Record?.EstimateAllocatedBytesRecursive ?? 0);
            
    
        public string GetFullyQualifiedRepresentation()
        {
            return PathBuilder.GetPhysicalPathFromRelativePath(RelativePath);
        }

    }
}