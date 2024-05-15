using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Instrumentation.Support;

namespace Imazen.Routing.Caching
{
    /// <summary>
    /// Uses a (2MB) ConcurrentBitArray of size 2^24 to track whether a key is likely to exist in the cache.
    /// Syncs via blob serialization. Bits representing buckets can only be set to 1, never 0. Works for any string key using SHA256 subsequence. Could potentially be extended to have multiple layers in the future if 2MB takes up too much bandwidth
    /// </summary>
    internal class ExistenceProbableMap
    {
        private readonly ConcurrentBitArray buckets;

        private const int hashBits = 24;

        private const int bucketCount = 1 << hashBits;
        public ExistenceProbableMap(){
            buckets = new ConcurrentBitArray(bucketCount);
        }
        
        private static byte[] HashKeyBasisStatic(byte[] keyBasis)
        {
            using (var h = SHA256.Create())
            {
                return h.ComputeHash(keyBasis);
            }
        }

        private static string BlobName => $"existence-probable-map-{hashBits}-bit.bin";
        private static Lazy<byte[]> BlobNameHash => new Lazy<byte[]>(() => HashKeyBasisStatic(Encoding.UTF8.GetBytes(BlobName)));
        
        private static Lazy<IBlobCacheRequest> BlobCacheRequest => new Lazy<IBlobCacheRequest>(()
            => new BlobCacheRequest(BlobGroup.Essential, BlobNameHash.Value, Convert.ToBase64String(BlobNameHash.Value), false));


               //ReadAllBytesAsync ArrayPool

        internal static async Task ReadAllBytesIntoBitArrayAsync(Stream stream, ConcurrentBitArray target, ArrayPool<byte> pool, CancellationToken cancellationToken = default)
        {   
            if (stream == null) return;
            byte[]? buffer = null;
            try
            {

                if (stream.CanSeek)
                {
                    if (stream.Length != target.ByteCount)
                    {
                        throw new Exception($"ExistenceProbableMap.ReadAllBytesIntoBitArrayAsync: Expected {target.ByteCount} bytes in stream, got {stream.Length}");
                    }
                }
                
                buffer = pool.Rent(target.ByteCount);
                //var bytesRead = await stream.ReadAsync(bufferOwner.Memory, cancellationToken).ConfigureAwait(false);
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    //pool.Return(buffer); //bufferOwner.Dispose();
                    throw new IOException("ExistenceProbableMap.ReadAllBytesIntoBitArrayAsync: Stream was empty.");
                }
                if (bytesRead != target.ByteCount)
                {
                    //pool.Return(buffer);
                    throw new IOException($"ExistenceProbableMap.ReadAllBytesIntoBitArrayAsync: Expected {target.ByteCount} bytes, got {bytesRead}");
                }
                // ensure no more bytes can be read
                if (stream.ReadByte() != -1)
                {
                    //pool.Return(buffer);
                    throw new IOException("ExistenceProbableMap.ReadAllBytesIntoBitArrayAsync: Stream was longer than {target.ByteCount} bytes.");
                }
                // copy to target
                target.LoadFromSpan(buffer);
                
            }
            finally
            {
                if (buffer != null) pool.Return(buffer);
                stream.Dispose();
            }
        }

        internal static async Task<ConcurrentBitArray?> FetchSync(IBlobCache cache, ConcurrentBitArray? target = null,
            CancellationToken cancellationToken = default)
        {

            var fetchResult = await cache.CacheFetch(BlobCacheRequest.Value, cancellationToken);
            if (fetchResult.IsError)
            {
                throw new Exception(
                    $"ExistenceProbableMap.FetchSync: fetchResult.IsError was true: {fetchResult.Error}");
            }

            using var wrapper = fetchResult.Unwrap();
            using var blob = await wrapper.GetConsumablePromise().IntoConsumableBlob();
            using var stream = blob.BorrowStream(DisposalPromise.CallerDisposesStreamThenBlob);
            target ??= new ConcurrentBitArray(bucketCount);
            await ReadAllBytesIntoBitArrayAsync(stream, target, ArrayPool<byte>.Shared,
                cancellationToken);
            return target;
        }


        public async Task Sync(IBlobCache cache, IReusableBlobFactory blobFactory, CancellationToken cancellationToken = default){
            
            var sw = Stopwatch.StartNew();
            // Fetch
            var existingData = await FetchSync(cache, cancellationToken: cancellationToken);
            if (existingData != null){
                //Merge
                buckets.MergeTrueBitsFrom(existingData);
            }
            //Put
            var bytes = buckets.ToBytes();
            sw.Stop();
            var putRequest = CacheEventDetails.CreateFreshResultGeneratedEvent(BlobCacheRequest.Value,
                blobFactory, Result<IBlobWrapper, IBlobCacheFetchFailure>.Ok(new BlobWrapper(null,
                    new MemoryBlob(new ArraySegment<byte>(bytes),  new BlobAttributes()
                    {
                        ContentType = "application/octet-stream"
                    },sw.Elapsed))));

            var putResponse = await cache.CachePut(putRequest, cancellationToken);
            if (putResponse.IsError)
            {
                throw new Exception(
                    $"ExistenceProbableMap.Sync: putResponse.IsError was true: {putResponse.Error}");
            }
        }


        internal static int HashFor(byte[] data)
        {
            // sha256 the data
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(data);
            var getBits = System.BitConverter.ToUInt32(hash, 0);
            return (int)(getBits >> (32 - hashBits));
        }

        internal static int HashFor(string s) => HashFor(System.Text.Encoding.UTF8.GetBytes(s));

        public bool MayExist(string key){
            var hash = HashFor(key);
            return buckets[hash];
        }
        public void MarkExists(string key){
            var hash = HashFor(key);
            buckets[hash] = true;
        }

        public void MarkExists(byte[] data){
            var hash = HashFor(data);
            buckets[hash] = true;
        }
        public bool MayExist(byte[] data){
            var hash = HashFor(data);
            return buckets[hash];
        }


    }
}