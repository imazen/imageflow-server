using Imazen.Abstractions.Resulting;
using Imazen.Common.Issues;
using Imazen.Routing.Matching;
using Imazen.Routing.Providers;
using Imazen.Routing.Requests;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Collections;
using Imazen.Abstractions.Logging;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;
using Imazen.Routing.RoutingExpressions;

namespace Imazen.Routing.Layers.RoutingExpressions;

public record UriRoutingOptions(List<string> Routes)
{

};

public class RoutingExpressionLayer : IRoutingLayer, IIssueProvider, IDisposable
{
    private record BadRoutingResult(string inputUrl, string expression, string resultPathAndQuery, string message);
    public string Name => "RoutingExpressionLayer";
    public IFastCond? FastPreconditions => null;


    private readonly IssueSink configIssues = new("RoutingExpressions");
    private readonly IssueSink evalIssues = new("RoutingExpressions", 15); // limit stored eval issues
    private readonly IDisposable? _optionsListener;

    private RoutingExpressionProviderSet _providerSet;
    private readonly List<IRoutedBlobProvider> _providers;
    private readonly List<IRoutedBlobProviderGroup> _providerGroups;
    
    private readonly IReLogger _logger;

    public RoutingExpressionLayer(IOptionsMonitor<UriRoutingOptions> options, IReLoggerFactory loggerFactory, 
    IEnumerable<IRoutedBlobProviderGroup> providerGroups, 
    IEnumerable<IRoutedBlobProvider> providerDefinitions)
    {
        _logger = loggerFactory.CreateReLogger("RoutingExpressionLayer");
        _providers = providerDefinitions.ToList();
        _providerGroups = providerGroups.ToList();
        _optionsListener = options.OnChange((options) => ReloadRoutes(options, _providers, _providerGroups));
        foreach (var group in _providerGroups)
        {
            group.OnProvidersChanged += () => ReloadRoutes(options.CurrentValue,_providers, _providerGroups);
        }
        ReloadRoutes(options.CurrentValue,_providers, _providerGroups);
    }

    // Any schema allowed, but a scheme: is required.
    private static readonly RoutingParsingOptions DefaultParsingOptions = new(
        AllowedSchemes: null,
        RequireScheme: true,
        RequirePath: true,
        AllowedTemplatePrefixes: null,
        RequireTemplatePrefix: false,
        AllowedFlags: null,
        AllowedFlagRegexes: null);


    private void ReloadRoutes(UriRoutingOptions options, List<IRoutedBlobProvider> providers, List<IRoutedBlobProviderGroup> providerGroups)
    {

        configIssues.ClearIssues();
        var parsedRoutingExpressions = new List<ParsedRoutingExpression>(options.Routes.Count);
        foreach (var expression in options.Routes)
        {
            if (RoutingExpressionParser.TryParse(DefaultParsingOptions, expression, out var parsed, out var error))
            {
                parsedRoutingExpressions.Add(parsed.Value);
            }
            else
            {
                configIssues.AcceptIssue(new Issue("Invalid route expression: " + expression + " Error: " + error, IssueSeverity.ConfigurationError));
            }
        }
        var combined = providers.Concat(providerGroups.SelectMany(g => g.Providers)).ToList();
        if (!RoutingExpressionProviderSet.TryCreate(combined, parsedRoutingExpressions, _logger, out var set, out var criticalError))
        {
            throw new Exception("Failed to create routing expression provider set: " + criticalError);
        }
        _providerSet = set;

    }

    private static readonly Dictionary<string, string> EmptyDictionary = new();

    public async ValueTask<CodeResult<IRoutingEndpoint>?> ApplyRouting(MutableRequest request, CancellationToken cancellationToken = default)
    {
        var context = MatchingContext.Default;
        foreach (var row in _providerSet.Routes)
        {
            var result = row.Route.Matcher.Match(context, request, null);
            if (!result.Success) continue;
            if (!row.Route.Template.TryEvaluateToPathAndQuery(result.Captures ?? EmptyDictionary, 
                out string? pathResult, out string? queryResult, 
                out List<KeyValuePair<string?, string?>>? queryPairs, out string? error))
            {
                // Trace failure
                _logger.LogTrace("Template in routing expression {expression} failed to evaluate to path and query: {error}", row.Route.OriginalExpression, error);
                //return CodeResult<IRoutingEndpoint>.ErrFrom(HttpStatus.ServerError, "Invalid final URI");
                continue;
            }

            if (!row.Provider.RespondsTo(pathResult, queryPairs, row.Route.ProviderInfo))
            {
                // Trace provider not handling path
                _logger.LogTrace("Providers {providerNames} do not handle path {pathResult}", row.Provider.ProviderNames, pathResult);
                continue; 
            }

            Uri? uri = null;
            if (row.Provider.NeedsUri && !TryCreateUri(row, pathResult, queryResult, request, out uri, out var errResult))
            {
                if (errResult != null)
                {
                    return errResult;
                }
                continue;
            }
            

            var promise = await row.Provider.GetBlobAsync(pathResult, queryPairs, uri, row.Route.ProviderInfo, request, cancellationToken);
            if (promise == null) break;
            return promise.MapOk<IRoutingEndpoint>(p => new PromiseWrappingEndpoint(p));
        }
        return null; // Let the other layers at it
    }

    private bool TryCreateUri(RouteRow row, string? pathResult, string? queryResult, IRequestSnapshot request, out Uri? uri, out CodeResult<IRoutingEndpoint>? errResult)
    {
        var combined = pathResult ?? "" + (queryResult ?? "");
        if (!Uri.TryCreate(combined, UriKind.Absolute, out uri))
        {
            // This is possibly a configuration error, as the template should always produce a valid URI.
            // We don't have a good channel for this error right now. 
            evalIssues.AcceptIssue(new Issue("Routing expression produced invalid Uri", "Expression: " + row.Route.OriginalExpression + " Request: " + request + " Failed to create URI from template result '" + combined + "'", IssueSeverity.ConfigurationError));
            errResult = CodeResult<IRoutingEndpoint>.ErrFrom(HttpStatus.NotImplemented, "Invalid final URI");
            return false;
        }

        // If possible, make sure result canonical URI is a superset of the canonical template start URI
        var templateStartUri = row.Route.TemplateLiteralStartUri;
        if (templateStartUri is not null)
        {
            var validateAgainst = templateStartUri;
            // Compare host values
            if (templateStartUri.Host != uri.Host)
            {
                // Construct a new URI from TemplateLiteralStartUri, but with the host from uri
                var newUriString = templateStartUri.Scheme + "://" + uri.Host + templateStartUri.GetLeftPart(UriPartial.Path);
                validateAgainst = new Uri(newUriString);   
            }
            if (!validateAgainst.IsBaseOf(uri))
            {
                evalIssues.AcceptIssue(new Issue("Routing expression produced invalid Uri", "Result URI: " + uri + 
                " (from template output ' + result.PathAndQuery + '), expected to have base uri " + validateAgainst + ", Expression: " + row.Route.OriginalExpression + 
                " Request: " + request + "", IssueSeverity.ConfigurationError));
                errResult = CodeResult<IRoutingEndpoint>.ErrFrom(HttpStatus.ServerError, "Invalid final URI");
                return false;
            }
        }
        errResult = null;
        return true;
    }

    public IEnumerable<IIssue> GetIssues() => configIssues.GetIssues().Concat(_providerSet.GetIssues()).Concat(evalIssues.GetIssues());

    public void Dispose() => _optionsListener?.Dispose();
}
