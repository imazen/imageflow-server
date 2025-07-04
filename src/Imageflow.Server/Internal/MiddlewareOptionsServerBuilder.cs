using System.Globalization;
using System.Text;
using Imageflow.Bindings;
using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Blobs.LegacyProviders;
using Imazen.Abstractions.DependencyInjection;
using Imazen.Abstractions.Logging;
using Imazen.Common.Storage;
using Imazen.Routing.Engine;
using Imazen.Routing.Health;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Layers;
using Imazen.Routing.Promises.Pipelines.Watermarking;
using Imazen.Routing.Requests;
using Imazen.Routing.Serving;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Imageflow.Server.Internal;

internal class MiddlewareOptionsServerBuilder(
    ImageServerContainer serverContainer,
    IReLogger logger, 
    IReLogStore logStore, 
    ImageflowMiddlewareOptions options, 
    IWebHostEnvironment env)
{

    private DiagnosticsPage diagnosticsPage = null!;
    public void PopulateServices()
    {
        
        var mappedPaths = options.MappedPaths.Cast<IPathMapping>().ToList();
        if (options.MapWebRoot)
        {
            if (env?.WebRootPath == null)
                throw new InvalidOperationException("Cannot call MapWebRoot if env.WebRootPath is null");
            mappedPaths.Add(new PathMapping("/", env.WebRootPath));
        }
        
        serverContainer.Register<IReLogger>(logger);
        serverContainer.Register<IReLogStore>(logStore);
        serverContainer.Register<IWebHostEnvironment>(env);
        serverContainer.Register<ImageflowMiddlewareOptions>(options);
        serverContainer.CopyFromOuter<IBlobCache>();
        serverContainer.CopyFromOuter<IBlobCacheProvider>();
        serverContainer.CopyFromOuter<ILicenseChecker>();
#pragma warning disable CS0618 // Type or member is obsolete
        serverContainer.CopyFromOuter<IBlobProvider>();
#pragma warning restore CS0618 // Type or member is obsolete
        serverContainer.CopyFromOuter<IBlobWrapperProvider>();
        var perfTracker = new NullPerformanceTracker();
        serverContainer.Register<IPerformanceTracker>(perfTracker);


        var licensingOptions = new LicenseOptions
        {
            LicenseKey = options.LicenseKey,
            MyOpenSourceProjectUrl = options.MyOpenSourceProjectUrl,
            ProcessWideKeyPrefixDefault = "imageflow_",
            ProcessWideCandidateCacheFoldersDefault = new[]
            {
                env.ContentRootPath,
                Path.GetTempPath()
            },
            EnforcementMethod = (Imazen.Routing.Serving.EnforceLicenseWith)options.EnforcementMethod

        };
        serverContainer.Register(licensingOptions);
        
        var diagPageOptions = new DiagnosticsPageOptions(
            options.DiagnosticsPassword,
            (Imazen.Routing.Layers.DiagnosticsPageOptions.AccessDiagnosticsFrom)options.DiagnosticsAccess);
        serverContainer.Register(diagPageOptions);
        
        diagnosticsPage = new DiagnosticsPage(serverContainer, logger, logStore,diagPageOptions);

        // Do watermark settings mappings
        WatermarkingLogicOptions? watermarkingLogicOptions = null;
        
        
        watermarkingLogicOptions = new WatermarkingLogicOptions(
            (name) =>
            {
                var match = options.NamedWatermarks.FirstOrDefault(a =>
                    name.Equals(a.Name, StringComparison.OrdinalIgnoreCase));
                if (match == null) return null;
                return new Imazen.Routing.Promises.Pipelines.Watermarking.WatermarkWithPath(
                    match.Name,
                    match.VirtualPath,
                    match.Watermark);
            },
            
            (IRequestSnapshot request, IList<WatermarkWithPath>? list) =>
            {
                if (options.Watermarking.Count == 0) return list;
                var startingList = list?.Select(NamedWatermark.From).ToList() ?? [];

                var args = new WatermarkingEventArgs(
                    request.OriginatingRequest?.GetHttpContextUnreliable<HttpContext>(),
                    request.Path, request.QueryString?.ToStringDictionary() ?? new Dictionary<string, string>(),
                    startingList);
                foreach (var handler in options.Watermarking)
                {
                    handler.Handler(args);
                }
                // We're ignoring any changes to the query or path.
                list = args.AppliedWatermarks.Select(WatermarkWithPath.FromIWatermark).ToList();
                return list;
            });
        serverContainer.Register(watermarkingLogicOptions);
        
        
        var licenseChecker = serverContainer.GetService<ILicenseChecker>() ??
                             Licensing.CreateAndEnsureManagerSingletonCreated(licensingOptions);

        var routingBuilder = new RoutingBuilder();
        var router = CreateRoutes(routingBuilder,mappedPaths, options.ExtensionlessPaths, licenseChecker);
        

        if (options.ExtraMediaFileExtensions != null)
        {
            routingBuilder.ConfigureMedia((media) =>
            {
                media.ConfigurePreconditions((preconditions) =>
                {
                    preconditions.IncludeFileExtensions(options.ExtraMediaFileExtensions.ToArray());
                });
            });
        }
        if (options.RoutingConfigurationActions != null)
        {
            foreach (var action in options.RoutingConfigurationActions)
            {
                action(router);
            }
        }

        var routingEngine = router.Build(logger);
        
        serverContainer.Register(routingEngine);
        
       
        //TODO: Add a way to get the current ILicenseChecker
        var imageServer = new ImageServer<RequestStreamAdapter,ResponseStreamAdapter, HttpContext>(
            serverContainer,
            licenseChecker,
            licensingOptions,  
            routingEngine, perfTracker, logger);
        
        serverContainer.Register<IImageServer<RequestStreamAdapter,ResponseStreamAdapter, HttpContext>>(imageServer);
        
        // Log any issues with the startup configuration
        new StartupDiagnostics(serverContainer).LogIssues(logger);
    }
    private PathPrefixHandler<Func<MutableRequestEventArgs, bool>> WrapUrlEventArgs(string pathPrefix,
        Func<UrlEventArgs, bool> handler, bool readOnly)
    {
        return new PathPrefixHandler<Func<MutableRequestEventArgs, bool>>(pathPrefix, (args) =>
        {
            var httpContext = args.Request.OriginatingRequest?.GetHttpContextUnreliable<HttpContext>();
            var dict = args.Request.MutableQueryString.ToStringDictionary();
            var e = new UrlEventArgs(httpContext, args.VirtualPath, dict);
            var result = handler(e);
            // We discard any changes to the query string or path.
            if (readOnly)
                return result;
            args.Request.MutablePath = e.VirtualPath;
            // Parse StringValues into a dictionary
            args.Request.MutableQueryString = 
                e.Query.ToStringValuesDictionary();
                
            return result;
        });
    }
    private RoutingBuilder CreateRoutes(RoutingBuilder builder, IReadOnlyCollection<IPathMapping> mappedPaths,
        IEnumerable<ImageflowMiddlewareOptions.ExtensionlessPath> optionsExtensionlessPaths, ILicenseChecker licenseChecker)
    {
        builder.ConfigureMedia((media) =>
        {
            media.ConfigurePreconditions((preconditions) =>
            {
                preconditions.IncludeDefaultImageExtensions().IncludePathPrefixes(optionsExtensionlessPaths);
            });
        });
        
        // signature layer
        var signatureOptions = options.RequestSignatureOptions;
        if (signatureOptions is { IsEmpty: false })
        {
            var newOpts = new Imazen.Routing.Layers.RequestSignatureOptions(
                (Imazen.Routing.Layers.SignatureRequired)signatureOptions.DefaultRequirement, signatureOptions.DefaultSigningKeys)
                .AddAllPrefixes(signatureOptions.Prefixes);
            builder.AddMediaLayer(new SignatureVerificationLayer(new SignatureVerificationLayerOptions(newOpts)));
        }
        
        //GlobalPerf.Singleton.PreRewriteQuery(request.GetQuery().Keys);
        
        // MutableRequestEventLayer (PreRewriteAuthorization), use lambdas to inject Context, and possibly also copy/restore dictionary.
        if (options.PreRewriteAuthorization.Count > 0)
        {
            builder.AddMediaLayer(new MutableRequestEventLayer("PreRewriteAuthorization",
                options.PreRewriteAuthorization.Select(
                    h => WrapUrlEventArgs(h.PathPrefix, h.Handler, true)).ToList()));
        }
        
        // Preset expansion layer
        if (options.Presets.Count > 0)
        {
            builder.AddMediaLayer(new PresetsLayer(new PresetsLayerOptions()
            {
                Presets = options.Presets.Values
                    .Select(a => new 
                        Imazen.Routing.Layers.PresetOptions(a.Name, (Imazen.Routing.Layers.PresetPriority)a.Priority, a.Pairs))
                    .ToDictionary(a => a.Name, a => a),
                UsePresetsExclusively = options.UsePresetsExclusively,
            }));
        }
        
        // MutableRequestEventLayer (Rewrites), use lambdas to inject Context, and possibly also copy/restore dictionary.
        if (options.Rewrite.Count > 0)
        {
            builder.AddMediaLayer(new MutableRequestEventLayer("Rewrites", options.Rewrite.Select(
                h => WrapUrlEventArgs(h.PathPrefix, (urlArgs) =>
                {
                    h.Handler(urlArgs); return true;
                }, false)).ToList()));
        }
        
        // Apply command defaults
        // TODO: only to already processing images?
        if (options.CommandDefaults.Count > 0)
        {
            builder.AddMediaLayer(new CommandDefaultsLayer(new CommandDefaultsLayerOptions()
            {
                CommandDefaults = options.CommandDefaults,
            }));
        }

        // MutableRequestEventLayer (PostRewriteAuthorization), use lambdas to inject Context, and possibly also copy/restore dictionary.
        if (options.PostRewriteAuthorization.Count > 0)
        {
            builder.AddMediaLayer(new MutableRequestEventLayer("PostRewriteAuthorization",
                options.PostRewriteAuthorization.Select(
                    h => WrapUrlEventArgs(h.PathPrefix, h.Handler, true)).ToList()));
        }
        
        //TODO: Add a layer that can be used to set the cache key basis
        builder.AddMediaLayer(new LicensingLayer(licenseChecker));
        

        if (mappedPaths.Count > 0)
        {
            builder.AddMediaLayer(new LocalFilesLayer(mappedPaths.Select(a => 
                (IPathMapping)new PathMapping(a.VirtualPath, a.PhysicalPath, a.IgnorePrefixCase)).ToList()));
        }

#pragma warning disable CS0618 // Type or member is obsolete
        var blobProviders = serverContainer.GetService<IEnumerable<IBlobProvider>>()?.ToList();
#pragma warning restore CS0618 // Type or member is obsolete
        var blobWrapperProviders = serverContainer.GetService<IEnumerable<IBlobWrapperProvider>>()?.ToList();
        if (blobProviders?.Count > 0 || blobWrapperProviders?.Count > 0)
        {
            builder.AddMediaLayer(new BlobProvidersLayer(blobProviders, blobWrapperProviders));
        }
        
        builder.AddEndpointLayer(diagnosticsPage);

        // We don't want signature requirements and the like applying to these endpoints.
        // Media delivery endpoints should be a separate thing...
        
        builder.AddEndpoint(Conditions.HasPathSuffix("/imageflow.ready"),
                (_) =>
                {
                    using (new JobContext())
                    {
                        return SmallHttpResponse.NoStore(200, "Imageflow.Server is ready to accept requests.");
                    }
                });
            
        builder.AddEndpoint(Conditions.HasPathSuffix("/imageflow.health"),
            (_) =>
            {
                return SmallHttpResponse.NoStore(200, "Imageflow.Server is healthy.");
            });
            
        builder.AddEndpoint(Conditions.HasPathSuffix("/imageflow.license", "/resizer.license"),
            (req) =>
            {
                var s = new StringBuilder(8096);
                var now = DateTime.UtcNow.ToString(NumberFormatInfo.InvariantInfo);
                s.AppendLine($"License page for Imageflow at {req.OriginatingRequest?.GetHost().Value} generated {now} UTC");
                var licenser = serverContainer.GetRequiredService<ILicenseChecker>(); 
                s.Append(licenser.GetLicensePageContents());
                return SmallHttpResponse.NoStoreNoRobots((200, s.ToString()));
            });
            
    
        return builder;
    }
}
