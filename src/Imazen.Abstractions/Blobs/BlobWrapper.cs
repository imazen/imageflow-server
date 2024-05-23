using Microsoft.Extensions.Logging;

namespace Imazen.Abstractions.Blobs
{
    /// <summary>
    /// A reference to a blob (either consumable or reusable). 
    /// </summary>
    public class BlobWrapper : IBlobWrapper
    {
        public IBlobAttributes Attributes => core?.Attributes ?? throw new ObjectDisposedException("The BlobWrapper has been disposed");
        public long? EstimateAllocatedBytes => core?.EstimateAllocatedBytes;
        
     
        public bool IsReusable => core?.IsReusable ?? throw new ObjectDisposedException("The BlobWrapper has been disposed");

        public ValueTask EnsureReusable(CancellationToken cancellationToken = default)
        {
            return core?.EnsureReusable(cancellationToken) ?? throw new ObjectDisposedException("The BlobWrapper has been disposed");
        }

        public void IndicateInterest()
        {
            core?.IndicateInterest();
        }

        public IConsumableBlobPromise GetConsumablePromise()
        {
            return core?.GetConsumablePromise(this) ?? throw new ObjectDisposedException("The BlobWrapper has been disposed");
        }

        public IConsumableMemoryBlobPromise GetConsumableMemoryPromise()
        {
            return core?.GetConsumableMemoryPromise(this) ?? throw new ObjectDisposedException("The BlobWrapper has been disposed");
        }

        public IBlobWrapper ForkReference()
        {
            if (core == null) throw new ObjectDisposedException("The BlobWrapper has been disposed");
            return new BlobWrapper(core);
        }


        private BlobWrapperCore? core;
        public BlobWrapper(LatencyTrackingZone? latencyZone, StreamBlob consumable)
        {
            core = new BlobWrapperCore(latencyZone, consumable);
            core.AddWeakReference(this);
        }
        public BlobWrapper(LatencyTrackingZone? latencyZone, MemoryBlob reusable)
        {
            core = new BlobWrapperCore(latencyZone, reusable);
            core.AddWeakReference(this);
        }
        private BlobWrapper(BlobWrapperCore core)
        {
            this.core = core;
            core.AddWeakReference(this);
        }
        
        // maybe internal LatencyTrackingZone? LatencyZone => core?.LatencyZone;
        
        // /// <summary>
        // /// Sets the blob factory to be used to create a reusable blob from a consumable blob.
        // /// </summary>
        // /// <param name="borrowedFactory"></param>
        // /// <returns>False if the factory is already set, or the blob is already reusable</returns>
        // bool TrySetReusableBlobFactory(IReusableBlobFactory borrowedFactory);
        //
        /// <summary>
        /// Sets the logger to be used by the blob.
        /// </summary>
        /// <param name="logger"></param>
        /// <returns>False if the logger is already set</returns>
        bool TrySetLogger(ILogger logger)
        {
            if (core == null) throw new ObjectDisposedException("The BlobWrapper has been disposed");
            return core.TrySetLogger(logger);
        }


        public void Dispose()
        {
            core?.RemoveReference(this);
            core = null;
        }
    }




}
        