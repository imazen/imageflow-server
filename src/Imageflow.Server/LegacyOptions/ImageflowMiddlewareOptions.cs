using Imageflow.Fluent;
using Imazen.Common.Instrumentation.Support.InfoAccumulators;
using Imazen.Routing.Engine;
using Imazen.Routing.Layers;
using Microsoft.Extensions.DependencyInjection;

namespace Imageflow.Server
{

    public class ImageflowMiddlewareOptions: IInfoProvider
    {
        internal record struct ExtensionlessPath(string StringToCompare, StringComparison StringComparison)
            : IStringAndComparison;
        
        internal string? LicenseKey { get; set; }
        internal EnforceLicenseWith EnforcementMethod { get; set; } = EnforceLicenseWith.Http422Error;
        
        //internal Licensing? Licensing { get; set; }

        public string? MyOpenSourceProjectUrl { get; set; } = "https://i-need-a-license.com";
        
        /// <summary>
        /// Set to true to allow any registered IStreamCache plugin (such as Imageflow.Server.HybridCache) to be used.
        /// </summary>
        public bool AllowCaching { get; set; } = true;
        /// <summary>
        /// Set to true to allow the legacy disk caching system (Imageflow.Server.DiskCache) to be used
        /// </summary>
        public bool AllowDiskCaching { get; set; } = true;

        // Removing
        // internal CacheBackend ActiveCacheBackend { get; set; }

        private readonly List<NamedWatermark> namedWatermarks = new List<NamedWatermark>();
        public IReadOnlyCollection<NamedWatermark> NamedWatermarks => namedWatermarks;
        
        private readonly List<PathMapping> mappedPaths = new List<PathMapping>();
        
        public IReadOnlyCollection<PathMapping> MappedPaths => mappedPaths;

        public bool MapWebRoot { get; set; }

        internal AccessDiagnosticsFrom DiagnosticsAccess { get; set; } = AccessDiagnosticsFrom.LocalHost;
        
        internal string? DiagnosticsPassword { get; set;  }

        public bool UsePresetsExclusively { get; set; }

        public string? DefaultCacheControlString { get; set; } = "";
        
        public RequestSignatureOptions RequestSignatureOptions { get; set; } = RequestSignatureOptions.Empty;
        public SecurityOptions JobSecurityOptions { get; set; } = new();

        internal List<string>? ExtraMediaFileExtensions;

        internal List<Action<RoutingBuilder>>? RoutingConfigurationActions;

        internal List<string>? RoutingExpressions;
        
        internal readonly List<PathPrefixHandler<Action<UrlEventArgs>>> Rewrite = [];

        internal readonly List<PathPrefixHandler<Func<UrlEventArgs, bool>>> PreRewriteAuthorization = [];

        internal readonly List<PathPrefixHandler<Func<UrlEventArgs, bool>>> PostRewriteAuthorization = [];

        internal readonly List<PathPrefixHandler<Action<WatermarkingEventArgs>>> Watermarking = [];


        internal readonly Dictionary<string, string> CommandDefaults = new(StringComparer.OrdinalIgnoreCase);
        public bool ApplyDefaultCommandsToQuerylessUrls { get; set; } = false;

        internal readonly Dictionary<string, PresetOptions> Presets = new(StringComparer.OrdinalIgnoreCase);
        
        internal readonly List<ExtensionlessPath> ExtensionlessPaths = [];
        /// <summary>
        /// Use this to add default command values if they are missing. Does not affect image requests with no querystring.
        /// Example: AddCommandDefault("down.colorspace", "srgb") reverts to ImageResizer's legacy behavior in scaling shadows and highlights.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public ImageflowMiddlewareOptions AddCommandDefault(string key, string value)
        {
            if (CommandDefaults.ContainsKey(key)) throw new ArgumentOutOfRangeException(nameof(key), "A default has already been added for this key");
            CommandDefaults[key] = value;
            return this;
        }
        
