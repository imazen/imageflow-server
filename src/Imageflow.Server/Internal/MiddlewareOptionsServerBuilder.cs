using System.Globalization;
using System.Text;
using Imageflow.Bindings;
using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Blobs.LegacyProviders;

using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Storage;
using Imazen.Routing.Engine;
using Imazen.Routing.Health;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Layers;
using Imazen.Routing.Promises;
using Imazen.Routing.Promises.Pipelines.Watermarking;
using Imazen.Routing.Requests;
using Imazen.Routing.Serving;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Imageflow.Server.Internal;

internal class MiddlewareOptionsServerBuilder(

        IServiceCollection services,
        ImageflowMiddlewareOptions options)
{


    public void RegisterServices()
    {


        // TODO: verify ASP.NET adds the webhostenvironment services.AddSingleton(env);
        services.AddSingleton(options);

        var diagPageOptions = new DiagnosticsPageOptions(
            options.DiagnosticsPassword,
            (Imazen.Routing.Layers.DiagnosticsPageOptions.AccessDiagnosticsFrom)options.DiagnosticsAccess);
        services.AddDiagnosticsPage(diagPageOptions);
        services.TryAddSingleton(
            new GlobalInfoProviderServiceCollectionReference(services)
        );
        services.TryAddSingleton<GlobalInfoProvider>();


        services.AddSingleton<LicenseOptions>(p =>
        {
            var env = p.GetRequiredService<IWebHostEnvironment>();
            var options = p.GetRequiredService<ImageflowMiddlewareOptions>();
            return new LicenseOptions
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
        });
        services.TryAddSingleton<ILicenseChecker>(p => Licensing.CreateAndEnsureManagerSingletonCreated(p.GetRequiredService<LicenseOptions>()));


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
        services.AddSingleton(watermarkingLogicOptions);
        services.AddSingleton<IRoutingEngine, LegacyRoutingEngine>();
        services.AddImageServer<RequestStreamAdapter, ResponseStreamAdapter, HttpContext>();

    }
}

internal class LegacyRoutingEngine : IRoutingEngine
{
    private IRoutingEngine routingEngine;

    public bool MightHandleRequest<TQ>(string path, TQ query) where TQ : IReadOnlyQueryWrapper => routingEngine.MightHandleRequest(path, query);
    public ValueTask<CodeResult<IRoutingEndpoint>?> Route(MutableRequest request, CancellationToken cancellationToken = default) => routingEngine.Route(request, cancellationToken);

    public ValueTask<CodeResult<ICacheableBlobPromise>?> RouteToPromiseAsync(MutableRequest request, CancellationToken cancellationToken = default) => routingEngine.RouteToPromiseAsync(request, cancellationToken);

    private readonly IReLogger logger;
    public LegacyRoutingEngine(
        ImageflowMiddlewareOptions options,
        IWebHostEnvironment env,
        IReLoggerFactory loggerFactory,
        ILicenseChecker licenseChecker,
        DiagnosticsPage diagnosticsPage,
        IEnumerable<IBlobWrapperProvider> blobWrapperProviders, 
        IEnumerable<IBlobWrapperProviderZoned> blobWrapperProvidersZoned,
        #pragma warning disable CS0618
        IEnumerable<IBlobProvider> blobProviders
        #pragma warning restore CS0618
        )
        {
            this.logger = loggerFactory.CreateReLogger("LegacyRoutingEngine");
        var mappedPaths = options.MappedPaths.Cast<IPathMapping>().ToList();
        if (options.MapWebRoot)
        {
            if (env?.WebRootPath == null)
                throw new InvalidOperationException("Cannot call MapWebRoot if env.WebRootPath is null");
            mappedPaths.Add(new PathMapping("/", env.WebRootPath));
        }

        var builder = new RoutingBuilder();
        builder.ConfigureMedia((media) =>
        {
            media.ConfigurePreconditions((preconditions) =>
            {
                preconditions.IncludeDefaultImageExtensions().IncludePathPrefixes(options.ExtensionlessPaths);
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
        
        // MutableRequestEventLayer (PreRewriteAuthorization), use lambdas 
        // to inject Context, and possibly also copy/restore dictionary.
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
                (IPathMapping)new PathMapping(a.VirtualPath, a.PhysicalPath, a.IgnorePrefixCase)).ToList(), logger));
        }


        builder.AddMediaLayer(new BlobProvidersLayer(blobProviders, blobWrapperProviders, logger));
        
        
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
                s.Append(licenseChecker.GetLicensePageContents());
                return SmallHttpResponse.NoStoreNoRobots((200, s.ToString()));
            });
            
    

        if (options.ExtraMediaFileExtensions != null)
        {
            builder.ConfigureMedia((media) =>
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
                action(builder);
            }
        }

        routingEngine = builder.Build(logger);
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


}
