using Imazen.Abstractions.Resulting;

namespace Imazen.Abstractions.Blobs.LegacyProviders
{
    public interface IBlobWrapperProvider : IUniqueNamed
    {
        IEnumerable<string> GetPrefixes();
        
        /// <summary>
        /// Only called if one of GetPrefixes matches already.
        /// </summary>
        /// <param name="virtualPath"></param>
        /// <returns></returns>
        bool SupportsPath(string virtualPath);
        
        Task<CodeResult<IBlobWrapper>> Fetch(string virtualPath);
    }
    
    public record BlobWrapperPrefixZone(string Prefix, LatencyTrackingZone LatencyZone);
    public interface IBlobWrapperProviderZoned : IUniqueNamed
    {
        IEnumerable<BlobWrapperPrefixZone> GetPrefixesAndZones();
        
        LatencyTrackingZone  GetLatencyZone(string virtualPath);
    }
}
