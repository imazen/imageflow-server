using System.IO.Pipelines;
using System.Net;
using Imazen.Abstractions.HttpStrings;
using Imazen.Routing.Requests;
using Microsoft.Extensions.Primitives;

namespace Imazen.Routing.HttpAbstractions;


// A convenience class for creating IHttpRequestStreamAdapter instances for testing.
public readonly record struct EmptyHttpRequest : IHttpRequestStreamAdapter
{
    private readonly string path;
    private readonly string queryString;
    private readonly IDictionary<string, StringValues> query;
    private readonly string host;
    private readonly Dictionary<string, StringValues> headers;
    private readonly Dictionary<string, string> cookies;

    public EmptyHttpRequest(
        IRequestSnapshot request) : this(request.Path, query: request.QueryString, method: request.HttpMethod){}

    public EmptyHttpRequest(
        string path,
        string queryString = "",
        IDictionary<string, StringValues>? query = null,
        string host = "localhost",
        string? method = null,
        string? contentType = null,
        Dictionary<string, StringValues>? headers = null,
        Dictionary<string, string>? cookies = null)
    {
        this.path = path;
        if (query != null)
        {
            this.query = query;
            this.queryString = UrlQueryString.Create(query).ToString();
        }
        else
        {
            this.queryString = queryString;
            this.query = GetQueryString().Parse();
        }


        this.host = host;
        this.Method = method ?? "GET";
        this.ContentType = contentType;
        this.headers = headers ?? new Dictionary<string, StringValues>();
        this.cookies = cookies ?? new Dictionary<string, string>();
    }

    public UrlPathString GetPath() => new UrlPathString(path);
    public Uri GetUri() => new Uri($"http://{host}{path}{queryString}");
    public string? TryGetServerVariable(string key) => null;
    public UrlPathString GetPathBase() => new UrlPathString(string.Empty);
    public IEnumerable<KeyValuePair<string, string>>? GetCookiePairs() => cookies;
    public IDictionary<string, StringValues> GetHeaderPairs() => headers;
    public bool TryGetHeader(string key, out StringValues value) => headers.TryGetValue(key, out value);
    public UrlQueryString GetQueryString() => new UrlQueryString(queryString);
    public UrlHostString GetHost() => new UrlHostString(host);
    public IReadOnlyQueryWrapper GetQuery() => new DictionaryQueryWrapper(query);

    public bool TryGetQueryValues(string key, out StringValues value) =>
        query.TryGetValue(key, out value);

    public T? GetHttpContextUnreliable<T>() where T : class => null;
    public string? ContentType { get; } = null;

    public long? ContentLength => 0;
    public string Protocol => "HTTP/1.1";
    public string Scheme => "http";
    public string Method { get; } = "GET";
    public bool IsHttps => false;
    public bool SupportsStream => true;
    public Stream GetBodyStream() => Stream.Null;
    public IPAddress? GetRemoteIpAddress() => null;
    public IPAddress? GetLocalIpAddress() => null;
    public bool SupportsPipelines => false;
    public PipeReader GetBodyPipeReader() => throw new NotSupportedException();
}

