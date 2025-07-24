using Imazen.Abstractions.Resulting;
using Imazen.Common.Issues;
using Imazen.Routing.Matching;
using Imazen.Routing.Providers;
using Imazen.Routing.Requests;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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


internal record struct RouteRow(ParsedRoutingExpression Route, IRoutedBlobProvider Provider);
internal class RoutingExpressionProviderSet : IIssueProvider
{
    // Scheme -> Provider Name -> Provider Group
    private readonly Dictionary<string, Dictionary<string, IRoutedBlobProvider>> providersByPrefix;

    private readonly List<KeyValuePair<string, string>>? duplicateProviderNames;

    private readonly Dictionary<string, IRoutedBlobProvider> providersByName;

    private readonly RouteRow[] routes;

    public RouteRow[] Routes => routes;

    public IEnumerable<IIssue> GetIssues()
    {
        return duplicateProviderNames?.Select(p => new Issue("Duplicate provider name", $"Provider name '{p.Value}' conflicts with previously registered provider for scheme '{p.Key}'", IssueSeverity.ConfigurationError)) ?? [];
    }
    internal RoutingExpressionProviderSet(Dictionary<string, Dictionary<string, IRoutedBlobProvider>> providersByPrefix,
    List<KeyValuePair<string, string>>? duplicateProviderNames,
    Dictionary<string, IRoutedBlobProvider> providersByName,
    RouteRow[] routes)
    {
        this.providersByPrefix = providersByPrefix;
        this.duplicateProviderNames = duplicateProviderNames;
        this.providersByName = providersByName;
        this.routes = routes;
    }
    public static bool TryCreate(IEnumerable<IRoutedBlobProvider> providers,
        IEnumerable<ParsedRoutingExpression> parsedRoutingExpressions,
        ILogger logger, out RoutingExpressionProviderSet set, out string criticalError)
    {
        set = null;
        criticalError = null;

        var providerList = providers.ToList();
        var expressionList = parsedRoutingExpressions.ToList();
        var byPrefix = new Dictionary<string, Dictionary<string, IRoutedBlobProvider>>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, IRoutedBlobProvider>(StringComparer.OrdinalIgnoreCase);
        var duplicateProviderNames = new List<KeyValuePair<string, string>>();
        var routes = new List<RouteRow>();
        var providerNameHelp = new StringBuilder(); // "prefix....[provider=x|y|z] for each prefix.
        foreach (var provider in providerList)
        {
            foreach (var prefix in provider.RespondsToStaticPrefixes)
            {
                if (!byPrefix.TryGetValue(prefix, out var dict))
                {
                    dict = new Dictionary<string, IRoutedBlobProvider>(StringComparer.OrdinalIgnoreCase);
                }
                foreach (var providerName in provider.ProviderNames)
                {
                    // If it already exists, we have a conflict, throw an exception
                    if (dict.ContainsKey(providerName))
                    {
                        duplicateProviderNames ??= [];
                        duplicateProviderNames.Add(new KeyValuePair<string, string>(prefix, providerName));
                        logger.LogError("Provider name '{providerName}' conflicts with previously registered provider for prefix '{prefix}'", providerName, prefix);

                    }
                    dict[providerName] = provider;
                    byName[providerName] = provider;
                }
                providerNameHelp.Append($"{prefix}...[provider={string.Join("|", provider.ProviderNames)}]");
            }
        }

        foreach (var parsed in expressionList)
        {
            var matchingProvider = providerList.FirstOrDefault(p => p.ProvidesFor(parsed.ProviderInfo));

            // If no provider supports parsed.ProviderInfo, log it
            if (matchingProvider == null)
            {
                logger.LogError("No provider found for route {route}. Try one of {providerNameHelp}", parsed.OriginalExpression, providerNameHelp.ToString());
            }
            else
            {
                routes.Add(new RouteRow(parsed, matchingProvider));
            }
        }

        set = new RoutingExpressionProviderSet(byPrefix, duplicateProviderNames, byName, routes.ToArray());
        return true;
    }



}