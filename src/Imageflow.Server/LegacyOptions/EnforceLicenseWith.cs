namespace Imageflow.Server
{
    public enum EnforceLicenseWith
    {
        RedDotWatermark = 0,
        Http422Error = 1,
        Http402Error = 2

    }
    internal static class EnforceLicenseWithExtensions
    {
        public static EnforceLicenseWith IntoLegacyEnforceWith(this Imazen.Routing.Serving.EnforceLicenseWith value) => (EnforceLicenseWith)(int)value;

        public static Imazen.Routing.Serving.EnforceLicenseWith Into(this EnforceLicenseWith value) => (Imazen.Routing.Serving.EnforceLicenseWith)(int)value;
    }
}