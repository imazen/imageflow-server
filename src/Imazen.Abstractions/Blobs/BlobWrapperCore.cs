using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Imazen.Abstractions.Blobs;

internal class BlobWrapperCore : IDisposable{
    public IBlobAttributes Attributes { get; }
    private StreamBlob? consumable;
    private MemoryBlob? reusable;
    internal ILogger? Logger { get; set; }
    public long? EstimateAllocatedBytes => reusable?.EstimateAllocatedBytesRecursive;
    internal DateTime CreatedAtUtc { get; }
    internal LatencyTrackingZone? LatencyZone { get; set; }
    public bool IsReusable => reusable != null;

    public BlobWrapperCore(LatencyTrackingZone? latencyZone, StreamBlob consumable)
    {
        this.consumable = consumable;
        this.Attributes = consumable.Attributes;
        CreatedAtUtc = DateTime.UtcNow;
        LatencyZone = latencyZone;
    }
    public BlobWrapperCore(LatencyTrackingZone? latencyZone, MemoryBlob reusable)
    {
        this.reusable = reusable;
        this.Attributes = reusable.Attributes;
        CreatedAtUtc = DateTime.UtcNow;
        LatencyZone = latencyZone;
    }
        
    public async ValueTask EnsureReusable(CancellationToken cancellationToken = default)
    {
        if (reusable != null) return;
        if (consumable != null)
        {
            IConsumableBlob c = consumable;
            if (c != null)
            {
                try
                {
                    consumable = null;
                    if (!c.StreamAvailable)
                    {
                        throw new InvalidOperationException("Cannot create a reusable blob from this wrapper, the consumable stream has already been taken");
                    }

                    reusable = await ConsumeAndCreateReusableCopy(c, cancellationToken);
                    return;
                }
                finally
                {
                    c.Dispose();
                }
            }
        }

        throw new InvalidOperationException("Cannot take or create a reusable blob from this wrapper, it is empty");
    }

    private void CheckNeedsDispose()
    {
        if (memoryPromises == 0 && streamPromises == 0 && blobWrappers == 0 && memoryBlobProxies == 0)
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        consumable?.Dispose();
        reusable?.Dispose(); 
        consumable = null;
        reusable = null;
    }

    public bool TrySetLogger(ILogger logger)
    {
        if (Logger != null) return false;
        Logger = logger;
        return true;
    }


    private volatile int memoryPromises = 0;
    private volatile int streamPromises = 0;
    private volatile int allPromises = 0;
    private volatile int blobWrappers = 0;
    private volatile int memoryBlobProxies = 0;
    private bool _mustBuffer;

    private void AddWeakReference(MemoryBlobProxy blobWrapper)
    {
        Interlocked.Increment(ref memoryBlobProxies);
    }
    public void AddWeakReference(BlobWrapper blobWrapper)
    {
        Interlocked.Increment(ref blobWrappers);
    }
    public void RemoveReference(BlobWrapper blobWrapper)
    {
        Interlocked.Decrement(ref blobWrappers);
        CheckNeedsDispose();
    }

    private void RemoveReference(ConsumableMemoryBlobPromise blobWrapper)
    {
        Interlocked.Decrement(ref memoryPromises);
        Interlocked.Decrement(ref allPromises);
        CheckNeedsDispose();
    }


    private void RemoveReference(ConsumableBlobPromise blobWrapper)
    {
        Interlocked.Decrement(ref streamPromises);
        Interlocked.Decrement(ref allPromises);
        CheckNeedsDispose();
    }

    private void RemoveReference(MemoryBlobProxy blobWrapper)
    {
        Interlocked.Decrement(ref memoryBlobProxies);
        CheckNeedsDispose();
    }
    
    public IConsumableBlobPromise GetConsumablePromise(BlobWrapper blobWrapper)
    {
        Interlocked.Increment(ref streamPromises);
        Interlocked.Increment(ref allPromises);
        return new ConsumableBlobPromise(this);
    }
    private async ValueTask<IConsumableBlob> IntoConsumableBlob(ConsumableBlobPromise consumableBlobPromise)
    {
        // If we have any other promises open, we need to convert to reusable.
        // TODO: make thread-safe
        if (allPromises > 1 || _mustBuffer || IsReusable)
        {
            if (!IsReusable)
            {
                await EnsureReusable();
            }
        }

        if (IsReusable)
        {
            Interlocked.Increment(ref memoryBlobProxies);
            RemoveReference(consumableBlobPromise);
            return  MemoryBlobProxy.CreateWithoutAddingReference(reusable!, this);
        }
        
        var copyref = consumable;
        var result =Interlocked.CompareExchange(ref consumable, null, copyref);
        if (result == null) throw new InvalidOperationException("The consumable blob has already been taken");
        RemoveReference(consumableBlobPromise);
        return result;
    }
    
