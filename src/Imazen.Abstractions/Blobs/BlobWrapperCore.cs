using System.Collections.Concurrent;
using System.Data;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Imazen.Abstractions.Logging;
using Microsoft.Extensions.Logging;

namespace Imazen.Abstractions.Blobs;


internal sealed class BlobWrapperCore : IDisposable
{
    [Flags]
    private enum BlobWrapperSource:byte{
        Unknown,
        StreamBlob =1,
        MemoryBlob =2,

    }

    [Flags]
    private enum BlobWrapperReferenceType:byte{
        Unknown,
        BlobWrapper =1,
        MemoryBlobProxy =2,
        MemoryPromise =4,
        StreamPromise =8,
    }

    private record struct OutstandingReference(
        BlobWrapperReferenceType Type,
        StackTrace StackTrace,
        int Order
    );

    public IBlobAttributes Attributes { get; }
    private StreamBlob? consumable;
    // For the consumable, it can be consumed either by promotion to reusable or by a single
    // promise being redeemed. Multiple concurrent promotion calls could occur.
    private Lazy<SemaphoreSlim> consumeLock = new(() => new SemaphoreSlim(1));
    private MemoryBlob? reusable;

    internal IReLogger? Logger { get; set; }
    public long? EstimateAllocatedBytes => reusable?.EstimateAllocatedBytesRecursive;
    internal DateTime CreatedAtUtc { get; }
    internal LatencyTrackingZone? LatencyZone { get; set; }
    public bool IsReusable => reusable != null;

    private bool disposed = false;

#if DEBUG
    private readonly long id = Interlocked.Increment(ref _instanceCounter);
    private BlobWrapperSource source = BlobWrapperSource.Unknown;
    private readonly StackTrace? creationStackTrace;

    private readonly ConcurrentDictionary<object,OutstandingReference> outstandingReferences = new();
    private int referenceOrder = 0;


    private static long _instanceCounter; 
    private static readonly ConcurrentDictionary<BlobWrapperCore,bool> Instances = new();
#endif

    private static string FormatStackStrace(StackTrace? stackTrace)
    {
#if NET6_0_OR_GREATER
        var splitObj = "\n   at ";
#else
        var splitObj = '\n';
#endif
#if DEBUG
        if (stackTrace == null) return "at (not compiled in DEBUG mode)";
var stackTraceString = stackTrace.ToString();

        var sb = new StringBuilder();
        sb.AppendLine();
        var lines = stackTraceString.Split(splitObj);
        var ix = 0;
        foreach (var line in lines)
        {
            if (ix == 0 && line.Contains("Imazen.Routing.Layers.LocalFilesLayer.FilePromise"))
            {
                return " (from Imazen.Routing.Layers.LocalFilesLayer)";
            }
            if (ix == 0 &&line.Contains("Imazen.Routing.Promises.Pipelines.ImagingPromise")) return " (from Imazen.Routing.Promises.Pipelines.ImagingPromise)"; //Imazen.Routing.Promises.Pipelines.ImagingPromise
            if (ix > 0) sb.Append(splitObj);
            sb.Append(line);
            if (ix++ > 6) break;
        }
        
        return sb.ToString();
#else
        return "at (not compiled in DEBUG mode)";
#endif
    }
    public BlobWrapperCore(LatencyTrackingZone? latencyZone, StreamBlob consumable, IReLogger? logger)
    {
        Logger = logger?.WithSubcategory("BlobWrapperCore").WithReScopeData("Blob", id);
        
#if DEBUG
        creationStackTrace = new StackTrace(2, true);
        source = BlobWrapperSource.StreamBlob;
        Logger?.LogTrace("Blob {id} created around StreamBlob {reference}. Stack Trace: {StackTrace}", id, consumable.Attributes.BlobStorageReference?.GetFullyQualifiedRepresentation(), FormatStackStrace(creationStackTrace));
        
#endif
        this.consumable = consumable;
        this.Attributes = consumable.Attributes;
        CreatedAtUtc = DateTime.UtcNow;
        LatencyZone = latencyZone;
        UpdateTrackingDictionary();
    }
    public BlobWrapperCore(LatencyTrackingZone? latencyZone, MemoryBlob reusable, IReLogger? logger)
    {
        Logger = logger?.WithSubcategory("BlobWrapperCore").WithReScopeData("Blob", id);
        
#if DEBUG
        creationStackTrace = new StackTrace(2, true);
        source = BlobWrapperSource.MemoryBlob;
        Logger?.LogTrace("Blob {id} created around MemoryBlob {reference}. Stack Trace: {StackTrace}", id, reusable.Attributes.BlobStorageReference?.GetFullyQualifiedRepresentation(), FormatStackStrace(creationStackTrace));
        
#endif
        this.reusable = reusable;
        this.Attributes = reusable.Attributes;
        CreatedAtUtc = DateTime.UtcNow;
        LatencyZone = latencyZone;
        UpdateTrackingDictionary();
    }


