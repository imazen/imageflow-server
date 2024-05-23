using System.IO.Pipelines;
using Imazen.Abstractions.Blobs;

namespace Imazen.Routing.HttpAbstractions;

public static class HttpResponseStreamAdapterExtensions
{
    public static void WriteUtf8String(this IHttpResponseStreamAdapter response, string str)
    {
        if (response.SupportsBufferWriter)
        {
            var writer = response.GetBodyBufferWriter();
            writer.WriteUtf8String(str);
        }else if (response.SupportsStream)
        {
            var stream = response.GetBodyWriteStream();
            var bytes = System.Text.Encoding.UTF8.GetBytes(str);
            stream.Write(bytes, 0, bytes.Length);
        }
            
    }
    
    /// <summary>
    ///  Use MagicBytes.ProxyToStream instead if you don't know the content type. This method only sets the length of the string.
    /// The caller is responsible for disposing IConsumableBlob
    /// </summary>
    /// <param name="response"></param>
    /// <param name="blob"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public static async ValueTask WriteBlobWrapperBody(this IHttpResponseStreamAdapter response, IConsumableBlob blob, CancellationToken cancellationToken = default)
    {
        using var consumable = blob;
        if (!consumable.StreamAvailable) throw new InvalidOperationException("BlobWrapper must have a stream available");
#if DOTNET5_0_OR_GREATER
        await using var stream = consumable.BorrowStream(DisposalPromise.CallerDisposesStreamThenBlob);
#else
        using var stream = consumable.BorrowStream(DisposalPromise.CallerDisposesStreamThenBlob);
#endif 
        if (stream.CanSeek)
        {
            response.SetContentLength(stream.Length - stream.Position);
        }
        if (response.SupportsPipelines)
        {
            var writer = response.GetBodyPipeWriter();
            await stream.CopyToAsync(writer, cancellationToken);
        }else if (response.SupportsStream)
        {
            var writeStream = response.GetBodyWriteStream();
#if DOTNET5_0_OR_GREATER
            await stream.CopyToAsync(writeStream, cancellationToken);
#else
            await stream.CopyToAsync(writeStream, 81920, cancellationToken);
#endif 
        }
        else
        {
            throw new InvalidOperationException("IHttpResponseStreamAdapter must support either pipelines or streams in addition to buffer writers");
        }
    }
}