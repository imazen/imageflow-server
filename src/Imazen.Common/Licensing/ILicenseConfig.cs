using System.Collections.Generic;

namespace Imazen.Common.Licensing
{
    internal delegate void LicenseConfigEvent(object sender, ILicenseConfig forConfig);

    internal interface ILicenseConfig
    {
        // var fromWebConfig = c.getNode("licenses")?.childrenByName("maphost")
        //                         .Select(n => new KeyValuePair<string, string>(
        //                             n.Attrs["from"]?.Trim().ToLowerInvariant(), 
        //                             n.Attrs["to"]?.Trim().ToLowerInvariant()))
        //                     ?? Enumerable.Empty<KeyValuePair<string, string>>();
        // var fromPluginsConfig = c.Plugins.GetLicensedDomainMappings();
        //
        //    fromWebConfig.Concat(fromPluginsConfig)
        
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        IEnumerable<KeyValuePair<string, string>> GetDomainMappings();


        //c.Plugins.GetAll<ILicensedPlugin>().Select(p => p.LicenseFeatureCodes).ToList();

        /// <summary>
        /// For each item, at least of of the feature codes in the sub-list must be present
        /// </summary>
        /// <returns></returns>
        IEnumerable<IEnumerable<string>> GetFeaturesUsed();


        // c.Plugins.GetAll<ILicenseProvider>()
        // .SelectMany(p => p.GetLicenses())
            
        IEnumerable<string> GetLicenses();
        
        /// <summary>
        /// The scope of licenses exposed by GetLicenses()
        /// </summary>
        LicenseAccess LicenseScope { get; }
        
        /// <summary>
        /// The license enforcement action to take
        /// </summary>
        LicenseErrorAction LicenseEnforcement { get; }
        
        /// <summary>
        ///<code>
        /// string EnforcementMethodMessage =&gt; LicenseError == LicenseErrorAction.Http402
        /// ? $"You are using &lt;licenses licenseError='{LicenseError}'>. If there is a licensing error, an exception will be thrown (with HTTP status code 402). This can also be set to '{LicenseErrorAction.Watermark}'."
        ///: $"You are using &lt;licenses licenseError='{LicenseError}'>. If there is a licensing error, a red dot will be drawn on the bottom-right corner of each image. This can be set to '{LicenseErrorAction.Http402}' instead (valuable if you are storing results)."
        ///;</code>
        /// </summary>
        string EnforcementMethodMessage { get;  }
        
        /// <summary>
        /// There has been a change in licensed or licensing plugins
        /// </summary>
        event LicenseConfigEvent LicensingChange;
        
        
        /// <summary>
        /// An event that fires for most image requests, but does not guarantee an http context.
        /// </summary>
        event LicenseConfigEvent Heartbeat;
        
        /// <summary>
        /// Determines if this is Imageflow
        /// Among other things, determines if the Expires or ImageflowExpires fields are used.
        /// </summary>
        bool IsImageflow { get; }
        
        /// <summary>
        /// Determines if this is ImageResizer
        /// </summary>
        bool IsImageResizer { get; }
        
        /// <summary>
        /// Should be https://imageresizing.net/licenses 
        /// </summary>
        string LicensePurchaseUrl { get;  }
        
        string AgplCompliantMessage { get; }
    }
}