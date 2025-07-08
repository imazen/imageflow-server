namespace Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Logging;

/// <summary>
/// Not thread safe
/// </summary>
public interface IConsumableBlob : IDisposable
{
    /// <summary>
    /// The attributes of the blob, such as its content type
    /// </summary>
    IBlobAttributes Attributes { get; }
    /// <summary>
    /// If true, the stream is available and can be borrowed.
    /// It can only be taken once.
    /// </summary>
    bool StreamAvailable { get; }

    /// <summary>
    /// If not null, specifies the length of the stream in bytes
    /// </summary>
    long? StreamLength { get; }

    bool IsDisposed { get; }

    /// <summary>
    /// Borrows the stream from this blob wrapper. Can only be called once. The IConsumableBlob wrapper should not be disposed until after the stream is disposed as there may
    /// be associated resources that need to be cleaned up.
    /// </summary>
    /// <returns></returns>
    Stream BorrowStream(DisposalPromise callerPromises);

    IReLogger? TryGetLogger();
}