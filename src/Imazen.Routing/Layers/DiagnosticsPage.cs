
using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Imazen.Routing.Health;
using Imazen.Routing.Helpers;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Promises;
using Imazen.Routing.Requests;
using Imazen.Routing.Serving;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Imazen.Routing.Layers;

public static class DiagnosticsPageExtensions
{ // register StartupDiagnostics, DiagnosticsReport, DiagnosticsPage, and DiagnosticsPageOptions
    
    /// <summary>
    /// Adds the diagnostics page to the service collection, binding to the configuration section "Imageflow::Diagnostics" and using the provided options as defaults for values not set by the configuration section
    /// </summary>
    /// <param name="services"></param>
    /// <param name="options">Default options for values not set by the configuration section "Imageflow::Diagnostics"</param>
    public static void AddDiagnosticsPage(this IServiceCollection services, DiagnosticsPageOptions options)
    {
        services.AddOptions<DiagnosticsPageOptions>()
                .BindConfiguration("Imageflow::Diagnostics");
        services.PostConfigure<DiagnosticsPageOptions>(opts => 
        {
            opts.Password ??= options.Password;
            opts.AccessFrom ??= options.AccessFrom;
        });

        services.TryAddDiagnosticsPageReportAndStartup();
    }
    /// <summary>
    /// Adds the diagnostics page to the service collection, binding to the configuration section "Imageflow::Diagnostics"
    /// Configuration keys and values:
    /// <list type="bullet">
    /// <item><term>password</term><description>The password to access the diagnostics page</description></item>
    /// <item><term>access_from</term><description>Where the diagnostics page can be accessed from</description></item>
    /// </list>
    /// </summary>
    /// <param name="services"></param>
    public static void AddDiagnosticsPage(this IServiceCollection services)
    {
        services.AddOptions<DiagnosticsPageOptions>()
                .BindConfiguration("Imageflow::Diagnostics");

        services.TryAddDiagnosticsPageReportAndStartup();
    }
    /// <summary>
    /// Adds the diagnostics page to the service collection without binding to the configuration section "Imageflow::Diagnostics".
    /// You need to bind the configuration section separately.
    /// <example>
    /// services.AddOptions<DiagnosticsPageOptions>()
    ///         .BindConfiguration("Imageflow::Diagnostics");
    /// services.AddDiagnosticsPageWithoutBindingConfiguration();
    /// </example>
    /// </summary>
    /// <param name="services"></param>
    public static void AddDiagnosticsPageWithoutBindingConfiguration(this IServiceCollection services)
    {
        services.TryAddDiagnosticsPageReportAndStartup();
    }
    internal static void TryAddDiagnosticsPageReportAndStartup(this IServiceCollection services)
    {
        services.TryAddSingleton<StartupDiagnostics>();
        services.TryAddSingleton<DiagnosticsReport>();
        services.TryAddSingleton<DiagnosticsPage>();
    }
}

public class DiagnosticsPageOptions
{
    public string? Password { get; set; }
    public DiagnosticsPageOptions.AccessDiagnosticsFrom? AccessFrom { get; set; }
    
    /// <summary>
    /// Where the diagnostics page can be accessed from
    /// </summary>
    public enum AccessDiagnosticsFrom
    {
        /// <summary>
        /// Do not allow unauthenticated access to the diagnostics page, even from localhost
        /// </summary>
        None,
        /// <summary>
        /// Only allow localhost to access the diagnostics page
        /// </summary>
        LocalHost,
        /// <summary>
        /// Allow any host to access the diagnostics page
        /// </summary>
        AnyHost
    }
}
    
