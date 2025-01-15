using System.Buffers;
using System.Text;
using Imazen.Abstractions;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Blobs.LegacyProviders;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Storage;
using Imazen.Routing.Helpers;
using Imazen.Routing.Promises;
using Imazen.Routing.Requests;
using BlobMissingException = Imazen.Abstractions.Blobs.LegacyProviders.BlobMissingException;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Imazen.Routing.Layers;

public class BlobProvidersLayer : IRoutingLayer
{
    public BlobProvidersLayer(IEnumerable<IBlobProvider>? blobProviders, IEnumerable<IBlobWrapperProvider>? blobWrapperProviders)
    {
        if (blobProviders == null) blobProviders = Array.Empty<IBlobProvider>();
        foreach (var provider in blobProviders)
        {
            this.blobProviders.Add(provider);
            CheckAndAddPrefixes(provider.GetPrefixes(), provider);
        }
        if (blobWrapperProviders == null) blobWrapperProviders = Array.Empty<IBlobWrapperProvider>();
        foreach (var provider in blobWrapperProviders)
        {
            this.blobWrapperProviders.Add(provider);
            if (provider is IBlobWrapperProviderZoned zoned)
            {
                CheckAndAddPrefixes(zoned.GetPrefixesAndZones(), provider);
            }
            else
            {
                CheckAndAddPrefixes(provider.GetPrefixes(), provider);
            }
        }

        FastPreconditions = Conditions.HasPathPrefixOrdinalIgnoreCase(blobPrefixes.Select(p => p.Prefix).ToArray());
    }

    private void CheckAndAddPrefixes(IEnumerable<string> prefix, object provider)
    {
        foreach (var p in prefix)
        {
            CheckAndAddPrefix(p, provider);
        }
    }
    private void CheckAndAddPrefixes(IEnumerable<BlobWrapperPrefixZone> prefix, object provider)
    {
        foreach (var p in prefix)
        {
            CheckAndAddPrefix(p.Prefix, provider, p.LatencyZone);
        }
    }
    private void CheckAndAddPrefix(string prefix, object provider, LatencyTrackingZone? zone = null)
    {
        var conflictingPrefix =
            blobPrefixes.FirstOrDefault(p =>
                prefix.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase) ||
                p.Prefix.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (conflictingPrefix != default)
        {
            throw new InvalidOperationException(
                $"Blob Provider failure: Prefix {{prefix}} conflicts with prefix {conflictingPrefix}");
        }

        // We don't check for conflicts with PathMappings because / is a path mapping usually, 
        // and we simply prefer blobs over files if there are overlapping prefixes.
        
        blobPrefixes.Add(new BlobPrefix(prefix, provider, zone ?? GetZoneFor(provider, prefix)));
    }
    
    private record struct BlobPrefix(string Prefix, object Provider, LatencyTrackingZone LatencyZone);
    private readonly List<IBlobProvider> blobProviders = new List<IBlobProvider>();
    private readonly List<IBlobWrapperProvider> blobWrapperProviders = new List<IBlobWrapperProvider>();
    private readonly List<BlobPrefix> blobPrefixes = new List<BlobPrefix>();


    public string Name => "BlobProviders";
    public IFastCond FastPreconditions { get; }

    private LatencyTrackingZone GetZoneFor(object provider, string prefix)
    {
        string? zoneName = null;
        if (provider is IUniqueNamed named)
        {
            zoneName = $"{named.UniqueName} ({provider.GetType().Name}) - '{prefix}'";
        }
        else
        {
            zoneName = $"({provider.GetType().Name}) - '{prefix}'";
        }
        return new LatencyTrackingZone(zoneName, 100);
    }
    