    public async ValueTask EnsureReusable(CancellationToken cancellationToken = default)
    {
        if (reusable != null) return;

        await consumeLock.Value.WaitAsync(cancellationToken);
        try
        {
            await EnsureReusableUnsynchronized(cancellationToken);
        }
        finally
        {
            consumeLock.Value.Release();
        }
    }

    private async ValueTask EnsureReusableUnsynchronized(CancellationToken cancellationToken = default)
    {
        if (reusable != null) return;
#if DEBUG
        Logger?.LogTrace("Blob {id} buffering - EnsureReusable(). Stack Trace: {StackTrace}", id, FormatStackStrace(new StackTrace(2,true)));
#endif
        if (consumable != null)
        { 
            StreamBlob c = consumable;
            if (c != null)
            {
                try
                {
#if DEBUG
                    var sw = Stopwatch.StartNew();
#endif
                    consumable = null;
                    if (!c.StreamAvailable)
                    {
                        throw new InvalidOperationException("Cannot create a reusable blob from this wrapper, the consumable stream has already been taken");
                    }

                    reusable = await ConsumeAndCreateReusableCopy(c, cancellationToken);
#if DEBUG
                    sw.Stop();
                    Logger?.LogTrace("Blob {id} fully buffered in {elapsedMs}ms - EnsureReusable() finished.", id, sw.ElapsedMilliseconds);
#endif
                    return;
                }
                finally
                {
                    c.Dispose();
                }
            }
        }
        Logger?.LogWarning("Blob {id} failed to buffer, it is empty - EnsureReusable() failed with exception. Stack Trace: {StackTrace}", id, new StackTrace(2, true).ToString());

        throw new InvalidOperationException("Cannot take or create a reusable blob from this wrapper, it is empty");
    }

    private async ValueTask<IConsumableBlob> IntoConsumableBlob(ConsumableBlobPromise consumableBlobPromise)
    {
        if (IsReusable)
        {
            Interlocked.Increment(ref memoryBlobProxies);
            RemoveReference(consumableBlobPromise);
            var proxy = MemoryBlobProxy.CreateWithoutAddingReference(reusable!, this);
            AddReference(proxy, false);
            return proxy;
        }
        await consumeLock.Value.WaitAsync();
        try
        {
            // If we have any other promises open, we need to convert to reusable.
            if (allPromises > 1 || _mustBuffer || IsReusable)
            {
                if (!IsReusable)
                {
                    await EnsureReusableUnsynchronized();
                }
            }

            if (IsReusable)
            {
                Interlocked.Increment(ref memoryBlobProxies);
                RemoveReference(consumableBlobPromise);
                var proxy = MemoryBlobProxy.CreateWithoutAddingReference(reusable!, this);
                AddReference(proxy, false);
                return proxy;
            }

            Logger?.LogTrace("Blob {id} [CONSUME] converting to consumable blob. Stack Trace: {StackTrace}", id, FormatStackStrace(new StackTrace(2, true)));
            var copyref = consumable;
            var result = Interlocked.CompareExchange(ref consumable, null, copyref);
            if (result == null) throw new InvalidOperationException("The consumable blob has already been taken");
            RemoveReference(consumableBlobPromise);
            return result;
        }
        finally
        {
            consumeLock.Value.Release();
        }
    }
   private void UpdateTrackingDictionary(){
        #if DEBUG
        Instances[this] = !disposed;
        // cleanup disposed instances
        foreach (var key in Instances.Where(x => x.Key.disposed).Select(x => x.Key))
        {
            Instances.TryRemove(key, out _);
        }
        #endif
    }
    private string ReferenceCounterString()
    {
        var total = memoryPromises + streamPromises + blobWrappers + memoryBlobProxies;
        return $"{total} references: {blobWrappers} BlobWrappers, {memoryBlobProxies} MemoryBlobProxies, {memoryPromises} memory promises, {streamPromises} stream promises";
    }
     private string ToDebugString()
    {
#if DEBUG
        var sb = new StringBuilder();
        var lifespan = DateTime.UtcNow - CreatedAtUtc;
        var total = memoryPromises + streamPromises + blobWrappers + memoryBlobProxies;
        sb.AppendLine($"BlobWrapper #{id} has lived {lifespan.TotalMilliseconds}ms and has {total} refs. {Attributes.BlobStorageReference?.GetFullyQualifiedRepresentation()} Created from {source} {FormatStackStrace(creationStackTrace)}");
        sb.AppendLine($"BlobWrapper #{id} has {ReferenceCounterString()}. Referenced by: ");
        int ix = 1;
        var count = outstandingReferences.Count;
        foreach (var reference in outstandingReferences)
        {
            sb.AppendLine($" - Remaining reference {ix++}/{count} (added as #{reference.Value.Order} of #{referenceOrder}): {reference.Value.Type} added {FormatStackStrace(reference.Value.StackTrace)}");
        }
        return sb.ToString();
#else
        return "active instance tracking disabled (not in DEBUG mode)";
#endif
    }