    public IConsumableMemoryBlobPromise GetConsumableMemoryPromise(BlobWrapper blobWrapper)
    {
        Interlocked.Increment(ref memoryPromises);
        Interlocked.Increment(ref allPromises);
        return new ConsumableMemoryBlobPromise(this);
    }
    
    private async ValueTask<IConsumableMemoryBlob> IntoConsumableMemoryBlob(ConsumableMemoryBlobPromise consumableMemoryBlobPromise)
    {
        Interlocked.Increment(ref memoryBlobProxies);
        RemoveReference(consumableMemoryBlobPromise);
        if (!IsReusable)
        {
            await EnsureReusable();
        }
        return MemoryBlobProxy.CreateWithoutAddingReference(reusable!, this);
    }



    private sealed class ConsumableBlobPromise(BlobWrapperCore core) : IConsumableBlobPromise
    {
        private bool disposed = false;
        private bool used = false;
        public void Dispose()
        {
            disposed = true;
            if (!used) core.RemoveReference(this);
        }

        public ValueTask<IConsumableBlob> IntoConsumableBlob()
        {
            if (disposed) throw new ObjectDisposedException("The ConsumableBlobPromise has been disposed");
            if (used) throw new InvalidOperationException("The ConsumableBlobPromise has already been used");
            used = true;
            return core.IntoConsumableBlob(this);
        }
    }
    
    private sealed class ConsumableMemoryBlobPromise(BlobWrapperCore core) : IConsumableMemoryBlobPromise
    {
        private bool disposed = false;
        private bool used = false;
        public void Dispose()
        {
            disposed = true;
            if (!used) core.RemoveReference(this);
        }

        public ValueTask<IConsumableMemoryBlob> IntoConsumableMemoryBlob()
        {
            if (disposed) throw new ObjectDisposedException("The ConsumableMemoryBlobPromise has been disposed");
            if (used) throw new InvalidOperationException("The ConsumableMemoryBlobPromise has already been used");
            used = true;
            return core.IntoConsumableMemoryBlob(this);
        }
    }
    
    private class MemoryBlobProxy : IConsumableMemoryBlob 
    {
        private MemoryBlob? memoryBlob;
        private bool proxyDisposed = false;
        private readonly BlobWrapperCore parent;

        private MemoryBlobProxy(MemoryBlob blob, BlobWrapperCore parent)
        {
            this.parent = parent;
            this.memoryBlob = blob;
        }
        
        public static MemoryBlobProxy CreateWithoutAddingReference(MemoryBlob blob, BlobWrapperCore parent)
        {
            return new MemoryBlobProxy(blob, parent);
        }

        public void Dispose()
        {
            if (proxyDisposed) return;
            proxyDisposed = true;
            memoryBlob = null;
            parent.RemoveReference(this);
        }

        public IBlobAttributes Attributes => proxyDisposed ? throw new ObjectDisposedException("The MemoryBlobProxy has been disposed") : memoryBlob!.Attributes;
        public bool StreamAvailable => !proxyDisposed && memoryBlob!.StreamAvailable;

        public long? StreamLength => proxyDisposed ? throw new ObjectDisposedException("The MemoryBlobProxy has been disposed") : memoryBlob!.StreamLength;

        public bool IsDisposed => proxyDisposed;

        public Stream BorrowStream(DisposalPromise callerPromises)
        {
            if (proxyDisposed) throw new ObjectDisposedException("The MemoryBlobProxy has been disposed");
            return memoryBlob!.BorrowStream(callerPromises);
        }

        public ReadOnlyMemory<byte> BorrowMemory => proxyDisposed ? throw new ObjectDisposedException("The MemoryBlobProxy has been disposed") : memoryBlob!.BorrowMemory;
    }

    private async ValueTask<MemoryBlob> ConsumeAndCreateReusableCopy(IConsumableBlob consumableBlob,
        CancellationToken cancellationToken = default)
    {
        using (consumableBlob)
        {
            var sw = Stopwatch.StartNew();
#if NETSTANDARD2_1_OR_GREATER 
             await using var stream = consumableBlob.BorrowStream(DisposalPromise.CallerDisposesStreamThenBlob);
#else
            using var stream = consumableBlob.BorrowStream(DisposalPromise.CallerDisposesStreamThenBlob);
#endif
            var ms = new MemoryStream(stream.CanSeek ? (int)stream.Length : 4096);
            await stream.CopyToAsync(ms, 81920, cancellationToken);
            ms.Position = 0;
            // TODO: trygetbuffer instead of toarray
            var byteArray = ms.ToArray();
            
            
            var arraySegment = new ArraySegment<byte>(byteArray);
            sw.Stop();
            var reusable = new MemoryBlob(arraySegment, consumableBlob.Attributes, sw.Elapsed);
            return reusable;
        }
    }

    public void IndicateInterest()
    {
        _mustBuffer = true;
    }
}