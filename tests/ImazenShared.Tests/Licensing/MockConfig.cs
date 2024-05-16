using Imazen.Common.Issues;
using Imazen.Common.Licensing;

namespace Imazen.Common.Tests.Licensing;

internal class MockConfig : ILicenseConfig
{
    Computation? cache;
    readonly string[] codes;

    readonly LicenseManagerSingleton mgr;
    ILicenseClock Clock { get; } = new RealClock();

    // ReSharper disable once MemberInitializerValueIgnored
    private readonly IEnumerable<KeyValuePair<string, string>> domainMappings = new List<KeyValuePair<string, string>>();
    Computation Result
    {
        get {
            if (cache?.ComputationExpires != null && cache.ComputationExpires.Value < Clock.GetUtcNow()) {
                cache = null;
            }
            return cache ??= new Computation(this, ImazenPublicKeys.All, mgr, mgr,
                Clock, true);
        }
    }
        
    public MockConfig(LicenseManagerSingleton mgr, ILicenseClock? clock, string[] codes, IEnumerable<KeyValuePair<string, string>> domainMappings)
    {
        this.codes = codes;
        this.mgr = mgr;
        Clock = clock ?? Clock;
            
        mgr.MonitorLicenses(this);
        mgr.MonitorHeartbeat(this);

        // Ensure our cache is appropriately invalidated
        cache = null;
        mgr.AddLicenseChangeHandler(this, (me, _) => me.cache = null);

        // And repopulated, so that errors show up.
        if (Result == null) {
            throw new ApplicationException("Failed to populate license result");
        }
        this.domainMappings = domainMappings;

    }
    public IEnumerable<KeyValuePair<string, string>> GetDomainMappings()
    {
        return domainMappings;
    }
        
    public IReadOnlyCollection<IReadOnlyCollection<string>> GetFeaturesUsed()
    {
        return new[] { codes };
    }

    private readonly List<string> licenses = new List<string>();
    public IEnumerable<string> GetLicenses()
    {
        return licenses;
    }

    public LicenseAccess LicenseScope { get; } = LicenseAccess.Local;
    public LicenseErrorAction LicenseEnforcement { get; } = LicenseErrorAction.Http402;
    public string EnforcementMethodMessage { get; } = "";
    public event LicenseConfigEvent? LicensingChange;
    public event LicenseConfigEvent? Heartbeat;
    public bool IsImageflow { get; } = false;
    public bool IsImageResizer { get; } = true;
    public string LicensePurchaseUrl { get; }  = "https://imageresizing.net/licenses";
    public string AgplCompliantMessage { get; } = "";

    public void AddLicense(string license)
    {
        licenses.Add(license);
        LicensingChange?.Invoke(this, this);
        cache = null;
    }

    public string GetLicensesPage()
    {
        return Result.ProvidePublicLicensesPage();
    }
        
    public IEnumerable<IIssue> GetIssues() => mgr.GetIssues().Concat(Result.GetIssues());


    public void FireHeartbeat()
    {
        Heartbeat?.Invoke(this,this);
    }
}