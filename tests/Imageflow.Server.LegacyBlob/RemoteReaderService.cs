using Imazen.Common.Helpers;
using Imazen.Common.Storage;
using Microsoft.Extensions.Logging;
#pragma warning disable CS0618 // Type or member is obsolete

namespace Imageflow.Server.LegacyBlob
{
    public class RemoteReaderService : IBlobProvider
    {

        private readonly List<string> prefixes = new List<string>();
        private readonly IHttpClientFactory httpFactory;
        private readonly RemoteReaderServiceOptions options;
        private readonly ILogger<RemoteReaderService> logger;
        private readonly Func<Uri, string> httpClientSelector;

        public RemoteReaderService(RemoteReaderServiceOptions options
            , ILogger<RemoteReaderService> logger
            , IHttpClientFactory httpFactory
            )
        {
            this.options = options;
            this.logger = logger;
            this.httpFactory = httpFactory;
            httpClientSelector = options.HttpClientSelector ?? (_ => "");

            prefixes.AddRange(this.options.Prefixes);
            prefixes.Sort((a, b) => b.Length.CompareTo(a.Length));
        }

        /// <summary>
        /// The remote URL and signature are encoded in the "file" part
        /// of the virtualPath parameter as follows:
        /// path/path/.../path/url_b64.hmac.ext
        /// </summary>
        /// <param name="virtualPath"></param>
        /// <returns></returns>
        public async Task<IBlobData> Fetch(string virtualPath)
        {
            var remote = virtualPath
                .Split('/')
                .Last()?
                .Split('.');

            if (remote == null || remote.Length < 2)
            {
                logger?.LogWarning("Invalid remote path: {VirtualPath}", virtualPath);
                throw new BlobMissingException($"Invalid remote path: {virtualPath}");
            }

            var urlBase64 = remote[0];
            var hmac = remote[1];

            var sig = Signatures.SignString(urlBase64, options.SigningKey, 8);
            if (hmac != sig)
            {
                //Try the fallback keys
                if (options.SigningKeys == null ||
                    !options.SigningKeys.Select(key => Signatures.SignString(urlBase64, key, 8)).Contains(hmac))
                {

                    logger?.LogWarning("Missing or Invalid signature on remote path: {VirtualPath}", virtualPath);
                    throw new BlobMissingException($"Missing or Invalid signature on remote path: {virtualPath}");
                }
            }

            var url = EncodingUtils.FromBase64UToString(urlBase64);

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                logger?.LogWarning("RemoteReader blob {VirtualPath} not found. Invalid Uri: {Url}", virtualPath, url);
                throw new BlobMissingException($"RemoteReader blob \"{virtualPath}\" not found. Invalid Uri: {url}");
            }

            var httpClientName = httpClientSelector(uri);
            // Per the docs, we do not need to dispose HttpClient instances. HttpFactories track backing resources and handle
            // everything. https://source.dot.net/#Microsoft.Extensions.Http/IHttpClientFactory.cs,4f4eda17fc4cd91b
            var client = httpFactory.CreateClient(httpClientName);
            try
            {
                var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    logger?.LogWarning(
                        "RemoteReader blob {VirtualPath} not found. The remote {Url} responded with status: {StatusCode}",
                        virtualPath, url, resp.StatusCode);
                    throw new BlobMissingException(
                        $"RemoteReader blob \"{virtualPath}\" not found. The remote \"{url}\" responded with status: {resp.StatusCode}");
                }

                return new RemoteReaderBlob(resp);
            }
            catch (BlobMissingException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "RemoteReader blob error retrieving {Url} for {VirtualPath}", url,
                    virtualPath);
                throw new BlobMissingException(
                    $"RemoteReader blob error retrieving \"{url}\" for \"{virtualPath}\".", ex);
            }
        }

        public IEnumerable<string> GetPrefixes()
        {
            return prefixes;
        }

        public bool SupportsPath(string virtualPath)
        {
            return prefixes.Any(s => virtualPath.StartsWith(s,
                options.IgnorePrefixCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        }
        
        private static string? SanitizeImageExtension(string extension)
        {
            //TODO: Deduplicate this function when making Imazen.ImageAPI.Client
            extension = extension.ToLowerInvariant().TrimStart('.');
            switch (extension)
            {
                case "png":
                    return "png";
                case "gif":
                    return "gif";
                case "webp":
                    return "webp";
                case "jpeg":
                case "jfif":
                case "jif":
                case "jfi":
                case "jpe":
                    return "jpg";
                default:
                    return null;
            }
        }
        public static string EncodeAndSignUrl(string url, string key)
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var extension = Path.GetExtension(path);
            var sanitizedExtension = SanitizeImageExtension(extension)?? "jpg";
            var data = EncodingUtils.ToBase64U(url);
            var sig = Signatures.SignString(data, key,8);
            return $"{data}.{sig}.{sanitizedExtension}";
        }
    }
}
