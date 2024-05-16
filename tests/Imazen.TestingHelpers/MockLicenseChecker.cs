using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Layers;
using Imazen.Routing.Serving;

namespace Imazen.Tests.Routing.Serving;

public class MockLicenseChecker(Func<IHttpRequestStreamAdapter, EnforceLicenseWith?> NeedsEnforcement, string LicenseMessage):ILicenseChecker
{
    public EnforceLicenseWith? RequestNeedsEnforcementAction(IHttpRequestStreamAdapter request)
    {
        return NeedsEnforcement(request);
    }

    public string InvalidLicenseMessage => LicenseMessage;
    public string GetLicensePageContents()
    {
        return "";
    }

    public void FireHeartbeat()
    {
        
    }

    public void Initialize(LicenseOptions licenseOptions)
    {
        
    }

    public static MockLicenseChecker AlwaysOK()
    {
        return new MockLicenseChecker(request => null, "OK");
    }
}