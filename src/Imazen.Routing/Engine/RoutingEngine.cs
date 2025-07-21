using System.Text;
using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Issues;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Layers;
using Imazen.Routing.Promises;
using Imazen.Routing.Requests;
using Imazen.Routing.Serving;
using Microsoft.Extensions.Logging;

namespace Imazen.Routing.Engine;

public class RoutingEngine : IRoutingEngine, IHasDiagnosticPageSection
{
    private readonly RoutingLayerGroup[] layerGroups;
    private readonly IReLogger logger;
    private readonly IFastCond mightHandleConditions;

    internal RoutingEngine(RoutingLayerGroup[] layerGroups, IReLogger logger)
    {
        this.layerGroups = layerGroups;
        this.logger = logger.WithSubcategory("RoutingEngine");
        
        // Build a precondition that is as specific as possible, so we can exit early if we know we can't handle the request.
        // This reduces allocations of wrappers etc.
        mightHandleConditions = layerGroups.Select(lg => lg.RecursiveComputedConditions).AnyPrecondition().Optimize();
    }

    public bool MightHandleRequest<TQ>(string path, TQ query) where TQ : IReadOnlyQueryWrapper
    {
        return mightHandleConditions.Matches(path, query);
    }

    public async ValueTask<CodeResult<IRoutingEndpoint>?> Route(MutableRequest request, CancellationToken cancellationToken = default)
    {
        // log info about the request
        foreach (var group in layerGroups)
        {
            if (!(group.GroupPrecondition?.Matches(request.MutablePath, request.ReadOnlyQueryWrapper) ??
                  true)) continue;
            
            foreach (var layer in group.Layers)
            {
                if (!(layer.FastPreconditions?.Matches(request.MutablePath, request.ReadOnlyQueryWrapper) ??
                      true)) continue;
                
                var result = await layer.ApplyRouting(request,cancellationToken);
                // log what (if anything) has changed.
                if (result != null)
                {
                    // log the result
                    return result;
                }

            }
        }
        return null; // We don't have matching routing for this. Let the rest of the app handle it.
    }
    

    /// <summary>
    /// Errors if the route isn't a cachable blob; returns null if there's no match.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async ValueTask<CodeResult<ICacheableBlobPromise>?> RouteToPromiseAsync(MutableRequest request, CancellationToken cancellationToken = default)
    {
        // log info about the request
        foreach (var group in layerGroups)
        {
            if (!(group.GroupPrecondition?.Matches(request.MutablePath, request.ReadOnlyQueryWrapper) ??
                  true)) continue;
            foreach (var layer in group.Layers)
            {
                if (!(layer.FastPreconditions?.Matches(request.MutablePath, request.ReadOnlyQueryWrapper) ??
                      true)) continue;
                var result = await layer.ApplyRouting(request, cancellationToken);
                // log what (if anything) has changed.
                if (result != null)
                {

                    if (result.IsOk)
                    {
                        var endpoint = result.Value;
                        if (endpoint!.IsBlobEndpoint)
                        {
                            var promise = await endpoint.GetInstantPromise(request, cancellationToken);
                            if (promise.IsCacheSupporting && promise is ICacheableBlobPromise cacheablePromise)
                            {
                                return CodeResult<ICacheableBlobPromise>.Ok(cacheablePromise);
                            }
                        }
                        else
                        {
                            logger.LogError(
                                "Imageflow Routing endpoint {endpoint} (from routing layer {layerName} is not a blob endpoint",
                                endpoint, layer.Name);
                            return CodeResult<ICacheableBlobPromise>.Err(
                                HttpStatus.ServerError.WithAppend("Routing endpoint is not a blob endpoint"));
                        }
                    }
                    else
                    {
                        return CodeResult<ICacheableBlobPromise>.Err(result.Error);
                    }
                }
            }
        }

        return null; // We don't have matching routing for this. Let the rest of the app handle it.
    }

    private static string FormatLayer(int groupIndex, RoutingLayerGroup group, int layerIndex)
    {
        var layer = group.Layers[layerIndex];
        return $"group[{groupIndex}]({group.Name}).layer[{layerIndex}]({layer.Name}): ({layer.GetType().Name}), Preconditions={layer.FastPreconditions}";
    
    }
    private static string FormatGroup(int groupIndex, RoutingLayerGroup group)
    {
        return $"group[{groupIndex}]({group.Name}): Layers: {group.Layers.Count}, Group Preconditions: {group.GroupPrecondition}";
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        var layersTotal = layerGroups.Sum(lg => lg.Layers.Count);
        sb.AppendLine($"Routing Engine: {layersTotal} layers in {layerGroups.Length} groups, Preconditions: {mightHandleConditions}");
        for(var i = 0; i < layerGroups.Length; i++)
        {
            sb.AppendLine(FormatGroup(i, layerGroups[i]));
            for (var j = 0; j < layerGroups[i].Layers.Count; j++)
            {
                sb.AppendLine(FormatLayer(i, layerGroups[i], j));
            }
        }
        var issueLines = new List<string>();
        var diagnosticLines = new List<string>();
        var issueCount = 0;
        for (var groupIndex = 0; groupIndex < layerGroups.Length; groupIndex++)
        {
            var group = layerGroups[groupIndex];
            for (var layerIndex = 0; layerIndex < group.Layers.Count; layerIndex++)
            {
                var layer = group.Layers[layerIndex];
                if (layer is IHasDiagnosticPageSection hasSection)
                {
                    // add layer name
                    diagnosticLines.Add($"\n{FormatLayer(groupIndex, group, layerIndex)} Diagnostics:");
                    diagnosticLines.Add(hasSection.GetDiagnosticsPageSection(DiagnosticsPageArea.Start) ?? "");
                }
                if (layer is IIssueProvider issueProvider)
                {
                    var issueList = issueProvider.GetIssues().ToList();
                    issueCount += issueList.Count;
                    issueLines.Add($"\n{FormatLayer(groupIndex, group, layerIndex)} Issues:");
                    issueLines.AddRange(issueList.Select(i => i.ToString() ?? ""));
                }
            }
        }
        if (issueLines.Count > 0)
        {
            sb.AppendLine(" \nRouting Issues:");
            sb.AppendLine(string.Join("\n", issueLines));
        }
        if (diagnosticLines.Count > 0)
        {
            sb.AppendLine(" \nRouting Diagnostics:");
            sb.AppendLine(string.Join("\n", diagnosticLines));
        }
        
        return sb.ToString();
    }

    public string? GetDiagnosticsPageSection(DiagnosticsPageArea section)
    {
        if (section == DiagnosticsPageArea.Start)
        {
            return ToString();
        }
        return null;
    }
}