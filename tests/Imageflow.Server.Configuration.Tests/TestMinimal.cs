using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Imageflow.Server.Configuration.Tests;

internal class TestFileMethods : IAbstractFileMethods
{
    public TestFileMethods(Dictionary<string, string> files)
    {
        this.Files = files;
    }
    public Dictionary<string, string> Files { get; }
    public string ReadAllText(string path) => Files[path];
    public bool FileExists(string path) => Files.ContainsKey(path);
    public bool DirectoryExists(string path) => true; // pretend
}
public class MinimalTomlTest
{
    private readonly ITestOutputHelper output;
    // xunit writer
    public MinimalTomlTest(ITestOutputHelper output)
    {
        this.output = output;
    }
    // We want to test GetHybridCacheOptions, GetImageflowMiddlewareOptions, GetRewriteOptions, GetServerConfigurationOptions
    // We want to test the string interpolator (InterpolateString) and the config parser (ConfigurationParser)
    // We want to test the variable evaluator, including fallback and nested variable references
    // We want to test defaults for everything, as well as for environment based stuff
    [Theory]
    [InlineData(DeploymentEnvironment.Production)]
    [InlineData(DeploymentEnvironment.Development)]
    [InlineData(DeploymentEnvironment.Staging)]
    public void TestComputedOptions(DeploymentEnvironment environment)
    {
        Dictionary<string, string> appVars = new(){
            {"approot", "D:\\inetpub\\site"},
            {"wwwroot", "D:\\inetpub\\site\\wwwroot"}
        };
        var context = new TomlParserContext(environment, appVars,
            key =>
            {
                return key.ToLowerInvariant() switch
                {
                    "homedrive" => "D:",
                    "homepath" => "\\inetpub\\site",
                    _ => null,
                };
            },
            new TestFileMethods(new Dictionary<string, string>{
                {"rewrites.ini", ""}
            })
        );

        var result = TomlParser.Parse(MINIMAL_TOML, "embedded.minimal.toml", context);
        var executor = result.GetAppConfigurator();
        var computed = executor.GetComputedConfiguration(false);

        var expected = environment switch
        {
            DeploymentEnvironment.Production => MINIMAL_TOML_EXPECTED_PROD,
            DeploymentEnvironment.Staging => MINIMAL_TOML_EXPECTED_STAGING,
            DeploymentEnvironment.Development => MINIMAL_TOML_EXPECTED_DEV,
            _ => throw new Exception("Unknown environment")
        };

        try
        {
            Assert.Equal(expected, computed);
        }
        catch
        {
            // We want to diff strings for better visibility
            var expectedText = Utilities.Utilities.DictionaryToCSharp("computed", expected);
            var actualText = Utilities.Utilities.DictionaryToCSharp("computed", computed);
            output.WriteLine(actualText);
            Assert.Equal(expectedText, actualText);
        }
    }
    // TODO: env.HOME expansion, and changing stuff up to avoid defaults causing false positive success

