using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Blobs.LegacyProviders;
using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Microsoft.Extensions.Logging;

namespace Imageflow.Server.Storage.RemoteReader
{
    /// <summary>
    /// Unlike RemoteReaderService, this does not require signing of requests.  
    /// You map prefixes to domains + base paths. 
    /// /// </summary>
    public class DomainProxyService : IBlobWrapperProviderZoned, IBlobWrapperProvider
    {
        private readonly IHttpClientFactory httpFactory;
        private readonly DomainProxyServiceOptions _options;
        private readonly IReLogger logger;

        private readonly List<DomainProxyPrefix> _mappings = new List<DomainProxyPrefix>();

        private LatencyTrackingZone _defaultZone;

        public DomainProxyService(DomainProxyServiceOptions options, 
        IHttpClientFactory httpFactory, IReLoggerFactory loggerFactory)
        {
            _options = options;
            this.httpFactory = httpFactory;
            this.logger = loggerFactory.CreateReLogger("DomainProxyService");
            _mappings = options.Prefixes.ToList();
            _mappings.Sort((a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));

            foreach (var mapping in _mappings)
            {
                // Ensure the uri is valid and starts with https. 
                if (!mapping.RemoteUriBase.StartsWith("https://"))
                {
                    throw new ArgumentException($"RemoteUriBase must start with https://. Found: {mapping.RemoteUriBase}");
                }
                mapping.Zone = new LatencyTrackingZone($"remote:{mapping.RemoteUriBase}", 1000, true);
            }
            _defaultZone = new LatencyTrackingZone("remote:default", 1000, true);
        }

        public string UniqueName => "DomainProxyService";

        public IEnumerable<BlobWrapperPrefixZone> GetPrefixesAndZones()
        {
            return _mappings.Select(p => new BlobWrapperPrefixZone(p.Prefix, p.Zone ?? _defaultZone));
        }

        public LatencyTrackingZone GetLatencyZone(string virtualPath)
        {
            var prefix = GetPrefix(virtualPath);
            if (prefix == null)
            {
                return _defaultZone;
            }
            return prefix.Zone ?? _defaultZone;
        }

        private DomainProxyPrefix? GetPrefix(string virtualPath)
        {
            return _options.Prefixes.FirstOrDefault(p => virtualPath.StartsWith(p.Prefix));
        }

        public IEnumerable<string> GetPrefixes()
        {
            return _options.Prefixes.Select(p => p.Prefix);
        }

        public bool SupportsPath(string virtualPath)
        {
            return GetPrefix(virtualPath) != null;
        }

        public async Task<CodeResult<IBlobWrapper>> Fetch(string virtualPath)
        {
            var prefix = GetPrefix(virtualPath);
            if (prefix == null) return CodeResult<IBlobWrapper>.Err(HttpStatus.NotFound.WithMessage("No prefix found for {virtualPath}"));

            var url = $"{prefix.RemoteUriBase}/{virtualPath.Substring(prefix.Prefix.Length)}";

            // Per the docs, we do not need to dispose HttpClient instances. HttpFactories track backing resources and handle
            // everything. https://source.dot.net/#Microsoft.Extensions.Http/IHttpClientFactory.cs,4f4eda17fc4cd91b
            var client = httpFactory.CreateClient(prefix.HttpClientName ?? "default");
            try
            {
                var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    logger?.LogWarning(
                        "RemoteReader blob {VirtualPath} not found. The remote {Url} responded with status: {StatusCode}",
                        virtualPath, url, resp.StatusCode);
                    return CodeResult<IBlobWrapper>.Err(HttpStatus.NotFound.WithMessage("RemoteReader blob \"{virtualPath}\" not found. The remote \"{url}\" responded with status: {resp.StatusCode}"));
                }

                var attrs = new BlobAttributes()
                {
                    ContentType = resp.Content.Headers.ContentType?.MediaType,
                    EstimatedBlobByteCount = resp.Content.Headers.ContentLength,
                    LastModifiedDateUtc = resp.Content.Headers.LastModified,
                    Etag = resp.Headers.ETag?.ToString()
                };

                var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);

                return CodeResult<IBlobWrapper>.Ok(new BlobWrapper(prefix.Zone,
                    new StreamBlob(attrs, stream, resp)));
            }
            catch (BlobMissingException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "RemoteReader blob error retrieving {Url} for {VirtualPath}", url,
                    virtualPath);
                return CodeResult<IBlobWrapper>.Err(HttpStatus.ServerError
                .WithMessage("RemoteReader blob error retrieving \"{url}\" for \"{virtualPath}\".")
                .WithAppend(ex.Message));
            }
        
        }





    }
}
