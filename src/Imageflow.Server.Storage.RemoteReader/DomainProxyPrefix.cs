

// A prefix that maps to a domain + base path. May specify a named http client to use. 
// may require general signing of requests or not.


// Define a class that represents a prefix mapping to a domain and base path.
// This class will also allow specifying a named HTTP client to use for requests.

using Imazen.Abstractions.Blobs;

public class DomainProxyPrefix
{
    // The prefix that will be mapped to a domain and base path.
    public string Prefix { get; set; }

    // The domain to which the prefix maps.
    public string RemoteUriBase { get; set; }

    // Optional: The name of the HTTP client to use for requests.
    public string? HttpClientName { get; set; }

    public bool IgnorePrefixCase { get; set; }

    internal LatencyTrackingZone? Zone { get; set; }

    // Constructor to initialize the properties of the DomainProxyPrefix.
    public DomainProxyPrefix(string prefix, string remoteUriBase, bool ignorePrefixCase, string? httpClientName = null)
    {
        Prefix = '/' + prefix.TrimStart('/').TrimEnd('/') + '/' ;
        RemoteUriBase = remoteUriBase;
        HttpClientName = httpClientName;
        IgnorePrefixCase = ignorePrefixCase;
    }
}
