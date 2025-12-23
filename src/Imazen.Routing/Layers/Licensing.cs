using System.Collections;
using Imazen.Common.Licensing;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Serving;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Imazen.Routing.Layers;

public class LicenseOptions{
    public string? LicenseKey { get; set; } = "";
    public string? MyOpenSourceProjectUrl { get; set; } = "";
    public EnforceLicenseWith? EnforcementMethod { get; set; } = EnforceLicenseWith.RedDotWatermark;
}

internal class LicenseCacheOptions{
    internal string ProcessWideKeyPrefixDefault { get; set; } = "imageflow_";
    internal string[] ProcessWideCandidateCacheFoldersDefault { get; set; } = [Path.GetTempPath()];
}

internal static class LicensingExtensions{
    public static void AddILicenseChecker(this IServiceCollection services, LicenseCacheOptions? cacheOptions = null, LicenseOptions? defaultOptions = null)
    {
        services.AddOptions<LicenseOptions>()
                .BindConfiguration("Imageflow:License");
        services.PostConfigure<LicenseOptions>(opts => 
        {
            opts.LicenseKey ??= defaultOptions?.LicenseKey;
            opts.MyOpenSourceProjectUrl ??= defaultOptions?.MyOpenSourceProjectUrl;
            opts.EnforcementMethod ??= defaultOptions?.EnforcementMethod;
        });

       services.TryAddSingleton<ILicenseChecker>(p => {
                
                var optionsMonitor = p.GetRequiredService<IOptionsMonitor<LicenseOptions>>();

                if (cacheOptions == null) {
                    var env = p.GetRequiredService<IDefaultContentRootPathProvider>();
                    cacheOptions= new LicenseCacheOptions(){
                    ProcessWideKeyPrefixDefault = "imageflow_",
                    ProcessWideCandidateCacheFoldersDefault =
                    [
                        env.DefaultContentRootPath,
                        Path.GetTempPath()
                    ]
                };
                }
                
                return Licensing.CreateAndEnsureManagerSingletonCreated(cacheOptions!, optionsMonitor);
            });
    }
}

internal class Licensing : ILicenseConfig, ILicenseChecker, IHasDiagnosticPageSection
{
    private static LicenseManagerSingleton GetOrCreateLicenseManagerProcessSingleton(string keyPrefix, string[] candidateCacheFolders) 
        => LicenseManagerSingleton.GetOrCreateSingleton(keyPrefix, candidateCacheFolders);
    internal static Licensing CreateAndEnsureManagerSingletonCreated(LicenseCacheOptions options, IOptionsMonitor<LicenseOptions> optionsMonitor)
     => CreateWithOptionsMonitoring(GetOrCreateLicenseManagerProcessSingleton(options.ProcessWideKeyPrefixDefault, options.ProcessWideCandidateCacheFoldersDefault), optionsMonitor);
    private readonly Func<Uri?>? getCurrentRequestUrl;

    private readonly LicenseManagerSingleton mgr;

    private Computation? cachedResult;
    internal Licensing(LicenseManagerSingleton mgr, Func<Uri?>? getCurrentRequestUrl = null, IOptionsMonitor<LicenseOptions>? optionsMonitor = null)
    {
        this.mgr = mgr;
        this.getCurrentRequestUrl = getCurrentRequestUrl;
        optionsMonitor?.OnChange(Initialize);
        Initialize(optionsMonitor?.CurrentValue);
    }

    private Licensing(LicenseManagerSingleton mgr, LicenseOptions options, Func<Uri?>? getCurrentRequestUrl = null)
    {
        this.mgr = mgr;
        this.getCurrentRequestUrl = getCurrentRequestUrl;
        Initialize(options);
    }

    private static Licensing CreateWithOptionsMonitoring(LicenseManagerSingleton mgr, IOptionsMonitor<LicenseOptions> optionsMonitor)
    {
        return new Licensing(mgr, optionsMonitor: optionsMonitor);
    }

    internal static Licensing CreateForManagerSingleton(LicenseManagerSingleton mgr, LicenseOptions options)
    {
        return new Licensing(mgr, options, getCurrentRequestUrl: null);
    }
    internal static Licensing CreateWithMockUrl(LicenseManagerSingleton mgr, LicenseOptions options, Func<Uri?> getCurrentRequestUrl = null)
    {
        return new Licensing(mgr, options, getCurrentRequestUrl);
    }
    private LicenseOptions? options;

    private LicenseManagerEvent? registeredChangeHandler;
    public void Initialize(LicenseOptions? licenseOptions)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        options = licenseOptions;
        mgr.MonitorLicenses(this);
        mgr.MonitorHeartbeat(this);

        // Ensure our cache is appropriately invalidated
        cachedResult = null;
        if (registeredChangeHandler != null) {
            mgr.RemoveLicenseChangeHandler(registeredChangeHandler);
        }
        registeredChangeHandler = mgr.AddLicenseChangeHandler(this, (me, manager) => me.cachedResult = null);

