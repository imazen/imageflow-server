using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Imageflow.Fluent;
using Imazen.Common.Helpers;
using Imazen.Common.Instrumentation;
using Imazen.Common.Storage;
using Microsoft.AspNetCore.Http;

namespace Imageflow.Server
{
    internal class ImageJobInfo
    {
        public static bool ShouldHandleRequest(HttpContext context, ImageflowMiddlewareOptions options)
        {
            // If the path is empty or null we don't handle it
            var pathValue = context.Request.Path;
            if (pathValue == null || !pathValue.HasValue)
                return false;

            var path = pathValue.Value;
            if (path == null)
                return false;

            // We handle image request extensions
            if (PathHelpers.IsImagePath(path))
            {
                return true;
            }

            // Don't do string parsing unless there are actually prefixes configured
            if (options.ExtensionlessPaths.Count == 0) return false;
            
            // If there's no extension, then we can see if it's one of the prefixes we should handle
            var extension = Path.GetExtension(path);
            // If there's a non-image extension, we shouldn't handle the request
            if (!string.IsNullOrEmpty(extension)) return false;

            // Return true if any of the prefixes match
            return options.ExtensionlessPaths
                .Any(extensionlessPath => path.StartsWith(extensionlessPath.Prefix, extensionlessPath.PrefixComparison));
        }
        public ImageJobInfo(HttpContext context, ImageflowMiddlewareOptions options, BlobProvider blobProvider)
        {
            this.options = options;
            Authorized = ProcessRewritesAndAuthorization(context, options);

            if (!Authorized) return;

            HasParams = PathHelpers.SupportedQuerystringKeys.Any(FinalQuery.ContainsKey);

            // Get the image and page domains
            ImageDomain = context.Request.Host.Host;
            var referer = context.Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var result))
            {
                PageDomain = result.DnsSafeHost;
            }
       
            var extension = Path.GetExtension(FinalVirtualPath);
            if (FinalQuery.TryGetValue("format", out var newExtension))
            {
                extension = newExtension;
            }

            EstimatedFileExtension = PathHelpers.SanitizeImageExtension(extension) ?? "jpg";

            primaryBlob = new BlobFetchCache(FinalVirtualPath, blobProvider);
            allBlobs = new List<BlobFetchCache>(1) {primaryBlob};

            appliedWatermarks = new List<NamedWatermark>();
            
            if (HasParams)
            {
                if (options.Licensing.RequestNeedsEnforcementAction(context.Request))
                {
                    if (options.EnforcementMethod == EnforceLicenseWith.RedDotWatermark)
                    {
                        FinalQuery["watermark_red_dot"] = "true";
                    }
                    LicenseError = true;
                }
                
                CommandString = PathHelpers.SerializeCommandString(FinalQuery);

                // Look up watermark names
                if (FinalQuery.TryGetValue("watermark", out var watermarkValues))
                {
                    var watermarkNames = watermarkValues.Split(",").Select(s => s.Trim(' '));
                    foreach (var name in watermarkNames)
                    {
                        var watermark = options.NamedWatermarks.FirstOrDefault(w =>
                            w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (watermark == null)
                        {
                            throw new InvalidOperationException(
                                $"watermark {name} was referenced from the querystring but no watermark by that name is registered with the middleware");
                        }

                        appliedWatermarks.Add(watermark);
                    }
                }
            }

            // After we've populated the defaults, run the event handlers for custom watermarking logic
            var args = new WatermarkingEventArgs(context, FinalVirtualPath, FinalQuery, appliedWatermarks);
            foreach (var handler in options.Watermarking)
            {
                var matches = string.IsNullOrEmpty(handler.PathPrefix) ||
                              ( FinalVirtualPath != null &&
                              FinalVirtualPath.StartsWith(handler.PathPrefix, StringComparison.OrdinalIgnoreCase));
                if (matches) handler.Handler(args);
            }
            appliedWatermarks = args.AppliedWatermarks;
            if (appliedWatermarks.Count > 0)
            {
                HasParams = true; 
            }

            // Add the watermark source files
            foreach (var w in appliedWatermarks)
            {
                allBlobs.Add(new BlobFetchCache(w.VirtualPath, blobProvider));
            }
        }