internal class DiagnosticsPage(
    DiagnosticsReport diagnosticsReport,
    IReLogger<DiagnosticsPage> logger,
    IOptionsMonitor<DiagnosticsPageOptions> options)
    : IRoutingEndpoint, IRoutingLayer
{
    
    public static bool MatchesPath(string path) => "/imageflow.debug".Equals(path, StringComparison.Ordinal)
    || "/resizer.debug".Equals(path, StringComparison.Ordinal);

    private static string PasswordRules = "Passwords must be at least 10 characters long";
    private static bool IsPasswordSufficient(string? password)
    {
        // can't be empty or less than 8 chars
        return !string.IsNullOrEmpty(password) && password.Length >= 10;
    }
 

    public bool IsAuthorized(IHttpRequestStreamAdapter request, out string? errorMessage)
    {
        errorMessage = null;
        var providedPassword = request.GetQuery()["password"].ToString().Trim();
        var actualPassword = options.CurrentValue.Password?.Trim();
        var passwordSetIsSufficient = IsPasswordSufficient(options.CurrentValue.Password);
        var passwordMatch = passwordSetIsSufficient 
                            && string.Equals(actualPassword, providedPassword, StringComparison.Ordinal);
            
        string s;
        if (passwordMatch || 
            options.CurrentValue.AccessFrom == DiagnosticsPageOptions.AccessDiagnosticsFrom.AnyHost ||
            (options.CurrentValue.AccessFrom == DiagnosticsPageOptions.AccessDiagnosticsFrom.LocalHost 
                        && request.IsClientLocalhost()))
        {
            return true;
        }
        else
        {
            var preface = options.CurrentValue.AccessFrom == DiagnosticsPageOptions.AccessDiagnosticsFrom.LocalHost
                ? "You can access this page from the localhost\r\n\r\n" : "";


            var message = """
How to access this diagnostics page with a password:
1. Configure a password (Imageflow:Diagnostics:Password), then add ?password=[password] to this URL.\r\n
"""
                 + PasswordRules + "\r\n\r\n" +
"2. Configure access by client IP (set Imageflow:Diagnostics:AccessFrom to AnyHost or LocalHost)\r\n\r\n";
            message += """
How to set a password in appsettings.json:
    { "Imageflow": { "Diagnostics": { "Password": "10CharPassword" } } }

How to set a password in imageflow.toml:
    [diagnostics]
    allow_with_password = "10CharPassword"

How to set a password in C#:
    new ImageflowMiddlewareOptions().SetDiagnosticsPagePassword("10CharPassword");


How to configure access by client IP in appsettings.json:
    { "Imageflow": { "Diagnostics": { "AccessFrom": "LocalHost" } } }
or 
    { "Imageflow": { "Diagnostics": { "AccessFrom": "AnyHost" } } }
    
How to configure access by client IP in imageflow.toml:
    [diagnostics]
    allow_localhost = true
or
    [diagnostics]
    allow_anyhost = true

How to configure access by client IP in C#:
    new ImageflowMiddlewareOptions().SetDiagnosticsPageAccess(AccessDiagnosticsFrom.AnyHost);
or
    new ImageflowMiddlewareOptions().SetDiagnosticsPageAccess(AccessDiagnosticsFrom.LocalHost);
""";
            
            s = preface + message;
            errorMessage = s;
            logger.LogInformation("Access to diagnostics page denied. {message}", s);
            return false;
        }
    }

    private async Task<string> GeneratePage(IRequestSnapshot r)
    {

        var request = r.OriginatingRequest;
        var result = await diagnosticsReport.GetReport(request);
        return result;
    }
        
        
       

    public ValueTask<IInstantPromise> GetInstantPromise(IRequestSnapshot request, CancellationToken cancellationToken = default)
    {
        return Tasks.ValueResult((IInstantPromise)new PromiseFuncAsync(request, async (r, _) =>
            SmallHttpResponse.NoStoreNoRobots((200, await GeneratePage(r)))));
    }

    public string Name => "Diagnostics page";
    public IFastCond FastPreconditions => Precondition;

    public static readonly IFastCond Precondition = Conditions.HasPathSuffix("/imageflow.debug", "/resizer.debug");
        
    public ValueTask<CodeResult<IRoutingEndpoint>?> ApplyRouting(MutableRequest request, CancellationToken cancellationToken = default)
    {
        if (!Precondition.Matches(request)) return default;
        if (request.IsChildRequest) return default;
        // method not allowed
        if (!request.IsGet())
            return new ValueTask<CodeResult<IRoutingEndpoint>?>(
                CodeResult<IRoutingEndpoint>.Err((405, "Method not allowed")));
 
        if (!IsAuthorized(request.UnwrapOriginatingRequest(), out var errorMessage))
        {
            return new ValueTask<CodeResult<IRoutingEndpoint>?>(
                CodeResult<IRoutingEndpoint>.Err((401, errorMessage)));
        }
        return new ValueTask<CodeResult<IRoutingEndpoint>?>(
            CodeResult<IRoutingEndpoint>.Ok(this));
    }

    public bool IsBlobEndpoint => false;
        
}
