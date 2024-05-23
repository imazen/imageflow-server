using System.Net;
using System.Text;

namespace Imazen.Routing.HttpAbstractions;

public static class HttpRequestStreamAdapterExtensions
{
    public static string ToShortString(this IHttpRequestStreamAdapter request)
    {
        // GET /path?query HTTP/1.1
        // Host: example.com:443
        // {count} Cookies
        // {count} Headers
        // Referer: example.com
        // Supports=pipelines,streams
        
        var sb = new StringBuilder();
        sb.Append(request.Method);
        sb.Append(" ");
        sb.Append(request.GetPathBase().Value);
        sb.Append(request.GetPath().Value);
        sb.Append(request.GetQueryString());
        sb.Append(" ");
        sb.Append(request.Protocol);
        sb.Append("\r\n");
        sb.Append("Host: ");
        sb.Append(request.GetHost());
        sb.Append("\r\n");
        var cookies = request.GetCookiePairs();
        if (cookies != null)
        {
            sb.Append(cookies.Count());
            sb.Append(" Cookies\r\n");
        }
        var headers = request.GetHeaderPairs();
        sb.Append(headers.Count);
        sb.Append(" Headers\r\n");
        foreach (var header in headers)
        {
            sb.Append(header.Key);
            sb.Append(": ");
            sb.Append(header.Value);
            sb.Append("\r\n");
        }
        sb.Append("[Supports=");
        if (request.SupportsPipelines)
        {
            sb.Append("PipeReader,");
        }
        if (request.SupportsStream)
        {
            sb.Append("Stream");
        }
        sb.Append("]");
        return sb.ToString();
    }
    
    public static string GetServerHost(this IHttpRequestStreamAdapter request)
    {
        var host = request.GetHost();
        return host.HasValue ? host.Value : "localhost";
    }
    public static string? GetRefererHost(this IHttpRequestStreamAdapter request)
    {
        if (request.GetHeaderPairs().TryGetValue("Referer", out var refererValues))
        {
            if (refererValues.Count > 0)
            {
                var referer = refererValues.ToString();
                if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var result))
                {
                    return result.DnsSafeHost;
                }
            }
                
        }
        return null;
    }
    public static bool IsClientLocalhost(this IHttpRequestStreamAdapter request)
    {
        // What about when a dns record is pointing to the local machine?
        var serverIp = request.GetLocalIpAddress();
        if (serverIp == null)
        {
            return false;
        }
        var clientIp = request.GetRemoteIpAddress();
        if (clientIp == null)
        {
            return false;
        }
        
        if (IPAddress.IsLoopback(clientIp))
        {
            return true;
        }
        // if they're the same
        if (serverIp.Equals(clientIp))
        {
            return true;
        }
        return false;
    }
}