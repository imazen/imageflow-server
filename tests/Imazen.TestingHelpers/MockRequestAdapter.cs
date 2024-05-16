using System.IO.Pipelines;
using System.Net;
using Imazen.Abstractions.HttpStrings;
using Imazen.Routing.HttpAbstractions;
using Microsoft.Extensions.Primitives;

namespace Imazen.Routing.Tests.Serving;

public class MockRequestAdapter(MockRequest options) : IHttpRequestStreamAdapter
{
    public string? TryGetServerVariable(string key)
    {
        return null;
    }

    public UrlPathString GetPath() => options.Path;
    public UrlPathString GetPathBase() => options.PathBase;
    public Uri GetUri() => options.Uri;
    public UrlQueryString GetQueryString() => options.QueryString;
    public UrlHostString GetHost() => options.Host;
    public IEnumerable<KeyValuePair<string, string>>? GetCookiePairs() => options.Cookies;
    public IDictionary<string, StringValues> GetHeaderPairs() => options.Headers;
    public IReadOnlyQueryWrapper GetQuery() => options.Query;
    public bool TryGetHeader(string key, out StringValues value) => options.Headers.TryGetValue(key, out value);
    public bool TryGetQueryValues(string key, out StringValues value) => options.Query.TryGetValue(key, out value);
    public T? GetHttpContextUnreliable<T>() where T : class
    {
        return null;
    }

    public string? ContentType => options.ContentType;
    public long? ContentLength => options.ContentLength;
    public string Protocol => options.Protocol;
    public string Scheme => options.Scheme;
    public string Method => options.Method;
    public bool IsHttps => options.IsHttps;
    public bool SupportsStream => options.BodyStream != null;
    public Stream GetBodyStream() => options.BodyStream ?? throw new NotSupportedException();
    public IPAddress? GetRemoteIpAddress() => options.RemoteIpAddress;
    public IPAddress? GetLocalIpAddress() => options.LocalIpAddress;
    public bool SupportsPipelines => options.SupportsPipelines;
    public PipeReader GetBodyPipeReader() => options.BodyPipeReader ?? throw new NotSupportedException();
}