        public ImageflowMiddlewareOptions AddPreset(PresetOptions preset)
        {
            if (Presets.ContainsKey(preset.Name)) throw new ArgumentOutOfRangeException(nameof(preset), "A preset by this name has already been added");
            Presets[preset.Name] = preset;
            return this;
        }

        public ImageflowMiddlewareOptions HandleExtensionlessRequestsUnder(string prefix, StringComparison prefixComparison = StringComparison.Ordinal)
        {
            ExtensionlessPaths.Add(new ExtensionlessPath() { StringToCompare = prefix, StringComparison = prefixComparison});
            return this;
        }
        
        [Obsolete("This API is unstable and will be removed in a future release")]
        public ImageflowMiddlewareOptions AddRoutingConfiguration(Action<RoutingBuilder> configure)
        {
            RoutingConfigurationActions ??= new();
            RoutingConfigurationActions.Add(configure);
            return this;
        }


        public ImageflowMiddlewareOptions AddRoute(string route)
        {
            RoutingExpressions ??= new();
            RoutingExpressions.Add(route);
            return this;
        }
        
        /// <summary>
        /// Add a file extension to the list of extensions that Imageflow will process and/or cache. Must start with a period.
        /// </summary>
        /// <param name="extension"></param>
        /// <returns></returns>
        public ImageflowMiddlewareOptions AddMediaFileExtension(string extension)
        {
            if (!extension.StartsWith(".")) throw new ArgumentOutOfRangeException(nameof(extension), "Extension must start with a period");
            ExtraMediaFileExtensions ??= new();
            ExtraMediaFileExtensions.Add(extension);
            return this;
        }

        /// <summary>
        /// Control when and where the diagnostics page is accessible when no password is used
        /// </summary>
        /// <param name="accessDiagnosticsFrom"></param>
        /// <returns></returns>
        public ImageflowMiddlewareOptions SetDiagnosticsPageAccess(AccessDiagnosticsFrom accessDiagnosticsFrom)
        {
            DiagnosticsAccess = accessDiagnosticsFrom;
            return this; 
        }
        
        /// <summary>
        /// When set, the diagnostics page will be accessible form anywhere by adding ?password=[password]
        /// </summary>
        /// <param name="password"></param>
        /// <returns></returns>
        public ImageflowMiddlewareOptions SetDiagnosticsPagePassword(string password)
        {
            DiagnosticsPassword = password;
            return this; 
        }

        /// <summary>
        /// Use this if you are complying with the AGPL v3 and open-sourcing your project.
        /// Provide the URL to your version control system or source code download page.
        /// Use .SetLicenseKey() instead if you are not open-sourcing your project.
        /// </summary>
        /// <param name="myOpenSourceProjectUrl">Provide the URL to your version control
        /// system or source code download page.</param>
        /// <returns></returns>
        public ImageflowMiddlewareOptions SetMyOpenSourceProjectUrl(string myOpenSourceProjectUrl)
        {
            MyOpenSourceProjectUrl = myOpenSourceProjectUrl;
            return this;
        }
        
        /// <summary>
        /// If you do not call this, Imageflow.Server will watermark image requests with a red dot. 
        ///
        /// If you are open-sourcing your project and complying with the AGPL v3, you can call
        /// .SetMyOpenSourceProjectUrl() instead.
        /// </summary>
        /// <param name="licenseKey"></param>
        /// <param name="enforcementMethod"></param>
        /// <returns></returns>
        public ImageflowMiddlewareOptions SetLicenseKey(EnforceLicenseWith enforcementMethod, string licenseKey)
        {
            EnforcementMethod = enforcementMethod;
            LicenseKey = licenseKey;
            return this;
        }

 
        
        
        public ImageflowMiddlewareOptions SetRequestSignatureOptions(RequestSignatureOptions options)
        {
            RequestSignatureOptions = options;
            return this;
        }
        
