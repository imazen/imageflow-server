
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

namespace Imazen.Routing.Layers;

public static class DiagnosticsPageExtensions
{ // register StartupDiagnostics, DiagnosticsReport, DiagnosticsPage, and DiagnosticsPageOptions
    public static void AddDiagnosticsPage(this IServiceCollection services, DiagnosticsPageOptions options)
    {
        services.AddSingleton(options);
        services.TryAddDiagnosticsPageReportAndStartup();
   }
    internal static void TryAddDiagnosticsPageReportAndStartup(this IServiceCollection services)
    {
        services.TryAddSingleton<StartupDiagnostics>();
        services.TryAddSingleton<DiagnosticsReport>();
        services.TryAddSingleton<DiagnosticsPage>();
    }
}

public record DiagnosticsPageOptions(
    string? DiagnosticsPassword,
    DiagnosticsPageOptions.AccessDiagnosticsFrom DiagnosticsAccess)
{
    
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
    DiagnosticsPageOptions options)
    : IRoutingEndpoint, IRoutingLayer
{
    
    public static bool MatchesPath(string path) => "/imageflow.debug".Equals(path, StringComparison.Ordinal)
    || "/resizer.debug".Equals(path, StringComparison.Ordinal);

 

    public bool IsAuthorized(IHttpRequestStreamAdapter request, out string? errorMessage)
    {
        errorMessage = null;
        var providedPassword = request.GetQuery()["password"].ToString();
        var passwordMatch = !string.IsNullOrEmpty(options.DiagnosticsPassword)
                            && options.DiagnosticsPassword == providedPassword;
            
        string s;
        if (passwordMatch || 
            options.DiagnosticsAccess == DiagnosticsPageOptions.AccessDiagnosticsFrom.AnyHost ||
            (options.DiagnosticsAccess == DiagnosticsPageOptions.AccessDiagnosticsFrom.LocalHost && request.IsClientLocalhost()))
        {
            return true;
        }
        else
        {
            s =
                "You can configure access to this page via the imageflow.toml [diagnostics] section, or in C# via ImageflowMiddlewareOptions.SetDiagnosticsPageAccess(allowLocalhost, password)\r\n\r\n";
            if (options.DiagnosticsAccess == DiagnosticsPageOptions.AccessDiagnosticsFrom.LocalHost)
            {
                s += "You can access this page from the localhost\r\n\r\n";
            }
            else
            {
                s += "Access to this page from localhost is disabled\r\n\r\n";
            }

            if (!string.IsNullOrEmpty(options.DiagnosticsPassword))
            {
                s += "You can access this page by adding ?password=[insert password] to the URL.\r\n\r\n";
            }
            else
            {
                s += "You can set a password via imageflow.toml [diagnostics] allow_with_password='' or in C# with SetDiagnosticsPageAccess to access this page remotely.\r\n\r\n";
            }
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