        public string FinalVirtualPath { get; private set; }

        private Dictionary<string,string> FinalQuery { get; set; }
        public bool HasParams { get; }
        
        public bool Authorized { get; }

        public bool LicenseError { get; }
        public string CommandString { get; } = "";
        public string EstimatedFileExtension { get; }
        public string AuthorizedMessage { get; private set; }
        
        private string ImageDomain { get; }
        private string PageDomain { get; }

        private readonly List<NamedWatermark> appliedWatermarks;

        private readonly List<BlobFetchCache> allBlobs;
        private readonly BlobFetchCache primaryBlob;
        private readonly ImageflowMiddlewareOptions options;

        private bool ProcessRewritesAndAuthorization(HttpContext context, ImageflowMiddlewareOptions middlewareOptions)
        {
            if (!VerifySignature(context, middlewareOptions))
            {
                AuthorizedMessage = "Invalid request signature";
                return false;
            }
                
            var path = context.Request.Path.Value;
            var args = new UrlEventArgs(context, context.Request.Path.Value, PathHelpers.ToQueryDictionary(context.Request.Query));

            
            GlobalPerf.Singleton.PreRewriteQuery(args.Query.Keys);
            
            foreach (var handler in middlewareOptions.PreRewriteAuthorization)
            {
                var matches = string.IsNullOrEmpty(handler.PathPrefix) ||
                              path.StartsWith(handler.PathPrefix, StringComparison.OrdinalIgnoreCase);
                if (matches && !handler.Handler(args)) return false;
            }

            if (middlewareOptions.UsePresetsExclusively)
            {
                var firstKey = args.Query.FirstOrDefault().Key;
                
                if (args.Query.Count > 1 || (firstKey != null && firstKey != "preset"))
                {
                    AuthorizedMessage = "Only presets are permitted in the querystring";
                    return false;
                }
            }

            // Parse and apply presets before rewriting
            if (args.Query.TryGetValue("preset", out var presetNames))
            {
                var presetNamesList = presetNames
                    .Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
                foreach (var presetName in presetNamesList)
                {
                    if (middlewareOptions.Presets.TryGetValue(presetName, out var presetOptions))
                    {
                        foreach (var pair in presetOptions.Pairs)
                        {
                            if (presetOptions.Priority == PresetPriority.OverrideQuery ||
                                !args.Query.ContainsKey(pair.Key))
                            {
                                args.Query[pair.Key] = pair.Value;
                            }
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"The image preset {presetName} was referenced from the querystring but is not registered.");
                    }
                }
            }
            
            // Apply rewrite handlers
            foreach (var handler in middlewareOptions.Rewrite)
            {
                var matches = string.IsNullOrEmpty(handler.PathPrefix) ||
                              path.StartsWith(handler.PathPrefix, StringComparison.OrdinalIgnoreCase);
                if (matches)
                {
                    handler.Handler(args);
                    path = args.VirtualPath;
                }
            }

            // Set defaults if keys are missing, but at least 1 supported key is present
            if (middlewareOptions.ApplyDefaultCommandsToQuerylessUrls || PathHelpers.SupportedQuerystringKeys.Any(args.Query.ContainsKey))
            {
                foreach (var pair in middlewareOptions.CommandDefaults)
                {
                    if (!args.Query.ContainsKey(pair.Key))
                    {
                        args.Query[pair.Key] = pair.Value;
                    }
                }
            }

            // Run post-rewrite authorization
            foreach (var handler in middlewareOptions.PostRewriteAuthorization)
            {
                var matches = string.IsNullOrEmpty(handler.PathPrefix) ||
                              path.StartsWith(handler.PathPrefix, StringComparison.OrdinalIgnoreCase);
                if (matches && !handler.Handler(args)) return false;
            }

            FinalVirtualPath = args.VirtualPath;
            FinalQuery = args.Query;
            return true; 
        }

