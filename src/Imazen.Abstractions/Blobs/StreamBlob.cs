using System.Collections.Concurrent;
using System.Diagnostics;
using Imazen.Abstractions.Logging;

namespace Imazen.Abstractions.Blobs;

public sealed class StreamBlob : IConsumableBlob
{
    public IBlobAttributes Attributes { get; }
    private Stream? _stream;
    private DisposalPromise? disposalPromise = default;
    private bool disposed = false;
    
    public bool IsDisposed => disposed;
    private IDisposable? disposeAfterStream1;

    private readonly string streamType;
    private int instanceId;
    
    private readonly IReLogger? logger;
    public IReLogger? TryGetLogger() => logger;

    private static int _instanceCount = 0;
#if DEBUG
    private static readonly ConcurrentDictionary<StreamBlob,bool> Instances = new();
    private StackTrace? creationStackTrace;
#endif 
    internal static string DebugInstances()
    {
#if DEBUG
        return string.Join("\n", Instances.Select(x => x.ToString()).OrderBy(x => x));
#else
        return "Debug mode not enabled";
#endif
    }
    
    public StreamBlob(IBlobAttributes attrs, Stream stream, IReLogger? logger = null, IDisposable? disposeAfterStream = null)
    {
        this.logger = logger;
        disposeAfterStream1 = disposeAfterStream;
        Attributes = attrs;
        _stream = stream;
        StreamLength = stream.CanSeek ? (long?)stream.Length : null;
        streamType = stream.GetType().Name;
        if (stream is FileStream fs)
        {
            streamType = $"{streamType} ({fs.Name})";
        }
        instanceId = _instanceCount;
        _instanceCount++;
#if DEBUG
        Instances[this] = true;
        creationStackTrace = new StackTrace();
        // cleanup disposed instances
        foreach (var key in Instances.Where(x => x.Key.disposed).Select(x => x.Key))
        {
            Instances.TryRemove(key, out _);
        }
#endif
    }
    
    private string WhoCreatedMe()
    {
        #if DEBUG
        return "Created by: " + creationStackTrace?.ToString();
        #else
        return "";
#endif
    }

    public override string ToString()
    {
        if (disposed) return $"StreamBlob<{streamType}>[{instanceId}] (Disposed)";
        if (disposalPromise.HasValue) return $"StreamBlob<{streamType}>[{instanceId}] (CanRead={_stream?.CanRead}, Stream taken with {disposalPromise})";
        return $"StreamBlob<{streamType}>[{instanceId}] (CanRead={_stream?.CanRead}, Stream unused and open, created by {WhoCreatedMe()})";
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (disposalPromise != DisposalPromise.CallerDisposesStreamThenBlob)
        {
            _stream?.Dispose();
            _stream = null;
        }
        disposeAfterStream1?.Dispose();
        disposeAfterStream1 = null;
    }

    public bool StreamAvailable => !disposalPromise.HasValue;
    public long? StreamLength { get; }

    public Stream BorrowStream(DisposalPromise callerPromises)
    {
        if (disposed) throw new ObjectDisposedException("The ConsumableBlob has been disposed");
        if (!StreamAvailable) throw new InvalidOperationException("Stream has already been taken");
        disposalPromise = callerPromises;
        return _stream ?? throw new ObjectDisposedException("The ConsumableBlob has been disposed");
    }
}