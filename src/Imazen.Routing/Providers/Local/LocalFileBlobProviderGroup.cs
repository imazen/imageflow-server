using Imazen.Abstractions;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Issues;
using Imazen.Routing.Layers;
using Imazen.Routing.Promises;
using Imazen.Routing.Requests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Routing.Helpers;
using System.Runtime.InteropServices;
using Imazen.Routing.Matching.Templating;
using Imazen.Routing.RoutingExpressions;

namespace Imazen.Routing.Providers.Local;

    public class LocalFileBlobProviderGroup : IRoutedBlobProvider, IIssueProvider, IUniqueNamed
{
    private readonly IOptionsMonitor<LocalFileBlobProviderGroupOptions> _optionsMonitor;
    private readonly IssueSink _issues;
    private readonly IReLogger _logger;
    private readonly IDefaultContentRootPathProvider _defaultContentRootPathProvider;

    private readonly string _defaultContentRootPath;

    private LocalFileBlobProviderGroupOptions? _options;
    private LocalFileProviderOptions[] _mappings = [];
    private Dictionary<string, List<string>> schemeToNames = [];
    private string[] schemes = [];

    public LocalFileBlobProviderGroup(IOptionsMonitor<LocalFileBlobProviderGroupOptions> options,IDefaultContentRootPathProvider defaultContentRootPathProvider, IReLoggerFactory loggerFactory)
    {
        this._optionsMonitor = options;
        this._logger = loggerFactory.CreateReLogger("LocalFileBlobProviderGroup");
        this._issues = new IssueSink(UniqueName);
        this._defaultContentRootPathProvider = defaultContentRootPathProvider;
        this._defaultContentRootPath = defaultContentRootPathProvider.DefaultContentRootPath;
        ReloadOptions(_optionsMonitor.CurrentValue);
        _optionsMonitor.OnChange(ReloadOptions);
    }
    
    private void ReloadOptions(LocalFileBlobProviderGroupOptions options)
    {
        
        _mappings = options.ResolveToFullSet(_defaultContentRootPathProvider, out var errorMessage);
        if (errorMessage != null)
        {
            _issues.AcceptIssue(new Issue("LocalFileProvider configuration error", errorMessage, IssueSeverity.ConfigurationError));

        }
        _options = options.WithMappings(_mappings);
        
        this.schemes = _mappings.Select(m => m.RequireScheme!).Distinct().ToArray();
        this.schemeToNames = _mappings.GroupBy(m => m.RequireScheme!)
        .ToDictionary(g => g.Key, g => g.Select(m => m.Name!).Distinct().ToList(), StringComparer.OrdinalIgnoreCase);
    }

    

    public string UniqueName => "LocalFileUriBlobProvider";

    public IReadOnlyCollection<string> RespondsToStaticPrefixes => 
        _mappings.Where(m => m.RequireUriPathStartsWith != null)
                 .Select(m => m.RequireUriPathStartsWith!)
                 .Distinct()
                 .ToArray();

    public IReadOnlyCollection<string> ProviderNames => 
        _mappings.Select(m => m.Name!)
                 .Distinct()
                 .ToArray();

    public bool NeedsUri => true;


    public IEnumerable<IIssue> GetIssues() => _issues.GetIssues();
    private LocalFileProviderOptions? FindMatch(Uri uri, string? providerName, out bool foundNameMatch)
    {
        var providerNameOrDefault = providerName ?? "files";
        foundNameMatch = false;
        foreach (var mapping in _mappings)
        {
            var nameMatches = mapping.MatchesName(providerNameOrDefault);
            if (nameMatches)
            {
                foundNameMatch = providerName != null;
            }
            var schemeMatches = mapping.MatchesScheme(uri.Scheme);
            var hostMatches = mapping.MatchesHost(uri.Host);
            var pathMatches = mapping.MatchesLocalPathPrefix(uri.LocalPath);
            var uriPathMatches = mapping.MatchesUriPathPrefix(uri.AbsolutePath);
            if (nameMatches && schemeMatches && hostMatches && pathMatches && uriPathMatches)
            {
                return mapping;
            }
        }
        return null;
    }

    public override string ToString()
    {
        return $"Provider Group {UniqueName}: {_options}";
    }