        // And repopulated, so that errors show up.
        if (Result == null) {
            throw new ApplicationException("Failed to populate license result");
        }
        sw.Stop();
    }

    private bool EnforcementEnabled()
    {
        return options != null && (!string.IsNullOrEmpty(options.LicenseKey)
                                   || string.IsNullOrEmpty(options.MyOpenSourceProjectUrl));
    }
    public IEnumerable<KeyValuePair<string, string>> GetDomainMappings()
    {
        return Enumerable.Empty<KeyValuePair<string, string>>();
    }

    public IReadOnlyCollection<IReadOnlyCollection<string>> GetFeaturesUsed()
    {
        return new [] {new [] {"Imageflow"}};
    }

    public IEnumerable<string> GetLicenses()
    {
        return !string.IsNullOrEmpty(options?.LicenseKey) ? Enumerable.Repeat(options!.LicenseKey!, 1) : Enumerable.Empty<string>();
    }

    public LicenseAccess LicenseScope => LicenseAccess.Local;

    public LicenseErrorAction LicenseEnforcement
    {
        get
        {
            if (options == null) {
                return LicenseErrorAction.Http422;
            }
            return options.EnforcementMethod switch
            {
                EnforceLicenseWith.RedDotWatermark => LicenseErrorAction.Watermark,
                EnforceLicenseWith.Http422Error => LicenseErrorAction.Http422,
                EnforceLicenseWith.Http402Error => LicenseErrorAction.Http402,
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }

    public string EnforcementMethodMessage
    {
        get
        {
            return LicenseEnforcement switch
            {
                LicenseErrorAction.Watermark =>
                    "You are using EnforceLicenseWith.RedDotWatermark. If there is a licensing error, an red dot will be drawn on the bottom-right corner of each image. This can be set to EnforceLicenseWith.Http402Error instead (valuable if you are externally caching or storing result images.)",
                LicenseErrorAction.Http422 =>
                    "You are using EnforceLicenseWith.Http422Error. If there is a licensing error, HTTP status code 422 will be returned instead of serving the image. This can also be set to EnforceLicenseWith.RedDotWatermark.",
                LicenseErrorAction.Http402 =>
                    "You are using EnforceLicenseWith.Http402Error. If there is a licensing error, HTTP status code 402 will be returned instead of serving the image. This can also be set to EnforceLicenseWith.RedDotWatermark.",
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }

#pragma warning disable CS0067
    public event LicenseConfigEvent? LicensingChange;
#pragma warning restore CS0067
        
    public event LicenseConfigEvent? Heartbeat;
    public bool IsImageflow => true;
    public bool IsImageResizer => false;
    public string LicensePurchaseUrl => "https://imageresizing.net/licenses";

    public string AgplCompliantMessage
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(options?.MyOpenSourceProjectUrl))
            {
                return "You have certified that you are complying with the AGPLv3 and have open-sourced your project at the following url:\r\n"
                       + options!.MyOpenSourceProjectUrl;
            }
            else
            {
                return "";
            }
        }
    }

    internal Computation Result
    {
        get {
            if (cachedResult?.ComputationExpires != null &&
                cachedResult.ComputationExpires.Value < mgr.Clock.GetUtcNow()) {
                cachedResult = null;
            }
            return cachedResult ??= new Computation(this, mgr.TrustedKeys,mgr, mgr,
                mgr.Clock, EnforcementEnabled());
        }
    }

   
    public string InvalidLicenseMessage =>
        "Imageflow cannot validate your license; visit /imageflow.debug or /imageflow.license to troubleshoot.";

    public EnforceLicenseWith? RequestNeedsEnforcementAction(IHttpRequestStreamAdapter request)
    {
        if (!EnforcementEnabled()) {
            return null;
        }
            
        var requestUrl = getCurrentRequestUrl != null ? getCurrentRequestUrl() : 
            request.GetUri();

        var isLicensed = Result.LicensedForRequestUrl(requestUrl);
        if (isLicensed) {
            return null;
        }

        if (requestUrl == null && Result.LicensedForSomething()) {
            return null;
        }

        return options?.EnforcementMethod;
    }

    public EnforceLicenseWith? RequestNeedsEnforcementActionExplained(IHttpRequestStreamAdapter request, out string? message)
    {
        if (!EnforcementEnabled()) {
            message = "License Enforcement is disabled";
            return null;
        }
        var explain = "";    

        var requestUrl = getCurrentRequestUrl != null ? getCurrentRequestUrl() : 
            request.GetUri();
        var requestUrlProvider = getCurrentRequestUrl != null ? "getCurrentRequestUrl" : "request.GetUri";
        explain += $"Request URL: {requestUrl} (from {requestUrlProvider})\n";

        var isLicensed = Result.LicensedForRequestUrl(requestUrl);
        if (isLicensed) {
            explain += "Request is licensed\n";
            message = explain;
            return null;
        }

        if (requestUrl == null && Result.LicensedForSomething()) {
            explain += "Request URL is null, but a validlicense for *something* exists, so we are permitting this action\n";
            message = explain;
            return null;
        }
        explain += "Request is not licensed, and no valid license exists for *something*\n";
        explain += "Enforcement method is " + options?.EnforcementMethod + "\n";
        message = explain;

        return options?.EnforcementMethod;
    }


    public string GetLicensePageContents()
    {
        return Result.ProvidePublicLicensesPage();
    }

    public void FireHeartbeat()
    {
        Heartbeat?.Invoke(this, this);
    }

    public string? GetDiagnosticsPageSection(DiagnosticsPageArea section)
    {
        if (section != DiagnosticsPageArea.End)
        {
            return Result.ProvidePublicLicensesPage();
        }
        var s = new System.Text.StringBuilder();
        s.AppendLine(
            "\n\nWhen fetching a remote license file (if you have one), the following information is sent via the querystring.");
        foreach (var pair in Result.GetReportPairs().GetInfo())
        {
            s.AppendFormat("   {0,32} {1}\n", pair.Key, pair.Value);
        }
            
            
        s.AppendLine(Result.DisplayLastFetchUrl());
        return s.ToString();
    }
}