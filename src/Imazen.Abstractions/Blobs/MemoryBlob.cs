using System.Buffers;
using CommunityToolkit.HighPerformance;
using Imazen.Abstractions.Logging;

namespace Imazen.Abstractions.Blobs;

public sealed class MemoryBlob : IConsumableMemoryBlob, IReusableBlob
{
    public IBlobAttributes Attributes { get; }

    public IReLogger? TryGetLogger() => logger;
    private readonly IReLogger? logger;

    /// <summary>
    /// Creates a consumable wrapper over owner-less memory (I.E. owned by the garbage collector - it cannot be manually disposed)
    /// </summary>
    /// <param name="creationDuration"></param>
    /// <param name="attrs"></param>
    /// <param name="memory"></param>
    /// <param name="backingAllocationSize"></param>
    public MemoryBlob(ReadOnlyMemory<byte> memory,IBlobAttributes attrs, TimeSpan creationDuration, int? backingAllocationSize = null, IReLogger? logger = null):this(memory,attrs,creationDuration,backingAllocationSize,null,logger)
    {
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="creationDuration"></param>
    /// <param name="attrs"></param>
    /// <param name="memory"></param>
    /// <param name="backingAllocationSize"></param>
    /// <param name="owner"></param>
    internal MemoryBlob(ReadOnlyMemory<byte> memory,IBlobAttributes attrs, TimeSpan creationDuration, int? backingAllocationSize, IMemoryOwner<byte>? owner, IReLogger? logger = null)
    {
        this.logger = logger;
        this.Attributes = attrs;
        this.memory = memory;
        this.owner = owner;
        CreationDuration = creationDuration;
        var backingSize = backingAllocationSize ?? memory.Length;
        if (backingSize < memory.Length)
        {
            throw new ArgumentException("backingAllocationSize must be at least as large as memory.Length");
        }
        // Precalculate since it will be called often
        EstimateAllocatedBytesRecursive = 
            24 + Attributes.EstimateAllocatedBytesRecursive
               + 24 + (backingSize);
    }
    public TimeSpan CreationDuration { get; init; }
    public DateTime CreationCompletionUtc { get; init; } = DateTime.UtcNow;
    
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        memory = default;
        owner?.Dispose();
        owner = null;
    }
    private IMemoryOwner<byte>? owner;
    private ReadOnlyMemory<byte> memory;
    private bool disposed = false;
    public bool IsDisposed => disposed;
    public ReadOnlyMemory<byte> BorrowMemory
    {
        get
        {
            if (disposed) throw new ObjectDisposedException("The ConsumableMemoryBlob has been disposed");
            return memory;
        }
    }

    public bool StreamAvailable => !disposed;
    public long? StreamLength => !disposed ? memory.Length : default;
    public Stream BorrowStream(DisposalPromise callerPromises)
    {
        if (disposed) throw new ObjectDisposedException("The ConsumableMemoryBlob has been disposed. .BorrowStream is an invalid operation on a disposed object");
        if (callerPromises != DisposalPromise.CallerDisposesStreamThenBlob) throw new ArgumentException("callerPromises must be DisposalPromise.CallerDisposesStreamThenBlob");
        return memory.AsStream();
    }

    public int EstimateAllocatedBytesRecursive { get; }
}