    public bool ProvidesFor(RouteProviderInfo providerInfo)
    {
        var providerNameOrDefault = providerInfo.ProviderName ?? "files";
        foreach (var mapping in _mappings)
        {
            var nameMatches = mapping.MatchesName(providerNameOrDefault);
            var schemeMatches = mapping.MatchesScheme(providerInfo.FixedScheme);
            if (nameMatches && schemeMatches)
            {
                return providerInfo.ProviderName != null || (providerInfo.FixedScheme != null && mapping.RequireScheme != null);
            }
        }
        return false;
    }

    public bool RespondsTo(string? path, ICollection<KeyValuePair<string?, string?>>? query, RouteProviderInfo providerInfo)
    {
        var providerNameOrDefault = providerInfo.ProviderName ?? "files";
        foreach (var mapping in _mappings)
        {
            var nameMatches = mapping.MatchesName(providerNameOrDefault);
            var schemeMatches = mapping.MatchesScheme(providerInfo.FixedScheme);
            var pathMatches = mapping.MatchesUriPathPrefix(path ?? "/");
            
            if (nameMatches && schemeMatches && pathMatches)
            {
                return true;
            }
        }
        return false;
    }

    public ValueTask<CodeResult<ICacheableBlobPromise>?> GetBlobAsync(string? path, ICollection<KeyValuePair<string?, string?>>? query, Uri? uri, RouteProviderInfo providerInfo, IRequestSnapshot request, CancellationToken cancellationToken)
    {
        // If no URI is provided but we need one, we can't process this request
        if (uri == null)
        {
            return Tasks.ValueResult<CodeResult<ICacheableBlobPromise>?>(null);
        }

        if (!uri.IsFile)
        {
            // Should be caught ahead of time, not here
            _issues.AcceptIssue(new Issue("Unsupported scheme", $"{UniqueName} only supports the 'file' URI scheme, not '{uri.Scheme}'.", IssueSeverity.ConfigurationError));
            return new ValueTask<CodeResult<ICacheableBlobPromise>?>(CodeResult<ICacheableBlobPromise>.Err(new HttpStatus((int)HttpStatusCode.BadRequest, "Unsupported URI scheme")));
        }

        var matchingMapping = FindMatch(uri, providerInfo.ProviderName, out var foundNameMatch);

        if (matchingMapping == null && foundNameMatch)
        {
            // If they specified one of our provider names explicitly, fail with a 404 immediately.
            return new ValueTask<CodeResult<ICacheableBlobPromise>?>(CodeResult<ICacheableBlobPromise>.Err(HttpStatusCode.NotFound));
        }
        if (matchingMapping == null)
        {
            // If they didn't specify a provider name, let the next provider work on this.
            return Imazen.Routing.Helpers.Tasks.ValueResult<CodeResult<ICacheableBlobPromise>?>(null);
        }

        var physicalPath = matchingMapping.GetPhysicalPath(uri);
        if (physicalPath == null)
        {
            _issues.AcceptIssue(new Issue("Path traversal attempt detected", "The requested path resolves outside the allowed prefix.", IssueSeverity.Error));
            return new ValueTask<CodeResult<ICacheableBlobPromise>?>(CodeResult<ICacheableBlobPromise>.Err(new HttpStatus((int)HttpStatusCode.Forbidden, "Path traversal attempt detected")));
        }
        
        var lastWriteTimeUtc = File.GetLastWriteTimeUtc(physicalPath);
        if (lastWriteTimeUtc.Year == 1601) // file doesn't exist, pass to next middleware
        {
            return new ValueTask<CodeResult<ICacheableBlobPromise>?>(CodeResult<ICacheableBlobPromise>.Err(new HttpStatus((int)HttpStatusCode.NotFound, "File not found")));
        }

        var fileInfo = new FileInfo(physicalPath);
        if (!fileInfo.Exists)
        {
            _issues.AcceptIssue(new Issue("File not found", "The requested file does not exist.", IssueSeverity.Warning));
            return new ValueTask<CodeResult<ICacheableBlobPromise>?>(CodeResult<ICacheableBlobPromise>.Err(new HttpStatus((int)HttpStatusCode.NotFound, "File not found")));
        }

        // Create a promise
        var latencyZone = new LatencyTrackingZone(physicalPath, 10);
        var loggerForPath = _logger.WithReScopeData("requestPath", request.Path);
        return Tasks.ValueResult<CodeResult<ICacheableBlobPromise>?>(CodeResult<ICacheableBlobPromise>.Ok(
            new FilePromise(request, physicalPath, latencyZone, lastWriteTimeUtc, loggerForPath)));
    }

}