    internal static Dictionary<string, string> MINIMAL_TOML_EXPECTED_DEV = new(){
      {"ImageflowMiddlewareOptions.MyOpenSourceProjectUrl", "https://i-need-a-license.com"},
    {"ImageflowMiddlewareOptions.AllowCaching", "True"},
    {"ImageflowMiddlewareOptions.AllowDiskCaching", "True"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..VirtualPath", "/images/"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..PhysicalPath", @"D:\inetpub\site\wwwroot\images"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..IgnorePrefixCase", "False"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..StringToCompare", "/images/"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..StringComparison", "Ordinal"},
    {"ImageflowMiddlewareOptions.MapWebRoot", "False"},
    {"ImageflowMiddlewareOptions.UsePresetsExclusively", "False"},
    {"ImageflowMiddlewareOptions.DefaultCacheControlString", "public, max-age=20"},
    {"ImageflowMiddlewareOptions.RequestSignatureOptions.IsEmpty", "True"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxDecodeSize.MaxWidth", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxDecodeSize.MaxHeight", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxDecodeSize.MaxMegapixels", "80"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxFrameSize.MaxWidth", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxFrameSize.MaxHeight", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxFrameSize.MaxMegapixels", "80"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxEncodeSize.MaxWidth", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxEncodeSize.MaxHeight", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxEncodeSize.MaxMegapixels", "40"},
    {"ImageflowMiddlewareOptions.ApplyDefaultCommandsToQuerylessUrls", "True"},
    {"ImageflowMiddlewareOptions.LicenseKey", " "},
    {"ImageflowMiddlewareOptions.DiagnosticsPassword", "[[redacted_must_be_12+chars]]"},
    {"HybridCacheOptions.UniqueName", "disk"},
    {"HybridCacheOptions.DiskCacheDirectory", @"D:\inetpub\site\ImageflowCache"},
    {"HybridCacheOptions.QueueSizeLimitInBytes", "104857600"},
    {"HybridCacheOptions.CacheSizeLimitInBytes", "31457280000"},
    {"HybridCacheOptions.MinCleanupBytes", "1048576"},
    {"HybridCacheOptions.WriteQueueMemoryMb", "100"},
    {"HybridCacheOptions.CacheSizeMb", "30000"},
    {"HybridCacheOptions.EvictionSweepSizeMb", "1"},
    {"HybridCacheOptions.MinAgeToDelete", "00:00:30"},
    {"HybridCacheOptions.DatabaseShards", "4"},
    {"ServerConfigurationOptions.UseDeveloperExceptionPage", "True"},
    {"ServerConfigurationOptions.UseExceptionHandler", "/error"},
    {"ServerConfigurationOptions.UseHsts", "False"},
    {"ServerConfigurationOptions.UseHttpsRedirection", "False"},
    {"ServerConfigurationOptions.UseRewriter", "True"},
    {"ServerConfigurationOptions.UseRouting", "True"},
    {"ServerConfigurationOptions.Endpoints[0]..For", "/error"},
    {"ServerConfigurationOptions.Endpoints[0]..ContentType", "text/html"},
    {"ServerConfigurationOptions.Endpoints[0]..Content", "<p>An error has occurred while processing the request.</p>"},
    {"ServerConfigurationOptions.Endpoints[0]..File", "null"},
    {"ServerConfigurationOptions.Endpoints[0]..CacheControl", "no-cache"},
    {"ServerConfigurationOptions.Endpoints[0]..StatusCode", "500"},
    {"RestartWhenThisFileChanges", "True"},
};

    internal static Dictionary<string, string> MINIMAL_TOML_EXPECTED_PROD = new(){
     {"ImageflowMiddlewareOptions.MyOpenSourceProjectUrl", "https://i-need-a-license.com"},
    {"ImageflowMiddlewareOptions.AllowCaching", "True"},
    {"ImageflowMiddlewareOptions.AllowDiskCaching", "True"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..VirtualPath", "/images/"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..PhysicalPath", @"D:\inetpub\site\wwwroot\images"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..IgnorePrefixCase", "False"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..StringToCompare", "/images/"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..StringComparison", "Ordinal"},
    {"ImageflowMiddlewareOptions.MapWebRoot", "False"},
    {"ImageflowMiddlewareOptions.UsePresetsExclusively", "False"},
    {"ImageflowMiddlewareOptions.DefaultCacheControlString", "public, max-age=20"},
    {"ImageflowMiddlewareOptions.RequestSignatureOptions.IsEmpty", "True"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxDecodeSize.MaxWidth", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxDecodeSize.MaxHeight", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxDecodeSize.MaxMegapixels", "80"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxFrameSize.MaxWidth", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxFrameSize.MaxHeight", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxFrameSize.MaxMegapixels", "80"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxEncodeSize.MaxWidth", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxEncodeSize.MaxHeight", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxEncodeSize.MaxMegapixels", "40"},
    {"ImageflowMiddlewareOptions.ApplyDefaultCommandsToQuerylessUrls", "True"},
    {"ImageflowMiddlewareOptions.LicenseKey", " "},
    {"ImageflowMiddlewareOptions.DiagnosticsPassword", "[[redacted_must_be_12+chars]]"},
    {"HybridCacheOptions.UniqueName", "disk"},
    {"HybridCacheOptions.DiskCacheDirectory", @"D:\inetpub\site\ImageflowCache"},
    {"HybridCacheOptions.QueueSizeLimitInBytes", "104857600"},
    {"HybridCacheOptions.CacheSizeLimitInBytes", "31457280000"},
    {"HybridCacheOptions.MinCleanupBytes", "1048576"},
    {"HybridCacheOptions.WriteQueueMemoryMb", "100"},
    {"HybridCacheOptions.CacheSizeMb", "30000"},
    {"HybridCacheOptions.EvictionSweepSizeMb", "1"},
    {"HybridCacheOptions.MinAgeToDelete", "00:00:30"},
    {"HybridCacheOptions.DatabaseShards", "4"},
    {"ServerConfigurationOptions.UseDeveloperExceptionPage", "False"},
    {"ServerConfigurationOptions.UseExceptionHandler", "/error"},
    {"ServerConfigurationOptions.UseHsts", "True"},
    {"ServerConfigurationOptions.UseHttpsRedirection", "True"},
    {"ServerConfigurationOptions.UseRewriter", "True"},
    {"ServerConfigurationOptions.UseRouting", "True"},
    {"ServerConfigurationOptions.Endpoints[0]..For", "/error"},
    {"ServerConfigurationOptions.Endpoints[0]..ContentType", "text/html"},
    {"ServerConfigurationOptions.Endpoints[0]..Content", "<p>An error has occurred while processing the request.</p>"},
    {"ServerConfigurationOptions.Endpoints[0]..File", "null"},
    {"ServerConfigurationOptions.Endpoints[0]..CacheControl", "no-cache"},
    {"ServerConfigurationOptions.Endpoints[0]..StatusCode", "500"},
    {"RestartWhenThisFileChanges", "True"},
};


    internal static Dictionary<string, string> MINIMAL_TOML_EXPECTED_STAGING = new(){
    {"ImageflowMiddlewareOptions.MyOpenSourceProjectUrl", "https://i-need-a-license.com"},
    {"ImageflowMiddlewareOptions.AllowCaching", "True"},
    {"ImageflowMiddlewareOptions.AllowDiskCaching", "True"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..VirtualPath", "/images/"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..PhysicalPath", @"D:\inetpub\site\wwwroot\images"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..IgnorePrefixCase", "False"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..StringToCompare", "/images/"},
    {"ImageflowMiddlewareOptions.MappedPaths[0]..StringComparison", "Ordinal"},
    {"ImageflowMiddlewareOptions.MapWebRoot", "False"},
    {"ImageflowMiddlewareOptions.UsePresetsExclusively", "False"},
    {"ImageflowMiddlewareOptions.DefaultCacheControlString", "public, max-age=20"},
    {"ImageflowMiddlewareOptions.RequestSignatureOptions.IsEmpty", "True"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxDecodeSize.MaxWidth", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxDecodeSize.MaxHeight", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxDecodeSize.MaxMegapixels", "80"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxFrameSize.MaxWidth", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxFrameSize.MaxHeight", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxFrameSize.MaxMegapixels", "80"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxEncodeSize.MaxWidth", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxEncodeSize.MaxHeight", "12000"},
    {"ImageflowMiddlewareOptions.JobSecurityOptions.MaxEncodeSize.MaxMegapixels", "40"},
    {"ImageflowMiddlewareOptions.ApplyDefaultCommandsToQuerylessUrls", "True"},
    {"ImageflowMiddlewareOptions.LicenseKey", " "},
    {"ImageflowMiddlewareOptions.DiagnosticsPassword", "[[redacted_must_be_12+chars]]"},
    {"HybridCacheOptions.UniqueName", "disk"},
    {"HybridCacheOptions.DiskCacheDirectory", @"D:\inetpub\site\ImageflowCache"},
    {"HybridCacheOptions.QueueSizeLimitInBytes", "104857600"},
    {"HybridCacheOptions.CacheSizeLimitInBytes", "31457280000"},
    {"HybridCacheOptions.MinCleanupBytes", "1048576"},
    {"HybridCacheOptions.WriteQueueMemoryMb", "100"},
    {"HybridCacheOptions.CacheSizeMb", "30000"},
    {"HybridCacheOptions.EvictionSweepSizeMb", "1"},
    {"HybridCacheOptions.MinAgeToDelete", "00:00:30"},
    {"HybridCacheOptions.DatabaseShards", "4"},
    {"ServerConfigurationOptions.UseDeveloperExceptionPage", "False"},
    {"ServerConfigurationOptions.UseExceptionHandler", "/error"},
    {"ServerConfigurationOptions.UseHsts", "True"},
    {"ServerConfigurationOptions.UseHttpsRedirection", "True"},
    {"ServerConfigurationOptions.UseRewriter", "True"},
    {"ServerConfigurationOptions.UseRouting", "True"},
    {"ServerConfigurationOptions.Endpoints[0]..For", "/error"},
    {"ServerConfigurationOptions.Endpoints[0]..ContentType", "text/html"},
    {"ServerConfigurationOptions.Endpoints[0]..Content", "<p>An error has occurred while processing the request.</p>"},
    {"ServerConfigurationOptions.Endpoints[0]..File", "null"},
    {"ServerConfigurationOptions.Endpoints[0]..CacheControl", "no-cache"},
    {"ServerConfigurationOptions.Endpoints[0]..StatusCode", "500"},
    {"RestartWhenThisFileChanges", "True"},
};

    internal static string MINIMAL_TOML = """"
[imageflow_server]
config_schema = '1.0'

[license]
enforcement = "http_402_error" # or http_402_error/http_422_error, for fast failures
key = """ """

[route_defaults]
prefix_case_sensitive = false
lowercase_path_remainder = true
cache_control = "public, max-age=20"
apply_default_commands = "quality=76"
apply_default_commands_to_queryless_urls = true


[[routes]]
route = '/images/{path} => {app.wwwroot}/{path}'

[disk_cache]
enabled = true
folder = '${env.HOME}\ImageflowCache'
cache_size_mb = 30_000 
write_queue_ram_mb = 200 
database_shards = 4
seconds_until_evictable = 30

[diagnostics]
allow_with_password = "[[redacted_must_be_12+chars]]"
allow_localhost = true

[development.diagnostics]
allow_anyhost = true


[aspnet_server]
use_developer_exception_page = false # Don't leak error details to the public
use_exception_handler = "/error"     # Use a custom error page (defined below)
use_hsts = true                      # Allow no HTTP connections whatsoever
use_https_redirection = true         # Redirect all HTTP requests to HTTPS, if any get through
restart_when_this_file_changes = true # Trigger a site or restart when this file changes

[aspnet_server.apache_mod_rewrite]
file = "rewrites.ini"

[development.aspnet_server]
use_developer_exception_page = true # Error details in dev please
use_hsts = false                    # Don't ban unencrypted HTTP in development
use_https_redirection = false       # Or redirect to HTTPS

[[static_response]]
for = "/error"
status_code = 500
cache_control = "no-cache"
content_type = "text/html"
content = '<p>An error has occurred while processing the request.</p>'

[security.max_decode_resolution]
width = 12000
height = 12000
megapixels = 80

[security.max_encode_resolution]
width = 12000
height = 12000
megapixels = 40

[security.max_frame_resolution]
width = 12000
height = 12000
megapixels = 80
"""";



    [Fact]
    public void RoutingExpressionLayer_Receives_Configured_Expressions()
    {
        // Arrange
        Dictionary<string, string> appVars = new(){
            {"approot", "D:\\inetpub\\site"},
            {"wwwroot", "D:\\inetpub\\site\\wwwroot"}
        };
        var context = new TomlParserContext(DeploymentEnvironment.Development, appVars,
            key => key.ToLowerInvariant() switch
            {
                "homedrive" => "D:",
                "homepath" => "\\inetpub\\site",
                _ => null,
            },
            new TestFileMethods(new Dictionary<string, string>{{"rewrites.ini", ""}})
        );
        var result = TomlParser.Parse(MINIMAL_TOML, "embedded.minimal.toml", context);
        var services = new ServiceCollection();
        var executor = result.GetAppConfigurator();
        executor.ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        var optionsMonitor = provider.GetService<Microsoft.Extensions.Options.IOptionsMonitor<Imazen.Routing.Layers.RoutingExpressions.UriRoutingOptions>>();
        Assert.NotNull(optionsMonitor);
        var opts = optionsMonitor!.CurrentValue;
        Assert.Contains("myexpression://foo/bar", opts.Routes);
    }

}