    private static bool Include(BlobWrapperCore item, IReLogger filterBy)
    {
        if (filterBy is ReLogger r)
        {
            return r.SharesParentWith(item.Logger as ReLogger);
        }
        return false;
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="filterBy">Multiple hosts will f things up - pass your logger here so we can filter them</param>
    /// <returns></returns>
    internal static int GetActiveInstanceCount(IReLogger? filterBy)
    {
#if DEBUG
        return Instances.Count(x => x.Value && x.Key.disposed == false && (filterBy == null || Include(x.Key, filterBy)));
#else
        return 0;
#endif
    }
    internal static string GetActiveInstanceInfo(IReLogger? filterBy)
    {
#if DEBUG
        var unfilteredCount = GetActiveInstanceCount(null);
        var filteredCount = GetActiveInstanceCount(filterBy);
        var sb = new StringBuilder();
        sb.AppendLine($"Active BlobWrapperCore instances: Local/filtered: {filteredCount}, Process-wide: {unfilteredCount} (of {_instanceCounter} created)");
        var now = DateTime.UtcNow;
        Instances.Where(x => x.Value && x.Key.disposed == false && (filterBy == null || Include(x.Key, filterBy))).OrderByDescending(x => now - x.Key.CreatedAtUtc).ToList().ForEach(x => sb.AppendLine(x.Key.ToDebugString()));
        return sb.ToString();
#else
        return "active instance tracking disabled (not in DEBUG mode)";
#endif
    }

    private bool HasOutstandingReferences()
    {
        return allPromises > 0 || memoryPromises > 0 || streamPromises > 0 || blobWrappers > 0 || memoryBlobProxies > 0;
    }
    private void CheckNeedsDispose()
    {
        if (!HasOutstandingReferences())
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        // TODO: log if called while references still exist.
    #if DEBUG
        if (disposed)
        {
            Logger?.LogInformation("Blob {id} already disposed. Dispose() called. Stack Trace: {StackTrace}", id, FormatStackStrace(new StackTrace(2, true)));
            return;
        }
        if (HasOutstandingReferences())
        {
            Logger?.LogInformation("Blob {id} Dispose() called while outstanding references exist. {DebugString}", id, ToDebugString());
        }
        var sw = Stopwatch.StartNew();
    #endif
        consumable?.Dispose();
        reusable?.Dispose(); 
        consumable = null;
        reusable = null;
        disposed = true;
        UpdateTrackingDictionary();
    #if DEBUG
        sw.Stop();
        if (sw.ElapsedMilliseconds > 100)
        {
            Logger?.LogWarning("Blob {id} disposed in {elapsedMs}ms. Dispose() called. {DebugString}", id, sw.ElapsedMilliseconds, ToDebugString());
        }
    #endif
    }


    private volatile int memoryPromises = 0;
    private volatile int streamPromises = 0;
    private volatile int allPromises = 0;
    private volatile int blobWrappers = 0;
    private volatile int memoryBlobProxies = 0;
    private bool _mustBuffer;

    private void AddTrackingRef(object reference, BlobWrapperReferenceType type)
    {
#if DEBUG
        var r = new OutstandingReference(type, new StackTrace(3, true), Interlocked.Increment(ref referenceOrder));
        outstandingReferences.TryAdd(reference, r);
        Logger?.LogInformation("Blob {id} has new {type} reference #{order}. {RefCounts}", id, type, r.Order, ReferenceCounterString());
#endif
    }

    private void RemoveTrackingRef(object reference)
    {
#if DEBUG
        if (outstandingReferences.TryRemove(reference, out var r))
        {
            Logger?.LogInformation("Blob #{id} has lost {type} reference #{order}. {RefCounts}", id, r.Type, r.Order, ReferenceCounterString());
        }else{
            Logger?.LogError("Blob #{id} has double-removed reference to {reference} that was not registered. {RefCounts}", id, reference, ReferenceCounterString());
        }
#endif
    }

    private void AddReference(MemoryBlobProxy memoryBlobProxy, bool increment = true)
    {
        if (increment) Interlocked.Increment(ref memoryBlobProxies);
        AddTrackingRef(memoryBlobProxy, BlobWrapperReferenceType.MemoryBlobProxy);
    }
    public void AddWeakReference(BlobWrapper blobWrapper)
    {
        var count = Interlocked.Increment(ref blobWrappers);
        AddTrackingRef(blobWrapper, BlobWrapperReferenceType.BlobWrapper);
    }
    public void RemoveReference(BlobWrapper blobWrapper)
    {
        var count = Interlocked.Decrement(ref blobWrappers);
        RemoveTrackingRef(blobWrapper);
        Logger?.LogInformation("Blob {id} RemoveReference(BlobWrapper). New count: {count}. Stack Trace: {StackTrace}", id, count, FormatStackStrace(new StackTrace(true)));
        CheckNeedsDispose();
    }

    private void RemoveReference(ConsumableMemoryBlobPromise consumableMemoryBlobPromise)
    {
        Interlocked.Decrement(ref memoryPromises);
        Interlocked.Decrement(ref allPromises);
        RemoveTrackingRef(consumableMemoryBlobPromise);
        CheckNeedsDispose();
    }


    private void RemoveReference(ConsumableBlobPromise consumableBlobPromise)
    {
        Interlocked.Decrement(ref streamPromises);
        Interlocked.Decrement(ref allPromises);
        RemoveTrackingRef(consumableBlobPromise);
        CheckNeedsDispose();
    }

    private void RemoveReference(MemoryBlobProxy memoryBlobProxy)
    {
        Interlocked.Decrement(ref memoryBlobProxies);
        RemoveTrackingRef(memoryBlobProxy);
        CheckNeedsDispose();
    }

    public IConsumableBlobPromise GetConsumablePromise(BlobWrapper blobWrapper)
    {
        var sPromises = Interlocked.Increment(ref streamPromises);
        var aPromises = Interlocked.Increment(ref allPromises);
    #if DEBUG
        Logger?.LogTrace("Blob {id} GetConsumablePromise. streamPromises: {sPromises}, allPromises: {aPromises}. Stack Trace: {StackTrace}", id, sPromises, aPromises, FormatStackStrace(new StackTrace(2, true)));
    #endif
        return new ConsumableBlobPromise(this);
    }

    
    public IConsumableMemoryBlobPromise GetConsumableMemoryPromise(BlobWrapper blobWrapper)
    {
        var mPromises = Interlocked.Increment(ref memoryPromises);
        var aPromises = Interlocked.Increment(ref allPromises);
    #if DEBUG
        Logger?.LogTrace("Blob {id} GetConsumableMemoryPromise. memoryPromises: {mPromises}, allPromises: {aPromises}. Stack Trace: {StackTrace}", id, mPromises, aPromises,  FormatStackStrace(new StackTrace(2, true)));
    #endif
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
        var proxy = MemoryBlobProxy.CreateWithoutAddingReference(reusable!, this);
        AddReference(proxy, false);
        return proxy;
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
        private readonly IReLogger? logger;

        public IReLogger? TryGetLogger() => logger;

        private MemoryBlobProxy(MemoryBlob blob, BlobWrapperCore parent)
        {
            this.parent = parent;
            this.memoryBlob = blob;
            this.logger = parent.Logger?.WithSubcategory("MemoryBlobProxy");
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
            if (ms.TryGetBuffer(out var buffer))
            {
                sw.Stop();
                var reusable = new MemoryBlob(buffer, consumableBlob.Attributes, sw.Elapsed);
                return reusable;
            } else
            {
                var byteArray = ms.ToArray();
                var arraySegment = new ArraySegment<byte>(byteArray);
                sw.Stop();
                var reusable = new MemoryBlob(arraySegment, consumableBlob.Attributes, sw.Elapsed);
                return reusable;
            }
        }
    }

    public void IndicateInterest()
    {
        Logger?.LogTrace("Blob {InstanceId} IndicateInterest(). Stack Trace: {StackTrace}", id, FormatStackStrace(new StackTrace(true)));
        _mustBuffer = true;
    }
}
