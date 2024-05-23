using System.IO.Pipelines;
using System.Net;
using Imazen.Abstractions.HttpStrings;
using Microsoft.Extensions.Primitives;

namespace Imazen.Routing.HttpAbstractions;

/// <summary>
/// 
/// 
/// </summary>
public interface IHttpRequestStreamAdapter
{
    // Microsoft.AspNetCore.Http.HttpRequest and System.Web.HttpRequest use different types for their properties.
    // PathString, QueryString, and PathBase are all strings in System.Web.HttpRequest, but are structs in Microsoft.AspNetCore.Http.HttpRequest.
    // https://source.dot.net/#Microsoft.AspNetCore.Http.Abstractions/PathString.cs,ff3b073b1591ea45
    // https://source.dot.net/#Microsoft.AspNetCore.Http.Abstractions/HostString.cs,677c1978743f8e43
    // https://source.dot.net/#Microsoft.AspNetCore.Http.Abstractions/QueryString.cs,b704925bb788f6c6


    /// <summary>
    /// May be empty if PathBase contains the full path (like for the doc root).
    /// Path should be fully decoded except for '%2F' which translates to / and would change path parsing.
    /// </summary>
    /// <returns></returns>

    string? TryGetServerVariable(string key);
    
    UrlPathString GetPath();
    
    /// <summary>
    /// Should not end with a trailing slash. If the app is mounted in a subdirectory, this will contain that part of the path.
    /// If no subdir mounting is supported, should return String.Empty.
    /// </summary>
    /// <returns></returns>

    UrlPathString GetPathBase();
    
    Uri GetUri();
    
    UrlQueryString GetQueryString();
    UrlHostString GetHost();
    
    IEnumerable<KeyValuePair<string, string>>? GetCookiePairs();
    IDictionary<string, StringValues> GetHeaderPairs();
    IReadOnlyQueryWrapper GetQuery();
    bool TryGetHeader(string key, out StringValues value);
    bool TryGetQueryValues(string key, out StringValues value);
  
    
    /// <summary>
    /// Avoid using this if possible.
    /// It's a hack to get around the fact that HttpContext is not a public interface and this
    /// interface is used in both non-web contexts and situations where HttpContext may or may not be availble
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    T? GetHttpContextUnreliable<T>() where T : class;
   
    string? ContentType { get; }
    long? ContentLength { get; }
    string Protocol { get; }
    string Scheme { get; }
    string Method { get; }
    bool IsHttps { get; }
    
    bool SupportsStream { get; }
    Stream GetBodyStream();
    
    IPAddress? GetRemoteIpAddress();
    IPAddress? GetLocalIpAddress();
    
    
    bool SupportsPipelines { get; }
    PipeReader GetBodyPipeReader();

}

// extension helper for local request