    public ValueTask<CodeResult<IRoutingEndpoint>?> ApplyRouting(MutableRequest request,
        CancellationToken cancellationToken = default)
    {
        
        foreach (var prefix in blobPrefixes)
        {
            if (request.Path.StartsWith(prefix.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                var latencyZone = prefix.LatencyZone;
                ICacheableBlobPromise? promise = null;
                if (prefix.Provider is IBlobWrapperProvider wp && wp.SupportsPath(request.Path))
                {
                    promise = new BlobWrapperProviderPromise(request.ToSnapshot(true), request.Path, wp, (wp as IBlobWrapperProviderZoned)?.GetLatencyZone(request.Path) ?? latencyZone);
                }else if (prefix.Provider is IBlobProvider p && p.SupportsPath(request.Path))
                {
                    promise = new BlobProviderPromise(request.ToSnapshot(true), request.Path, p, latencyZone);
                }

                if (promise == null) break;
                return Tasks.ValueResult<CodeResult<IRoutingEndpoint>?>(CodeResult<IRoutingEndpoint>.Ok(
                    new PromiseWrappingEndpoint(
                        promise)));
            }
        }
        // Note: if GetPrefixes isn't good enough, stuff won't get routed to a blob wrapper. In the past, SupportsPath was always called
        return Tasks.ValueResult<CodeResult<IRoutingEndpoint>?>(null);
    }
    
    /// ToString includes all data in the layer, for full diagnostic transparency, and lists the preconditions and data count
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Routing Layer {Name}: {blobProviders.Count} IBlobProviders, {blobWrapperProviders.Count} IBlobWrapperProviders, PreConditions: {FastPreconditions}");
        sb.AppendLine("Registered prefixes:");
        object? lastProvider = null;
        foreach (var prefix in blobPrefixes)
        {
            // show which IBlobProvider or IBlobWrapperProvider registered this prefix (class name and tostring)
            var providerObject =  prefix.Provider;
            var providerClassName = providerObject?.GetType().Name;
            var providerUniqueName = (providerObject as IUniqueNamed)?.UniqueName;
            var providerToString = providerObject?.ToString();
            if (lastProvider == providerObject)
            {
                sb.AppendLine($"  {prefix}* -> same ^");
            }
            else
            {
                sb.AppendLine($"  {prefix}* -> {providerClassName} ('{providerUniqueName}') {providerToString}");
            }
            lastProvider = providerObject;
        }
        return sb.ToString();
    }
    
}

internal record BlobProviderPromise(IRequestSnapshot FinalRequest, String VirtualPath, IBlobProvider Provider, LatencyTrackingZone LatencyZone): LocalFilesLayer.CacheableBlobPromiseBase(FinalRequest, LatencyZone)
{
    public override void WriteCacheKeyBasisPairsToRecursive(IBufferWriter<byte> writer)
    {
        base.WriteCacheKeyBasisPairsToRecursive(writer);
        // We don't add anything, unless we later implement active invalidation.
    }
    public override async ValueTask<CodeResult<IBlobWrapper>> TryGetBlobAsync(IRequestSnapshot request, IBlobRequestRouter router, IBlobPromisePipeline pipeline,
        CancellationToken cancellationToken = default)
    {
        IBlobData blobData;
        try
        {
            blobData = await Provider.Fetch(VirtualPath);
        }
        catch (BlobMissingException e)
        {
            return CodeResult<IBlobWrapper>.Err((404, e.Message));
        }
        if (blobData.Exists == false) return CodeResult<IBlobWrapper>.Err((404, "Blob not found"));
            
        var attrs = new BlobAttributes()
        {
            LastModifiedDateUtc = blobData.LastModifiedDateUtc,
        };
        var stream = blobData.OpenRead();
        return CodeResult<IBlobWrapper>.Ok(new BlobWrapper(LatencyZone, new StreamBlob(attrs, stream, blobData)));
    }
}

internal record BlobWrapperProviderPromise(
    IRequestSnapshot FinalRequest,
    String VirtualPath,
    IBlobWrapperProvider Provider, LatencyTrackingZone LatencyZone) : LocalFilesLayer.CacheableBlobPromiseBase(FinalRequest, LatencyZone)
{
    public override async ValueTask<CodeResult<IBlobWrapper>> TryGetBlobAsync(IRequestSnapshot request,
        IBlobRequestRouter router, IBlobPromisePipeline pipeline,
        CancellationToken cancellationToken = default)
    {
        return await Provider.Fetch(VirtualPath);
    }
}
        