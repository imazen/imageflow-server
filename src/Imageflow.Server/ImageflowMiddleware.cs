using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Imageflow.Bindings;
using Imazen.Common.Extensibility.ClassicDiskCache;
using Imazen.Common.Extensibility.StreamCache;
using Imazen.Common.Instrumentation;
using Imazen.Common.Instrumentation.Support.InfoAccumulators;
using Imazen.Common.Licensing;
using Imazen.Common.Storage;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace Imageflow.Server
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ImageflowMiddleware
    {
        private enum Image404FilterMode
        {
            IncludeUnknownCommands,
            ExcludeUnknownCommands,
            IncludeAllCommands,
            ExcludeAllCommands
        }

        private readonly RequestDelegate next;
        private readonly ILogger<ImageflowMiddleware> logger;
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly IWebHostEnvironment env;
        private readonly IClassicDiskCache diskCache;
        private readonly IStreamCache streamCache;
        private readonly BlobProvider blobProvider;
        private readonly DiagnosticsPage diagnosticsPage;
        private readonly LicensePage licensePage;
        private readonly ImageflowMiddlewareOptions options;
        private readonly GlobalInfoProvider globalInfoProvider;
        public ImageflowMiddleware(
            RequestDelegate next, 
            IWebHostEnvironment env, 
            IEnumerable<ILogger<ImageflowMiddleware>> logger, 
            IEnumerable<IClassicDiskCache> diskCaches, 
            IEnumerable<IStreamCache> streamCaches, 
            IEnumerable<IBlobProvider> blobProviders, 
            ImageflowMiddlewareOptions options)
        {
            this.next = next;
            options.Licensing ??= new Licensing(LicenseManagerSingleton.GetOrCreateSingleton(
                "imageflow_", new[] {env.ContentRootPath, Path.GetTempPath()}));
            this.options = options;
            this.env = env;
            this.logger = logger.FirstOrDefault();
            diskCache = diskCaches.FirstOrDefault();

            var streamCacheArray = streamCaches.ToArray();
            if (streamCacheArray.Count() > 1)
            {
                throw new InvalidOperationException("Only 1 IStreamCache instance can be registered at a time");
            }
            
            streamCache = streamCacheArray.FirstOrDefault();

            
            var providers = blobProviders.ToList();
            var mappedPaths = options.MappedPaths.ToList();
            if (options.MapWebRoot)
            {
                if (this.env?.WebRootPath == null)
                    throw new InvalidOperationException("Cannot call MapWebRoot if env.WebRootPath is null");
                mappedPaths.Add(new PathMapping("/", this.env.WebRootPath));
            }
            
            //Determine the active cache backend
            var streamCacheEnabled = streamCache != null && options.AllowCaching;
            var diskCacheEnabled = this.diskCache != null && options.AllowDiskCaching;

            if (streamCacheEnabled)
                options.ActiveCacheBackend = CacheBackend.StreamCache;
            else if (diskCacheEnabled)
                options.ActiveCacheBackend = CacheBackend.ClassicDiskCache;
            else
                options.ActiveCacheBackend = CacheBackend.NoCache;
            
            
            options.Licensing.Initialize(this.options);

            blobProvider = new BlobProvider(providers, mappedPaths);
            diagnosticsPage = new DiagnosticsPage(options, env, this.logger, streamCache, this.diskCache, providers);
            licensePage = new LicensePage(options);
            globalInfoProvider = new GlobalInfoProvider(options, env, this.logger, streamCache,  this.diskCache, providers);
            
            options.Licensing.FireHeartbeat();
            GlobalPerf.Singleton.SetInfoProviders(new List<IInfoProvider>(){globalInfoProvider});
        }

        private string MakeWeakEtag(string cacheKey) => $"W/\"{cacheKey}\"";
        // ReSharper disable once UnusedMember.Global
        public async Task Invoke(HttpContext context)
        {
            // For instrumentation
            globalInfoProvider.CopyHttpContextInfo(context);
            
            var path = context.Request.Path;

            
            // Delegate to the diagnostics page if it is requested
            if (DiagnosticsPage.MatchesPath(path.Value))
            {
                await diagnosticsPage.Invoke(context);
                return;
            }
            // Delegate to licenses page if requested
            if (licensePage.MatchesPath(path.Value))
            {
                await licensePage.Invoke(context);
                return;
            }

            // Respond to /imageflow.ready
            if ( "/imageflow.ready".Equals(path.Value, StringComparison.Ordinal))
            {
                options.Licensing.FireHeartbeat();
                using (new JobContext())
                {
                    await StringResponseNoCache(context, 200, "Imageflow.Server is ready to accept requests.");
                }
                return;
            }
            
            // Respond to /imageflow.health
            if ( "/imageflow.health".Equals(path.Value, StringComparison.Ordinal))
            {
                options.Licensing.FireHeartbeat();
                await StringResponseNoCache(context, 200, "Imageflow.Server is healthy.");
                return;
            }
            

            // We only handle requests with an image extension or if we configured a path prefix for which to handle
            // extension-less requests
            
            if (!ImageJobInfo.ShouldHandleRequest(context, options))
            {
                await next.Invoke(context);
                return;
            }
            
            options.Licensing.FireHeartbeat();
            
            var imageJobInfo = new ImageJobInfo(context, options, blobProvider);

            if (!imageJobInfo.Authorized)
            {
                await NotAuthorized(context, imageJobInfo.AuthorizedMessage);
                return;
            }

            if (imageJobInfo.LicenseError) 
            {
                if (options.EnforcementMethod == EnforceLicenseWith.Http422Error)
                {
                    await StringResponseNoCache(context, 422, options.Licensing.InvalidLicenseMessage);
                    return;
                }
                if (options.EnforcementMethod == EnforceLicenseWith.Http402Error)
                {
                    await StringResponseNoCache(context, 402, options.Licensing.InvalidLicenseMessage);
                    return;
                }
            }

            // If the file is definitely missing hand to the next middleware
            // Remote providers will fail late rather than make 2 requests
            if (!imageJobInfo.PrimaryBlobMayExist())
            {
                if (TryHandleImage404(context))
                {
                    return;
                }
                await next.Invoke(context);
                return;
            }
            
            string cacheKey = null;
            var cachingPath = imageJobInfo.NeedsCaching() ? options.ActiveCacheBackend : CacheBackend.NoCache;
            if (cachingPath != CacheBackend.NoCache)
            {
                cacheKey = await imageJobInfo.GetFastCacheKey();
                
                // W/"etag" should be used instead, since we might have to regenerate the result non-deterministically while a client is downloading it with If-Range
                // If-None-Match is supposed to be weak always
                var etagHeader = MakeWeakEtag(cacheKey);
            
                if (context.Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var conditionalEtag) && etagHeader == conditionalEtag)
                {
                    GlobalPerf.Singleton.IncrementCounter("etag_hit");
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    context.Response.ContentLength = 0;
                    context.Response.ContentType = null;
                    return;
                }
                GlobalPerf.Singleton.IncrementCounter("etag_miss");
            }

            try
            {
                switch (cachingPath)
                {
                    case CacheBackend.ClassicDiskCache:
                        await ProcessWithDiskCache(context, cacheKey, imageJobInfo);
                        break;
                    case CacheBackend.NoCache:
                        await ProcessWithNoCache(context, imageJobInfo);
                        break;
                    case CacheBackend.StreamCache:
                        await ProcessWithStreamCache(context, cacheKey, imageJobInfo);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                
                GlobalPerf.Singleton.IncrementCounter("middleware_ok");
            }
            catch (BlobMissingException e)
            {
                if (TryHandleImage404(context))
                {
                    return;
                }
                await NotFound(context, e);
            }
            catch (Exception e)
            {
                var errorName = e.GetType().Name;
                var errorCounter = "middleware_" + errorName;
                GlobalPerf.Singleton.IncrementCounter(errorCounter);
                GlobalPerf.Singleton.IncrementCounter("middleware_errors");
                throw;
            }
            finally
            {
                // Increment counter for type of file served
                var imageExtension = PathHelpers.GetImageExtensionFromContentType(context.Response.ContentType);
                if (imageExtension != null)
                {
                    GlobalPerf.Singleton.IncrementCounter("module_response_ext_" + imageExtension);
                }
            }
        }

        private async Task NotAuthorized(HttpContext context, string detail)
        {
            var s = "You are not authorized to access the given resource.";
            if (!string.IsNullOrEmpty(detail))
            {
                s += "\r\n" + detail;
            }
            GlobalPerf.Singleton.IncrementCounter("http_403");
            await StringResponseNoCache(context, 403, s);
        }
        
        private async Task NotFound(HttpContext context, BlobMissingException e)
        {
            GlobalPerf.Singleton.IncrementCounter("http_404");
            // We allow 404s to be cached, but not 403s or license errors
            var s = "The specified resource does not exist.\r\n" + e.Message;
            context.Response.StatusCode = 404;
            context.Response.ContentType = "text/plain; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(s);
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
        }
        private async Task StringResponseNoCache(HttpContext context, int statusCode, string contents)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers[HeaderNames.CacheControl] = "no-store";
            var bytes = Encoding.UTF8.GetBytes(contents);
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
        }

        private bool TryHandleImage404(HttpContext context)
        {
            if (!context.Request.Query.TryGetValue("404", out var image404Value))
            {
                return false;
            }

            var requestedFallback = image404Value.ToString();
            if (string.IsNullOrWhiteSpace(requestedFallback))
            {
                return false;
            }

            var redirectPath = BuildImage404RedirectPath(context, requestedFallback);
            context.Response.Redirect(redirectPath, false);
            return true;
        }

        private string BuildImage404RedirectPath(HttpContext context, string requestedFallback)
        {
            var fallbackWithQuery = ResolveImage404Path(requestedFallback);
            var imageQuery = PathHelpers.ToQueryDictionary(context.Request.Query);

            var filterMode = imageQuery.TryGetValue("404.filterMode", out var modeString)
                ? ParseFilterMode(modeString)
                : Image404FilterMode.ExcludeUnknownCommands;
            var except = imageQuery.TryGetValue("404.except", out var exceptString)
                ? ParseCommandList(exceptString)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var keys = imageQuery.Keys.ToList();
            foreach (var key in keys)
            {
                if (ShouldRemoveCommand(filterMode, except, key, imageQuery[key]))
                {
                    imageQuery.Remove(key);
                }
            }

            imageQuery.Remove("404");
            imageQuery.Remove("404.filterMode");
            imageQuery.Remove("404.except");

            var (fallbackPath, fallbackQuery) = SplitPathAndQuery(fallbackWithQuery);
            foreach (var pair in fallbackQuery)
            {
                imageQuery[pair.Key] = pair.Value;
            }

            var query = QueryString.Create(imageQuery.Select(p => new KeyValuePair<string, StringValues>(p.Key, p.Value)))
                .ToString();
            return string.IsNullOrEmpty(query) ? fallbackPath : fallbackPath + query;
        }

        private static (string Path, Dictionary<string, string> Query) SplitPathAndQuery(string pathWithQuery)
        {
            var queryIndex = pathWithQuery.IndexOf('?');
            if (queryIndex < 0)
            {
                return (pathWithQuery, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            }

            var path = pathWithQuery.Substring(0, queryIndex);
            var queryString = pathWithQuery.Substring(queryIndex);
            var parsed = QueryHelpers.ParseQuery(queryString);
            var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in parsed)
            {
                query[pair.Key] = pair.Value.ToString();
            }

            return (path, query);
        }

        private static string ResolveImage404Path(string path)
        {
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase) || path.StartsWith("//", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Image 404 redirects must be server-local.");
            }

            if (path.StartsWith("/", StringComparison.Ordinal))
            {
                return path;
            }

            if (path.StartsWith("~", StringComparison.Ordinal))
            {
                return "/" + path.TrimStart('~', '/');
            }

            return "/" + path.TrimStart('/');
        }

        private static Image404FilterMode ParseFilterMode(string value)
        {
            return Enum.TryParse<Image404FilterMode>(value, true, out var mode)
                ? mode
                : Image404FilterMode.ExcludeUnknownCommands;
        }

        private static HashSet<string> ParseCommandList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return new HashSet<string>(
                value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        private static bool ShouldRemoveCommand(Image404FilterMode filterMode, HashSet<string> except, string name, string value)
        {
            return filterMode switch
            {
                Image404FilterMode.IncludeUnknownCommands => IsBlacklisted(name, value) || except.Contains(name),
                Image404FilterMode.ExcludeUnknownCommands => !(IsWhitelisted(name, value) || except.Contains(name)),
                Image404FilterMode.IncludeAllCommands => except.Contains(name),
                Image404FilterMode.ExcludeAllCommands => !except.Contains(name),
                _ => true
            };
        }

        private static bool IsWhitelisted(string name, string value)
        {
            if (name.StartsWith("s.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("a.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (name.Equals("crop", StringComparison.OrdinalIgnoreCase))
            {
                return value.Equals("auto", StringComparison.OrdinalIgnoreCase);
            }

            return name.Equals("maxwidth", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("maxheight", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("width", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("height", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("w", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("h", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("mode", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("anchor", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("scale", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("zoom", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("bgcolor", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("paddingwidth", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("paddingcolor", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("borderwidth", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("bordercolor", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("shadowwidth", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("shadowoffset", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("shadowcolor", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("margin", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("dpi", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("format", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("quality", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("colors", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("subsampling", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("dither", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("speed", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("ignoreicc", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("flip", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("rotate", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBlacklisted(string name, string value)
        {
            if (name.Equals("crop", StringComparison.OrdinalIgnoreCase))
            {
                return !value.Equals("auto", StringComparison.OrdinalIgnoreCase);
            }

            return name.Equals("watermark", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("cache", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("process", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("builder", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("decoder", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("encoder", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("sflip", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("srotate", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("autorotate", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("cropxunits", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("cropyunits", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("trim.threshold", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("trim.percentpadding", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("hmac", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("urlb64", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("frame", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("page", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("color1", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("color2", StringComparison.OrdinalIgnoreCase);
        }

        private async Task ProcessWithStreamCache(HttpContext context, string cacheKey, ImageJobInfo info)
        {
            var keyBytes = Encoding.UTF8.GetBytes(cacheKey);
            var typeName = streamCache.GetType().Name;
            var cacheResult = await streamCache.GetOrCreateBytes(keyBytes, async (cancellationToken) =>
            {
                if (info.HasParams)
                {
                    logger?.LogDebug("{CacheName} miss: Processing image {VirtualPath}?{Querystring}", typeName, info.FinalVirtualPath,info.ToString());
                    var result = await info.ProcessUncached();
                    if (result.ResultBytes.Array == null)
                    {
                        throw new InvalidOperationException("Image job returned zero bytes.");
                    }
                    return new StreamCacheInput(result.ContentType, result.ResultBytes);
                }
                
                logger?.LogDebug("{CacheName} miss: Proxying image {VirtualPath}",typeName,  info.FinalVirtualPath);
                var bytes = await info.GetPrimaryBlobBytesAsync();
                return new StreamCacheInput(null, bytes);
            
            },CancellationToken.None,false);

            if (cacheResult.Status != null)
            {
                GlobalPerf.Singleton.IncrementCounter($"{typeName}_{cacheResult.Status}");
            }
            if (cacheResult.Data != null)
            {
                await using (cacheResult.Data)
                {
                    if (cacheResult.Data.Length < 1)
                    {
                        throw new InvalidOperationException($"{typeName} returned cache entry with zero bytes");
                    }
                    SetCachingHeaders(context, MakeWeakEtag(cacheKey));
                    await MagicBytes.ProxyToStream(cacheResult.Data, context.Response);
                }
                logger?.LogDebug("Serving from {CacheName} {VirtualPath}?{CommandString}", typeName, info.FinalVirtualPath, info.CommandString);
            }
            else
            {
                // TODO explore this failure path better
                throw new NullReferenceException("Caching failed: " + cacheResult.Status);
            }
        }

        
        private async Task ProcessWithDiskCache(HttpContext context, string cacheKey, ImageJobInfo info)
        {
            var cacheResult = await diskCache.GetOrCreate(cacheKey, info.EstimatedFileExtension, async (stream) =>
            {
                if (info.HasParams)
                {
                    logger?.LogInformation("DiskCache Miss: Processing image {VirtualPath}{QueryString}", info.FinalVirtualPath,info);

 
                    var result = await info.ProcessUncached();
                    if (result.ResultBytes.Array == null)
                    {
                        throw new InvalidOperationException("Image job returned zero bytes.");
                    }
                    await stream.WriteAsync(result.ResultBytes,
                        CancellationToken.None);
                    await stream.FlushAsync();
                }
                else
                {
                    logger?.LogInformation("DiskCache Miss: Proxying image {VirtualPath}", info.FinalVirtualPath);
                    await info.CopyPrimaryBlobToAsync(stream);
                }
            });
            
            if (cacheResult.Result == CacheQueryResult.Miss)
            {
                GlobalPerf.Singleton.IncrementCounter("diskcache_miss");
            }
            else if (cacheResult.Result == CacheQueryResult.Hit)
            {
                GlobalPerf.Singleton.IncrementCounter("diskcache_hit");
            }
            else if (cacheResult.Result == CacheQueryResult.Failed)
            {
                GlobalPerf.Singleton.IncrementCounter("diskcache_timeout");
            }

            // Note that using estimated file extension instead of parsing magic bytes will lead to incorrect content-type
            // values when the source file has a mismatched extension.
            var etagHeader = MakeWeakEtag(cacheKey);
            if (cacheResult.Data != null)
            {
                if (cacheResult.Data.Length < 1)
                {
                    throw new InvalidOperationException("DiskCache returned cache entry with zero bytes");
                }
                SetCachingHeaders(context, etagHeader);
                await MagicBytes.ProxyToStream(cacheResult.Data, context.Response);
            }
            else
            {
                logger?.LogInformation("Serving {0}?{1} from disk cache {2}", info.FinalVirtualPath, info.CommandString, cacheResult.RelativePath);
                await ServeFileFromDisk(context, cacheResult.PhysicalPath, etagHeader);
            }
        }

        private async Task ServeFileFromDisk(HttpContext context, string path, string etagHeader)
        {
            await using var readStream = File.OpenRead(path);
            if (readStream.Length < 1)
            {
                throw new InvalidOperationException("DiskCache file entry has zero bytes");
            }
            SetCachingHeaders(context, etagHeader);
            await MagicBytes.ProxyToStream(readStream, context.Response);
        }
        private async Task ProcessWithNoCache(HttpContext context, ImageJobInfo info)
        {
            // If we're not caching, we should always use the modified date from source blobs as part of the etag
            var betterCacheKey = await info.GetExactCacheKey();
            // Still use weak since recompression is non-deterministic
            
            var etagHeader = MakeWeakEtag(betterCacheKey);
            if (context.Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var conditionalEtag) && etagHeader == conditionalEtag)
            {
                GlobalPerf.Singleton.IncrementCounter("etag_hit");
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                context.Response.ContentLength = 0;
                context.Response.ContentType = null;
                return;
            }
            GlobalPerf.Singleton.IncrementCounter("etag_miss");
            if (info.HasParams)
            {
                logger?.LogInformation("Processing image {VirtualPath} with params {CommandString}", info.FinalVirtualPath, info.CommandString);
                GlobalPerf.Singleton.IncrementCounter("nocache_processed");
                var imageData = await info.ProcessUncached();
                var imageBytes = imageData.ResultBytes;
                var contentType = imageData.ContentType;

                // write to stream
                context.Response.ContentType = contentType;
                context.Response.ContentLength = imageBytes.Count;
                SetCachingHeaders(context, etagHeader);

                if (imageBytes.Array == null)
                {
                    throw new InvalidOperationException("Image job returned zero bytes.");
                }
                await context.Response.Body.WriteAsync(imageBytes);
            }
            else
            {
                logger?.LogInformation("Proxying image {VirtualPath} with params {CommandString}", info.FinalVirtualPath, info.CommandString);
                GlobalPerf.Singleton.IncrementCounter("nocache_proxied");
                await using var sourceStream = (await info.GetPrimaryBlob()).OpenRead();
                SetCachingHeaders(context, etagHeader);
                await MagicBytes.ProxyToStream(sourceStream, context.Response);
            }
            

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="etagHeader">Should include W/</param>
        private void SetCachingHeaders(HttpContext context, string etagHeader)
        {
            context.Response.Headers[HeaderNames.ETag] = etagHeader;
            if (options.DefaultCacheControlString != null)
                context.Response.Headers[HeaderNames.CacheControl] = options.DefaultCacheControlString;
        }
        
    }
}
