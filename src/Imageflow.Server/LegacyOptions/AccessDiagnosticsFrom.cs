namespace Imageflow.Server
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

    internal static class AccessDiagnosticsFromExtensions
    {
        public static AccessDiagnosticsFrom IntoLegacyEnum(this Imazen.Routing.Layers.DiagnosticsPageOptions.AccessDiagnosticsFrom value) => (AccessDiagnosticsFrom)(int)value;

        public static Imazen.Routing.Layers.DiagnosticsPageOptions.AccessDiagnosticsFrom Into(this AccessDiagnosticsFrom value) => (Imazen.Routing.Layers.DiagnosticsPageOptions.AccessDiagnosticsFrom)(int)value;
    }
}