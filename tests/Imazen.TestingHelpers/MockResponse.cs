using System.Text;
using Imazen.Routing.Serving;

namespace Imazen.Routing.Tests.Serving;

public record MockResponse
{
    public MockResponse(MockResponseAdapter adapter, byte[] body)
    {
        StatusCode = adapter.StatusCode;
        ContentType = adapter.ContentType;
        ContentLength = adapter.ContentLength;
        Headers = adapter.Headers;
        Body = body;
    }
    public MockResponseStreamType EnabledStreams { get; init; }
    public int StatusCode { get; init; }
    public string? ContentType { get; init; }
    public long ContentLength { get; init; }
    public Dictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public byte[] Body { get; init; }

    public string DecodeBodyUtf8()
    {
        return Encoding.UTF8.GetString(Body);
    }
}