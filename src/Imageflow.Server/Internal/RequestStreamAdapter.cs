using System.IO.Pipelines;
using System.Net;
using Imazen.Abstractions.HttpStrings;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Serving;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace Imageflow.Server.Internal;

public readonly record struct RequestStreamAdapter(HttpRequest Request) : IHttpRequestStreamAdapter
{

    public UrlPathString GetPath() => new UrlPathString(Request.Path.Value);
 
    public Uri GetUri()
    {
        return new Uri(Request.GetEncodedUrl());
    }
    
    public string? TryGetServerVariable(string key)
    {
        return Request.HttpContext.Features.Get<IServerVariablesFeature>()?[key];
    }

    public UrlPathString GetPathBase() => new UrlPathString(Request.PathBase.Value);
  
    public IEnumerable<KeyValuePair<string, string>>? GetCookiePairs() => Request.Cookies;

    public IDictionary<string, StringValues> GetHeaderPairs() => Request.Headers;

    public bool TryGetHeader(string key, out StringValues value)
    {
        return Request.Headers.TryGetValue(key, out value);
    }
    public UrlQueryString GetQueryString() => new UrlQueryString(Request.QueryString.Value);
    public UrlHostString GetHost() => new UrlHostString(Request.Host.Value);
    public IReadOnlyQueryWrapper GetQuery() => new QueryCollectionWrapper(Request.Query);
    public bool TryGetQueryValues(string key, out StringValues value)
    {
        return Request.Query.TryGetValue(key, out value);
    }

    public T? GetHttpContextUnreliable<T>() where T : class
    {
        return Request.HttpContext as T;
    }

    public string? ContentType => Request.ContentType;
    public long? ContentLength => Request.ContentLength;
    public string Protocol  => Request.Protocol;
    public string Scheme => Request.Scheme;
    public string Method => Request.Method;
    public bool IsHttps => Request.IsHttps;
    public bool SupportsStream => true;

    public Stream GetBodyStream()
    {
        return Request.Body;
    }

    public IPAddress? GetRemoteIpAddress()
    {
        return Request.HttpContext.Connection.RemoteIpAddress;
    }

    public IPAddress? GetLocalIpAddress()
    {
        return Request.HttpContext.Connection.LocalIpAddress;
    }

    public bool SupportsPipelines => true;
    public PipeReader GetBodyPipeReader()
    {
        return Request.BodyReader;
    }
}
