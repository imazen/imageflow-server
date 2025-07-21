using System.Buffers;
using System.Collections.Immutable;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Resulting;
using Imazen.Routing.Helpers;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Promises;
using Imazen.Routing.Requests;
using Imazen.Abstractions.Logging;

namespace Imazen.Routing.Layers;
public interface IPathMapping : IStringAndComparison
{
    string VirtualPath { get; }
    string PhysicalPath { get; }
    bool IgnorePrefixCase { get; }
    
    // TODO: when one of these is mapping / to /wwwroot, we need some preconditions to 
    // prevent all file requests from being routed to it.
    
    // But we also don't want to be serving .config files if it's NOT root. 
}

public record PathMapping(string VirtualPath, string PhysicalPath, bool IgnorePrefixCase = false) : IPathMapping
{
    public string StringToCompare => VirtualPath;
    public StringComparison StringComparison => IgnorePrefixCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

public class PathMapper
{
    public record struct PathMapperResult(string MappedPhysicalPath, IPathMapping MappingUsed);
    public IReadOnlyList<IPathMapping> PathMappings { get; }
    public IFastCond? FastPreconditions { get; }
    public PathMapper(IEnumerable<IPathMapping> pathMappings)
    {
        var list = pathMappings.ToList(); 
        // We want to compare the longest prefixes first so they match in case of collisions
        list.Sort((a, b) => b.VirtualPath.Length.CompareTo(a.VirtualPath.Length));
        PathMappings = list;
        
        // ReSharper disable twice ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (PathMappings.Any(m => m.PhysicalPath == null || m.VirtualPath == null))
        {
            throw new ArgumentException("Path mappings must have both a virtual and physical path");
        }
        // if any is simply '/', replace the condition with Tru
        FastPreconditions = PathMappings.Any(m => m.VirtualPath == "/") ? Conditions.True : Conditions.HasPathPrefix(PathMappings);
    }

    public PathMapperResult? TryMapVirtualPath(string path)
    {
        foreach (var mapping in PathMappings)
        {
            if (!path.StartsWith(mapping.VirtualPath, mapping.StringComparison)) continue;
            
            var relativePath = path
                .Substring(mapping.VirtualPath.Length)
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);

            var physicalDir = Path.GetFullPath(mapping.PhysicalPath.TrimEnd(Path.DirectorySeparatorChar));

            var physicalPath = Path.GetFullPath(Path.Combine(
                physicalDir,
                relativePath));
            if (!physicalPath.StartsWith(physicalDir, StringComparison.Ordinal))
            {
                return null; //We stopped a directory traversal attack (most likely)
            }
            return new PathMapperResult(physicalPath, mapping);
        }
        return null;
    }
}
public class LocalFilesLayer(IEnumerable<IPathMapping> pathMappings, IReLogger logger) : PathMapper(pathMappings), IRoutingLayer
{
    public string Name => "LocalFiles";

    public ValueTask<CodeResult<IRoutingEndpoint>?> ApplyRouting(MutableRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = TryMapVirtualPath(request.Path);
        if (result == null) return new ValueTask<CodeResult<IRoutingEndpoint>?>((CodeResult<IRoutingEndpoint>?)null);
        var (mappedPhysicalPath, mappingUsed) = result.Value;

        var lastWriteTimeUtc = File.GetLastWriteTimeUtc(mappedPhysicalPath);
        if (lastWriteTimeUtc.Year == 1601) // file doesn't exist, pass to next middleware
        {
            return new ValueTask<CodeResult<IRoutingEndpoint>?>((CodeResult<IRoutingEndpoint>?)null);
        }
        var latencyZone = new LatencyTrackingZone(mappingUsed.PhysicalPath, 10);
        var loggerForPath = logger.WithReScopeData("requestPath", request.Path);
        return Tasks.ValueResult<CodeResult<IRoutingEndpoint>?>(CodeResult<IRoutingEndpoint>.Ok(
            new PromiseWrappingEndpoint(
                new FilePromise(request.ToSnapshot(true), mappedPhysicalPath, latencyZone, lastWriteTimeUtc, loggerForPath))));
    
    }

    /// ToString includes all data in the layer, for full diagnostic transparency, and lists the preconditions and data count
    public override string ToString()
    {
        // Include all mappings and preconditions, if any
        var mappingPairs = PathMappings.Select(m => $"  {m.VirtualPath}{(m.IgnorePrefixCase ? @"(i)" : @"")} => {m.PhysicalPath}").ToArray();
        return $"Routing Layer {Name}: {PathMappings.Count} mappings, Preconditions: {FastPreconditions}\n{string.Join("\n", mappingPairs)}";
    }
  
   

}

 internal record FilePromise(IRequestSnapshot FinalRequest, string PhysicalPath,LatencyTrackingZone LatencyZone, DateTime LastWriteTimeUtc, IReLogger LoggerForPath): CacheableBlobPromiseBase(FinalRequest, LatencyZone, LoggerForPath)
    {
        
        public override void WriteCacheKeyBasisPairsToRecursive(IBufferWriter<byte> writer)
        {
            base.WriteCacheKeyBasisPairsToRecursive(writer);
            // We don't write the physical path since it might change based on deployment. The relative path and date are sufficient.
            writer.WriteLong(LastWriteTimeUtc.ToBinary());
        }
        public override ValueTask<CodeResult<IBlobWrapper>> TryGetBlobAsync(IRequestSnapshot request, IBlobRequestRouter router, IBlobPromisePipeline pipeline,
            CancellationToken cancellationToken = default)
        {
            return Tasks.ValueResult(CodeResult<IBlobWrapper>.Ok(new BlobWrapper(LatencyZone, PhysicalFileBlobHelper.CreateConsumableBlob(PhysicalPath, LastWriteTimeUtc, LoggerForPath))));
        }
    }

    internal abstract record CacheableBlobPromiseBase(IRequestSnapshot FinalRequest, LatencyTrackingZone? LatencyZone, IReLogger LoggerForPath) : ICacheableBlobPromise
    {
        public bool IsCacheSupporting => true;

        public async ValueTask<IAdaptableHttpResponse> CreateResponseAsync(IRequestSnapshot request, IBlobRequestRouter router, IBlobPromisePipeline pipeline,
            CancellationToken cancellationToken = default)
        {
            return new BlobResponse(await TryGetBlobAsync(request, router, pipeline, cancellationToken)).AsResponse();
        }
        

        public bool HasDependencies => false;
        public bool ReadyToWriteCacheKeyBasisData => true;
        public ValueTask<CodeResult> RouteDependenciesAsync(IBlobRequestRouter router, CancellationToken cancellationToken = default)
        {
            return Tasks.ValueResult(CodeResult.Ok());
        }

        public virtual void WriteCacheKeyBasisPairsToRecursive(IBufferWriter<byte> writer)
        {
            FinalRequest.WriteCacheKeyBasisPairsTo(writer);
        }

        private byte[]? cacheKey32Bytes = null;
        public byte[] GetCacheKey32Bytes()
        {
            return cacheKey32Bytes ??= this.GetCacheKey32BytesUncached();
        }

        public virtual bool SupportsPreSignedUrls => false;

        public abstract ValueTask<CodeResult<IBlobWrapper>> TryGetBlobAsync(IRequestSnapshot request,
            IBlobRequestRouter router, IBlobPromisePipeline pipeline,
            CancellationToken cancellationToken = default);
    }