using System.IO.Pipelines;
using System.Net;
using Imazen.Abstractions.HttpStrings;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Serving;
using Microsoft.Extensions.Primitives;

namespace Imazen.Routing.Tests.Serving;

public record MockRequest
{
    public required UrlPathString Path { get; init; }
    public UrlPathString PathBase { get; init; }
    public required Uri Uri { get; init; }
    public required UrlQueryString QueryString { get; init; }
    public required UrlHostString Host { get; init; }
    public IEnumerable<KeyValuePair<string, string>>? Cookies { get; init; }
    public required IDictionary<string, StringValues> Headers { get; init; }
    public required IReadOnlyQueryWrapper Query { get; init; }
    public string? ContentType { get; init; }
    public long? ContentLength { get; init; }
    public required string Protocol { get; init; }
    public required string Scheme { get; init; }
    public required string Method { get; init; }
    public bool IsHttps => Scheme == "https";
    public Stream? BodyStream { get; init; }
    public IPAddress? RemoteIpAddress { get; init; }
    public IPAddress? LocalIpAddress { get; init; }
    public bool SupportsPipelines => BodyPipeReader != null;
    public PipeReader? BodyPipeReader { get; init; }


    public MockRequest WithAbsoluteUri(Uri absoluteUri, string pathBase = "")
    {
        var path = absoluteUri.AbsolutePath;
        if (!string.IsNullOrEmpty(pathBase))
        {
            if (path.StartsWith(pathBase, StringComparison.OrdinalIgnoreCase))
                path = path[pathBase.Length..];
            else if (path.StartsWith("/" + pathBase, StringComparison.OrdinalIgnoreCase))
                path = path[(pathBase.Length + 1)..];
        }
        var q = UrlQueryString.FromUriComponent(absoluteUri);
        return this with
        {
            Path = path,
            PathBase = pathBase,
            Uri = absoluteUri,
            QueryString = q,
            Host = UrlHostString.FromUriComponent(absoluteUri),
            Query = new DictionaryQueryWrapper(q.Parse()),
            Scheme = absoluteUri.Scheme,
        };
    }

    private static MockRequest? _localhostRootHttp;
    public static MockRequest GetLocalhostRootHttp()
    {
        _localhostRootHttp ??= new MockRequest
        {
            Path = "/",
            PathBase = "",
            Uri = new Uri("http://localhost/"),
            QueryString = UrlQueryString.FromUriComponent(""),
            Host = UrlHostString.FromUriComponent("localhost"),
            Query = new DictionaryQueryWrapper(new Dictionary<string, StringValues>()),
            Scheme = "http",
            Protocol = "HTTP/1.1",
            Headers = new Dictionary<string, StringValues>(),
            Method = "GET"
        };
        return _localhostRootHttp;
    }
    
    public MockRequest WithUrlString(string uriString, 
        string pathBase = "", 
        string defaultHost = "localhost",
        string defaultScheme = "https")
    {
        
        Uri absoluteUri;
        // check if uriString is absolute and well formed
        // if not, prepend http://localhost or https://localhost
        if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri1))
        {
            absoluteUri = uri1;
        }
        else
        {
            if (uriString.StartsWith("//"))
            {
                uriString = $"{defaultScheme}:{uriString}";
            }else if (uriString.StartsWith("/"))
            {
                uriString = $"{defaultScheme}://{defaultHost}{uriString}";
            }
            else
            {
                uriString = $"{defaultScheme}://{uriString}";
            }
            
            absoluteUri = new Uri(uriString);
        }
        return WithAbsoluteUri(absoluteUri, pathBase);
    }
    
    public MockRequest WithHeader(string key, string value)
    {
        var headers = new Dictionary<string, StringValues>(Headers);
        headers[key] = value;
        return this with { Headers = headers };
    }
    
    public MockRequest WithEmptyBody()
    {
        return this with { BodyStream = new MemoryStream(), ContentLength = 0 };
    }
    
    public static MockRequest Get(string uriString,
        string pathBase = "", string defaultHost = "localhost", string defaultScheme = "https",
        IPAddress? remoteIpAddress = null, IPAddress? localIpAddress = null)

    {
        return GetLocalhostRootHttp()
            .WithUrlString(uriString, pathBase, defaultHost, defaultScheme)
            .WithEmptyBody();
    }

    /// <summary>
    /// Creates an HttpRequestStreamAdapterOptions object for a local request with optional pathBase.
    /// </summary>
    /// <param name="uriString">The URL string for the request.</param>
    /// <param name="pathBase">The base path for the request URL (optional).</param>
    /// <returns>An instance of HttpRequestStreamAdapterOptions for the local request.
    /// Specifies localhost as the host, https as the scheme, and GET as the method.
    /// server and client IP addresses are set to loopback.</returns>
    public static MockRequest GetLocalRequest(string uriString,
        string pathBase = "")

    {
        return GetLocalhostRootHttp()
            .WithUrlString(uriString, pathBase)
            .WithEmptyBody() 
            with { RemoteIpAddress = IPAddress.Loopback, LocalIpAddress = IPAddress.Loopback };
    }
    public MockRequestAdapter ToAdapter() => new (this);
}