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

namespace Imageflow.Server
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ImageflowMiddleware
    {
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

            // If the operator configured a three-layer killbits policy,
            // validate it once on a scratch context, cache the resulting
            // net-support grid for diagnostics, and stash a narrowing-only
            // effective SecurityOptions to apply per job. Any failure here
            // takes down startup — a silent degraded mode would defeat the
            // point of the killbits surface.
            if (options.SecurityPolicyOptions != null)
            {
                var validator = new SecurityPolicyValidator();
                validator.Apply(options.SecurityPolicyOptions, this.logger);
                options.SecurityPolicyValidator = validator;
            }

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
                await NotFound(context, e);
            }
            catch (KillbitsDeniedException e)
            {
                GlobalPerf.Singleton.IncrementCounter("middleware_killbits_denied");
                await KillbitsDenied(context, e);
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

        private async Task KillbitsDenied(HttpContext context, KillbitsDeniedException e)
        {
            // The native side embeds a JSON envelope in the error message
            // with a structured reasons grid; surface it as HTTP 422 so
            // clients can distinguish "your format isn't allowed" from
            // "server is broken".
            var body = new StringBuilder();
            body.Append("{\"error\":\"");
            body.Append(e.DenialKind switch
            {
                KillbitsDenialKind.CodecNotAvailable => "codec_not_available",
                KillbitsDenialKind.DecodeNotAvailable => "decode_not_available",
                KillbitsDenialKind.EncodeNotAvailable => "encode_not_available",
                _ => "killbits_denied",
            });
            body.Append('"');
            if (!string.IsNullOrEmpty(e.Codec))
            {
                body.Append(",\"codec\":\"").Append(e.Codec).Append('"');
            }
            if (!string.IsNullOrEmpty(e.Format))
            {
                body.Append(",\"format\":\"").Append(e.Format).Append('"');
            }
            if (e.Reasons.Count > 0)
            {
                body.Append(",\"reasons\":[");
                for (var i = 0; i < e.Reasons.Count; i++)
                {
                    if (i > 0) body.Append(',');
                    body.Append('"').Append(e.Reasons[i]).Append('"');
                }
                body.Append(']');
            }
            body.Append('}');

            context.Response.StatusCode = 422;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers[HeaderNames.CacheControl] = "no-store";
            MaybeEmitNetSupportHeader(context);
            var bytes = Encoding.UTF8.GetBytes(body.ToString());
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
        }

        private void MaybeEmitNetSupportHeader(HttpContext context)
        {
            if (!options.ExposeNetSupportHeader) return;
            var grid = options.SecurityPolicyValidator?.EffectiveNetSupport;
            if (grid == null) return;

            // Compact form: "formats=jpeg,png,webp;encoders=zen_jpeg_encoder,..."
            var sb = new StringBuilder();
            sb.Append("formats=");
            var first = true;
            foreach (var kv in grid.Formats)
            {
                if (!kv.Value.Encode) continue;
                if (!first) sb.Append(',');
                sb.Append(kv.Key);
                first = false;
            }
            context.Response.Headers["X-Imageflow-Net-Support"] = sb.ToString();
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