        public ImageflowMiddlewareOptions AddRewriteHandler(string pathPrefix, Action<UrlEventArgs> handler)
        {
            Rewrite.Add(new PathPrefixHandler<Action<UrlEventArgs>>(pathPrefix, handler));
            return this;
        }
        public ImageflowMiddlewareOptions AddPreRewriteAuthorizationHandler(string pathPrefix, Func<UrlEventArgs, bool> handler)
        {
            PreRewriteAuthorization.Add(new PathPrefixHandler<Func<UrlEventArgs, bool>>(pathPrefix, handler));
            return this;
        }
        public ImageflowMiddlewareOptions AddPostRewriteAuthorizationHandler(string pathPrefix, Func<UrlEventArgs, bool> handler)
        {
            PostRewriteAuthorization.Add(new PathPrefixHandler<Func<UrlEventArgs, bool>>(pathPrefix, handler));
            return this;
        }

        public ImageflowMiddlewareOptions AddWatermarkingHandler(string pathPrefix, Action<WatermarkingEventArgs> handler)
        {
            Watermarking.Add(new PathPrefixHandler<Action<WatermarkingEventArgs>>(pathPrefix, handler));
            return this;
        }
        
        public ImageflowMiddlewareOptions SetMapWebRoot(bool value)
        {
            MapWebRoot = value;
            return this;
        }
        
        public ImageflowMiddlewareOptions SetJobSecurityOptions(SecurityOptions securityOptions)
        {
            JobSecurityOptions = securityOptions;
            return this;
        }
        
        /// <summary>
        /// If true, query strings will be discarded except for their preset key/value. Query strings without a preset key will throw an error. 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public ImageflowMiddlewareOptions SetUsePresetsExclusively(bool value)
        {
            UsePresetsExclusively = value;
            return this;
        }


        public ImageflowMiddlewareOptions MapPath(string virtualPath, string physicalPath)
            => MapPath(virtualPath, physicalPath, false);
        public ImageflowMiddlewareOptions MapPath(string virtualPath, string physicalPath, bool ignorePrefixCase)
        {
            mappedPaths.Add(new PathMapping(virtualPath, physicalPath, ignorePrefixCase));
            return this;
        }
        
        public ImageflowMiddlewareOptions AddWatermark(NamedWatermark watermark)
        {
            if (namedWatermarks.Any(w => w.Name?.Equals(watermark.Name, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                throw new InvalidOperationException($"A watermark already exists by the name {watermark.Name}");
            }
            namedWatermarks.Add(watermark);
            return this;
        }
        
        /// <summary>
        /// Set to true to allow any registered IStreamCache plugin (such as Imageflow.Server.HybridCache) to be used.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public ImageflowMiddlewareOptions SetAllowCaching(bool value)
        {
            this.AllowCaching = value;
            return this;
        }

        /// <summary>
        /// Set to true to allow the legacy disk caching system (Imageflow.Server.DiskCache) to be used
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public ImageflowMiddlewareOptions SetAllowDiskCaching(bool value)
        {
            this.AllowDiskCaching = value;
            return this;
        }

        /// <summary>
        /// Use "public, max-age=2592000" to cache for 30 days and cache on CDNs and proxies.
        /// </summary>
        /// <param name="cacheControlString"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public ImageflowMiddlewareOptions SetDefaultCacheControlString(string? cacheControlString)
        {
            DefaultCacheControlString = cacheControlString;
            return this;
        }

        /// <summary>
        /// Set to true have the command defaults to all urls, not just ones containing a valid query command.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public ImageflowMiddlewareOptions SetApplyDefaultCommandsToQuerylessUrls(bool value)
        {
            this.ApplyDefaultCommandsToQuerylessUrls = value;
            return this;
        }

        public void Add(IInfoAccumulator query)
        {
            query.Add("map_web_root", MapWebRoot);
            query.Add("use_presets_exclusively", UsePresetsExclusively);
            query.Add("request_signing_default", RequestSignatureOptions?.DefaultRequirement.ToString() ?? "never");
            query.Add("default_cache_control", DefaultCacheControlString ?? "");
            query.Add("default_commands", Imazen.Routing.Helpers.PathHelpers.SerializeCommandString(CommandDefaults));

        }
    }
}