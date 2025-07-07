using System.Buffers;
using System.IO.Pipelines;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Serving;
using Microsoft.AspNetCore.Http;

namespace Imageflow.Server.Internal;

public readonly record struct ResponseStreamAdapter(HttpResponse Response) : IHttpResponseStreamAdapter
{
    public void SetHeader(string name, string value)
    {
        Response.Headers[name] = value;
    }

    public void SetStatusCode(int statusCode)
    {
        Response.StatusCode = statusCode;
    }

    public int StatusCode => Response.StatusCode;

    public void SetContentType(string contentType)
    {
        Response.ContentType = contentType;
    }

    public string? ContentType => Response.ContentType;

    public void SetContentLength(long contentLength)
    {
        Response.ContentLength = contentLength;
    }

    public bool SupportsStream => true;

    public Stream GetBodyWriteStream()
    {
        return Response.Body;
    }

    public bool SupportsPipelines => true;
    public PipeWriter GetBodyPipeWriter()
    {
        return Response.BodyWriter;
    }

    public bool SupportsBufferWriter => true;
    public IBufferWriter<byte> GetBodyBufferWriter()
    {
        return Response.BodyWriter;
    }
}
