using System.Buffers;
using System.IO.Pipelines;
using System.Reflection;

namespace Imazen.Routing.HttpAbstractions;

/// <summary>
/// Allows writing to a response stream and setting headers
/// </summary>
public interface IHttpResponseStreamAdapter
{

    void SetHeader(string name, string value);
    void SetStatusCode(int statusCode);
    int StatusCode { get; }
    void SetContentType(string contentType);
    string? ContentType { get; }
    void SetContentLength(long contentLength);

    bool SupportsStream { get; }
    Stream GetBodyWriteStream();
    
    bool SupportsPipelines { get; }
    PipeWriter GetBodyPipeWriter();
    
    bool SupportsBufferWriter { get; }
    IBufferWriter<byte> GetBodyBufferWriter();
}