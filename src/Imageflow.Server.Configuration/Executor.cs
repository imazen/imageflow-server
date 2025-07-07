using Imageflow.Fluent;
using Imageflow.Server.Configuration.Parsing;
using Imageflow.Server.HybridCache;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.DependencyInjection;

#nullable enable

namespace Imageflow.Server.Configuration.Execution;


internal record ExecutorContext(string SourcePath, Func<string,string, string> InterpolateString, IConfigRedactor Redactor, IAbstractFileMethods Fs);
internal class Executor : IAppConfigurator{
    //ImageflowConfig

    public Executor(ImageflowConfig config, ExecutorContext context){
        this.Context = context;
        this.config = config;
        this.sourcePath = context.SourcePath;
    }

    public ExecutorContext Context { get; }

    public bool RestartWhenThisFileChanges => config.AspnetServer?.RestartWhenThisFileChanges ?? false;

    private readonly ImageflowConfig config;
    private readonly string sourcePath;

    private string InterpolateString(string input, string fieldName){
        return Context.InterpolateString(input, fieldName);
    }
    

    public ImageflowMiddlewareOptions GetImageflowMiddlewareOptions(){
        var options = new ImageflowMiddlewareOptions();


        var routeDefaults = config.RouteDefaults;


        if (config.Routes != null){
            // Map physical path routes
            foreach (var route in config.Routes){
                //throw if from or to is null or whitespace
                if (string.IsNullOrWhiteSpace(route.Prefix)){
                    throw new ArgumentNullException($"route.prefix is missing. Defined in file '{sourcePath}'");
                }
                if (string.IsNullOrWhiteSpace(route.MapToPhysicalFolder)){
                    throw new ArgumentNullException($"route.map_to_physical_folder is missing (other route types not yet supported). Defined in file '{sourcePath}'");
                }
                var prefixCaseSensitive = route.PrefixCaseSensitive ?? routeDefaults?.PrefixCaseSensitive ?? true;
                var from = InterpolateString(route.Prefix, "route.prefix");
                var to = InterpolateString(route.MapToPhysicalFolder, "route.map_to_physical_folder");
                if (!Context.Fs.DirectoryExists(to)){
                    throw new DirectoryNotFoundException($"Folder '{to}' does not exist. Cannot route '{from}' to a non-existent folder. Create folder or modify [[route]] prefix='{to}' from='fix this' in '{sourcePath}' ");
                }
                options.MapPath(from, to, prefixCaseSensitive);
            }
        }
        // TODO: map physical paths
        // foreach(var map in config.MapPhysicalPath){

        // }
        // set license key
        EnforceLicenseWith enforcementMethod;
        // parse enforcement method string: http_402_error http_422_error watermark (default watermark)
        // throw if invalid
        if (string.Equals(config.License?.Enforcement, "watermark")){
            enforcementMethod = EnforceLicenseWith.RedDotWatermark;
        } else if (string.Equals(config.License?.Enforcement, "http_402_error")){
            enforcementMethod = EnforceLicenseWith.Http402Error;
        } else if (string.Equals(config.License?.Enforcement, "http_422_error")){
            enforcementMethod = EnforceLicenseWith.Http422Error;
        } else {
            throw new FormatException($"Invalid [license] enforcement= method '{config.License?.Enforcement}'. Valid values are 'watermark', 'http_402_error', and 'http_422_error'. Defined in file '{sourcePath}'");
        }
        if (config.License?.Key != null){
            options.SetLicenseKey(enforcementMethod, config.License.Key);
        }

        // set cache control
        if (config.RouteDefaults?.CacheControl != null){
            options.SetDefaultCacheControlString(config.RouteDefaults?.CacheControl);
        }

        // set diagnostics page access
        var access = AccessDiagnosticsFrom.None;
        if (config.Diagnostics?.AllowLocalhost ?? false){
            access = AccessDiagnosticsFrom.LocalHost;
        }
        if (config.Diagnostics?.AllowAnyhost ?? false){
            access = AccessDiagnosticsFrom.AnyHost;
        }
        options.SetDiagnosticsPageAccess(access);
        // set diagnostics page password
        if (!string.IsNullOrWhiteSpace(config.Diagnostics?.AllowWithPassword)){
            options.SetDiagnosticsPagePassword(config.Diagnostics.AllowWithPassword);
        }
        // set hybrid cache
        if (config.DiskCache?.Enabled ?? false){
            options.SetAllowCaching(true);
        }
        // default commands
        if (config.RouteDefaults?.ApplyDefaultCommands != null){
            // parse as querystring using ASP.NET.
            var querystring = '?' + config.RouteDefaults.ApplyDefaultCommands.TrimStart('?');
            var parsed = Imazen.Routing.Helpers.PathHelpers.ParseQuery(querystring);
            if (parsed != null)
                foreach (var (key, value) in parsed)
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        throw new ArgumentNullException(
                            $"route_defaults.apply_default_commands.key is missing. Defined in file '{sourcePath}'");
                    }

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        throw new ArgumentNullException(
                            $"route_defaults.apply_default_commands.value is missing. Defined in file '{sourcePath}' for key route_defaults.apply_default_commands.key '{key}'");
                    }
                    options.AddCommandDefault(key, value!);
                }
        }
        if (config.RouteDefaults?.ApplyDefaultCommandsToQuerylessUrls ?? false){
            options.SetApplyDefaultCommandsToQuerylessUrls(true);
        }

        // set security options
        var securityOptions = new SecurityOptions();
        if (config.Security?.MaxDecodeResolution != null){
            securityOptions.SetMaxDecodeSize(config.Security.MaxDecodeResolution.ToFrameSizeLimit());
        }
        if (config.Security?.MaxEncodeResolution != null){
            securityOptions.SetMaxEncodeSize(config.Security.MaxEncodeResolution.ToFrameSizeLimit());
        }
        if (config.Security?.MaxFrameResolution != null){
            securityOptions.SetMaxFrameSize(config.Security.MaxFrameResolution.ToFrameSizeLimit());
        }
        options.SetJobSecurityOptions(securityOptions);
        return options;
    }
    public RewriteOptions GetRewriteOptions(){
        var options = new RewriteOptions();
        var apache = config.AspnetServer?.ApacheModRewrite;
        // if (apache?.Text != null){
        //     options.AddApacheModRewrite(new StringReader(PreprocessApacheRewrites(apache.Text)));
        // }
        if (apache?.File != null){
            // read from string, throw exception if missing, and preprocess
            var path = InterpolateString(apache.File, "asp_net_server.apache_mod_rewrite.file");
            if (!Context.Fs.FileExists(path)){
                throw new FileNotFoundException($"Apache mod_rewrite file '{path}' does not exist. Defined in key asp_net_server.apache_mod_rewrite.file in file '{sourcePath}'");
            }
            var text = Context.Fs.ReadAllText(path);
            options.AddApacheModRewrite(new StringReader(PreprocessApacheRewrites(text)));
        }

        // var iis = config.AspNetServer?.IisUrlRewrite;
        // if (iis?.Text != null){
        //     options.AddIISUrlRewrite(new StringReader(iis.Text));
        // }
        // if (iis?.File != null){
        //     // read from string, throw exception if missing
        //     var path = this.varsParser.InterpolateString(iis.File, "asp_net_server.iis_url_rewrite.file");
        //     if (!Context.Fs.FileExists(path)){
        //         throw new FileNotFoundException($"IIS URL rewrite file '{path}' does not exist. Defined in key asp_net_server.iis_url_rewrite.file in file '{sourcePath}'");
        //     }
        //     var text = Context.Fs.ReadAllText(path);
        //     options.AddIISUrlRewrite(new StringReader(text));
        // }

        return options;
    }

    internal class ServerConfigurationOptions{
    
        public bool UseDeveloperExceptionPage { get; set; }
        public string? UseExceptionHandler { get; set; } = null;
        public bool UseHsts { get; set; }
        public bool UseHttpsRedirection { get; set; }
        public bool UseRewriter { get; set; }
        public bool UseRouting { get; set; }
        internal List<StaticResponse>? Endpoints { get; set; }

        // From ImageflowConfig
        internal ServerConfigurationOptions(ImageflowConfig config){
            UseDeveloperExceptionPage = config.AspnetServer?.UseDeveloperExceptionPage ?? false;
            UseExceptionHandler = config.AspnetServer?.UseExceptionHandler ?? null;
            UseHsts = config.AspnetServer?.UseHsts ?? false;
            UseHttpsRedirection = config.AspnetServer?.UseHttpsRedirection ?? false;
            UseRewriter = config.AspnetServer?.ApacheModRewrite?.File != null;
            //                 config.AspNetServer?.ApacheModRewrite?.File != null ||
            // UseRewriter = config.AspNetServer?.ApacheModRewrite?.Text != null ||
            //                 config.AspNetServer?.ApacheModRewrite?.File != null ||
            //                 config.AspNetServer?.IisUrlRewrite?.Text != null ||
            //                 config.AspNetServer?.IisUrlRewrite?.File != null;
            
            UseRouting = (config.StaticResponse?.Count ?? 0) > 0;
            Endpoints = config.StaticResponse;
        }

    }
    public ServerConfigurationOptions GetServerConfigurationOptions(){
        return new ServerConfigurationOptions(config);
    }

    // Because the msft implemention is maliciously incompetent
    internal static string PreprocessApacheRewrites(string text) => text.Replace("\\w", "[A-Za-z0-9_]").Replace("\\d", "[0-9]");
    


    public HybridCacheOptions GetHybridCacheOptions(){
        if (config.DiskCache == null){
            throw new InvalidOperationException("Cannot call GetHybridCacheOptions() when config.DiskCache is null.");
        } 
        var expandedFolder = config.DiskCache.Folder != null ? 
                                    InterpolateString(config.DiskCache.Folder, "disk_cache.folder") : null;
        if (expandedFolder == null){
            throw new InvalidOperationException("Cannot call GetHybridCacheOptions() when config.DiskCache.Folder is null.");
        }
        // require path exists
        if (!Context.Fs.DirectoryExists(expandedFolder)){
            var parent = Path.GetDirectoryName(expandedFolder);
            // or at least the parent folder
            if (parent == null || !Context.Fs.DirectoryExists(parent)){
                throw new DirectoryNotFoundException($"Hybrid cache folder '{expandedFolder}' and its parent do not exist. Defined in file '{sourcePath}'");
            }
        }

        var options = new HybridCacheOptions(expandedFolder);
        var CacheSizeMb = config.DiskCache.CacheSizeMb ?? 0;
        if (CacheSizeMb > 0){
            options.CacheSizeMb = CacheSizeMb;
        }
        var DatabaseShards = config.DiskCache.DatabaseShards ?? 0;
        if (DatabaseShards > 0){
            options.DatabaseShards = DatabaseShards;
        }
        var writeQueueRamMb = config.DiskCache.WriteQueueRamMb ?? 0;
        if (writeQueueRamMb > 0){
            //TODO:  warn ignored
            
        }
        
        var EvictionSweepSizeMb = config.DiskCache.EvictionSweepSizeMb ?? 0;
        if (EvictionSweepSizeMb > 0){
            options.EvictionSweepSizeMb = EvictionSweepSizeMb;
        }

        var SecondsUntilEvictable = config.DiskCache.SecondsUntilEvictable ?? 0;
        if (SecondsUntilEvictable > 0){
            options.MinAgeToDelete = TimeSpan.FromSeconds(SecondsUntilEvictable);
        }   

        return options;
    }
    public void ConfigureServices(IServiceCollection services){
        // Unlike ImageResizer, this MUST NOT be within the application directory.
        if (config.DiskCache?.Enabled ?? false){
            services.AddImageflowHybridCache(GetHybridCacheOptions());
        }

        services.ConfigureImageflowMiddleware(GetImageflowMiddlewareOptions());
    }
    public void ConfigureApp(IApplicationBuilder app, IWebHostEnvironment env){
            var options = GetServerConfigurationOptions();
            if (options.UseRewriter){
                app.UseRewriter(GetRewriteOptions());
            }
            if (options.UseDeveloperExceptionPage){
                app.UseDeveloperExceptionPage();
            }
            if (options.UseExceptionHandler != null){
                app.UseExceptionHandler(options.UseExceptionHandler);
            }
            if (options.UseHsts){
                app.UseHsts();
            }
            if (options.UseHttpsRedirection){
                app.UseHttpsRedirection();
            }
            app.UseImageflow();

            if (options.UseRouting){
                app.UseRouting();
            }

            var staticRoutes = options.Endpoints ?? new List<StaticResponse>();
            if (staticRoutes.Count > 0){
                app.UseEndpoints(endpoints =>
                {
                    foreach(var route in staticRoutes){
                        endpoints.MapGet(route.For, async context =>
                        {
                            // validate content type and status code
                            context.Response.ContentType = route.ContentType ?? "text/plain";
                            context.Response.StatusCode = route.StatusCode ?? 200;
                            // if route.File is null, route.Content must be non-null
                            if (route.File != null){
                                var expandedFile = InterpolateString(route.File, "static_response.file");
                                await context.Response.SendFileAsync(expandedFile);
                                return;
                            }else {
                                if (route.Content == null){
                                    throw new InvalidOperationException("Both route.File and route.Content are null.");
                                }else{
                                    await context.Response.WriteAsync(route.Content);
                                }
                            }
                            
                        });
                    }
                });
            }

    }

    public Dictionary<string, string> GetComputedConfiguration(bool redactSecrets)
    {
        var d = new Dictionary<string, string>();
        Utilities.Utilities.AddToDictionaryRecursive(GetImageflowMiddlewareOptions(), d, "ImageflowMiddlewareOptions");
        if (config.DiskCache?.Enabled == true){
            Utilities.Utilities.AddToDictionaryRecursive(GetHybridCacheOptions(), d, "HybridCacheOptions");
        }
        Utilities.Utilities.AddToDictionaryRecursive(GetServerConfigurationOptions(), d, "ServerConfigurationOptions");
        Utilities.Utilities.AddToDictionaryRecursive(RestartWhenThisFileChanges, d, "RestartWhenThisFileChanges");
        if (redactSecrets){
            foreach(var kvp in d){
                d[kvp.Key] = Context.Redactor.Redact(kvp.Value);
            }
        }
        return d;
    }
}