        private bool VerifySignature(HttpContext context, ImageflowMiddlewareOptions middlewareOptions)
        {
            if (middlewareOptions.RequestSignatureOptions == null) return true;

            var (requirement, signingKeys) = middlewareOptions.RequestSignatureOptions
                .GetRequirementForPath(context.Request.Path.Value);

            var queryString = context.Request.QueryString.ToString();

            var pathAndQuery = context.Request.PathBase.HasValue
                ? "/" + context.Request.PathBase.Value.TrimStart('/')
                : "";
            pathAndQuery += context.Request.Path.ToString() + queryString;

            pathAndQuery = Signatures.NormalizePathAndQueryForSigning(pathAndQuery);
            if (context.Request.Query.TryGetValue("signature", out var actualSignature))
            {
                foreach (var key in signingKeys)
                {
                    var expectedSignature = Signatures.SignString(pathAndQuery, key, 16);
                    if (expectedSignature == actualSignature) return true;
                }

                AuthorizedMessage = "Image signature does not match request, or used an invalid signing key.";
                return false;

            }

            if (requirement == SignatureRequired.Never)
            {
                return true;
            }
            if (requirement == SignatureRequired.ForQuerystringRequests)
            {
                if (queryString.Length <= 0) return true;
                
                AuthorizedMessage = "Image processing requests must be signed. No &signature query key found. ";
                return false;
            }
            AuthorizedMessage = "Image requests must be signed. No &signature query key found. ";
            return false;

        }


        public bool PrimaryBlobMayExist()
        {
            // Just returns a lambda for performing the actual fetch, does not actually call .Fetch() on providers
            return primaryBlob.GetBlobResult() != null;
        }

        public bool NeedsCaching()
        {
            return HasParams || primaryBlob?.GetBlobResult()?.IsFile == false;
        }
      
        public Task<IBlobData> GetPrimaryBlob()
        {
            return primaryBlob.GetBlob();
        }

        private string HashStrings(IEnumerable<string> strings)
        {
            return PathHelpers.Base64Hash(string.Join('|',strings));
        }

        internal async Task CopyPrimaryBlobToAsync(Stream stream)
        {

            await using var sourceStream = (await GetPrimaryBlob()).OpenRead();
            var oldPosition = stream.Position;
            await sourceStream.CopyToAsync(stream);
            if (stream.Position - oldPosition == 0)
            {
                throw new InvalidOperationException("Source blob has zero bytes; will not proxy.");
            }
        }

        internal async Task<byte[]> GetPrimaryBlobBytesAsync()
        {
            await using var sourceStream = (await GetPrimaryBlob()).OpenRead();
            var ms = new MemoryStream(sourceStream.CanSeek ? (int)sourceStream.Length : 4096);
            await sourceStream.CopyToAsync(ms);
            var buffer = ms.ToArray();
            if (buffer.Length == 0)
            {
                throw new InvalidOperationException("Source blob has length of zero bytes; will not proxy.");
            }
            return buffer;
        }

        private string[] serializedWatermarkConfigs;
        private IEnumerable<string> SerializeWatermarkConfigs()
        {
            if (serializedWatermarkConfigs != null) return serializedWatermarkConfigs;
            if (appliedWatermarks == null) return Enumerable.Empty<string>();
            serializedWatermarkConfigs = appliedWatermarks.SelectMany(w => w.Serialized()).ToArray();
            return serializedWatermarkConfigs;
        }

        public async Task<string> GetFastCacheKey()
        {
            // Only get DateTime values from local files
            var dateTimes = await Task.WhenAll(
                allBlobs
                    .Where(b => b.GetBlobResult()?.IsFile == true)
                    .Select(async b =>
                        (await b.GetBlob())?.LastModifiedDateUtc?.ToBinary().ToString()));
            
            return HashStrings(new [] {FinalVirtualPath, CommandString}.Concat(dateTimes).Concat(SerializeWatermarkConfigs()));
        }

        public override string ToString()
        {
            return CommandString;
        }

        public async Task<string> GetExactCacheKey()
        {
            var dateTimes = await Task.WhenAll(
                allBlobs
                    .Select(async b =>
                        (await b.GetBlob())?.LastModifiedDateUtc?.ToBinary().ToString()));
            
            return HashStrings(new [] {FinalVirtualPath, CommandString}.Concat(dateTimes).Concat(SerializeWatermarkConfigs()));
        }

        private class BlobFetchResult: IDisposable
        {
            private IBlobData blob;
            private StreamSource streamSource;
            private ArraySegment<byte> bytes;


            internal BytesSource GetBytesSource()
            {
                return new BytesSource(bytes);
            }
            public void Dispose()
            {
                streamSource?.Dispose();
                blob?.Dispose();
            }

            public static async Task<BlobFetchResult> FromCache(BlobFetchCache blobFetchCache)
            {
                var sw = Stopwatch.StartNew();
                using var blob = await blobFetchCache.GetBlob();
                if (blob == null) return null;
                
                var source = new StreamSource(blob.OpenRead(), true);
                var result = new BlobFetchResult()
                {
                    streamSource = source,
                    blob = blob,
                    bytes = await source.GetBytesAsync(CancellationToken.None)
                };
                sw.Stop();
                GlobalPerf.BlobRead(sw.ElapsedTicks, result.bytes.Count);
                return result;
            }
        }

        public async Task<ImageData> ProcessUncached()
        {
            //Fetch all blobs simultaneously
            var blobs = await Task.WhenAll(
                allBlobs
                    .Select(BlobFetchResult.FromCache));
            try
            {
                //Add all 
                List<InputWatermark> watermarks = null;
                if (appliedWatermarks != null)
                {
                    watermarks = new List<InputWatermark>(appliedWatermarks.Count);
                    watermarks.AddRange(
                        appliedWatermarks.Select((t, i) =>
                        {
                            if (blobs[i + 1] == null)
                                throw new BlobMissingException(
                                    $"Cannot locate watermark \"{t.Name}\" at virtual path \"{t.VirtualPath}\"");
                            return new InputWatermark(
                                blobs[i + 1].GetBytesSource(),
                                t.Watermark);
                        }));
                }

                using var buildJob = new ImageJob();
                var jobResult = await buildJob.BuildCommandString(
                        blobs[0].GetBytesSource(),
                        new BytesDestination(), CommandString, watermarks)
                    .Finish()
                    .SetSecurityOptions(options.JobSecurityOptions)
                    .InProcessAsync();
                
                GlobalPerf.Singleton.JobComplete(new ImageJobInstrumentation(jobResult)
                {
                    FinalCommandKeys = FinalQuery.Keys,
                    ImageDomain = ImageDomain,
                    PageDomain = PageDomain
                });
                
                // TryGetBytes returns the buffer from a regular MemoryStream, not a recycled one
                var resultBytes = jobResult.First.TryGetBytes();

                if (!resultBytes.HasValue || resultBytes.Value.Count < 1 || resultBytes.Value.Array == null)
                {
                    throw new InvalidOperationException("Image job returned zero bytes.");
                }

                return new ImageData
                {
                    ContentType = jobResult.First.PreferredMimeType,
                    ResultBytes = resultBytes.Value
                };
            }
            finally
            {
                foreach (var b in blobs)
                {
                    b?.Dispose();
                }
            }
        }
    }
    
    internal static class PerformanceDetailsExtensions{
        
        private static long GetWallMicroseconds(this PerformanceDetails d, Func<string, bool> nodeFilter)
        {
            long totalMicroseconds = 0;
            foreach (var frame in d.Frames)
            {
                foreach (var node in frame.Nodes.Where(n => nodeFilter(n.Name)))
                {
                    totalMicroseconds += node.WallMicroseconds;
                }
            }

            return totalMicroseconds;
        }
        
  
        
        public static long GetTotalWallTicks(this PerformanceDetails d) =>
            d.GetWallMicroseconds(n => true) * TimeSpan.TicksPerSecond / 1000000;
        
        public static long GetEncodeWallTicks(this PerformanceDetails d) =>
            d.GetWallMicroseconds(n => n == "primitive_encoder") * TimeSpan.TicksPerSecond / 1000000;
        
        public static long GetDecodeWallTicks(this PerformanceDetails d) =>
            d.GetWallMicroseconds(n => n == "primitive_decoder") * TimeSpan.TicksPerSecond / 1000000;
    }
}
