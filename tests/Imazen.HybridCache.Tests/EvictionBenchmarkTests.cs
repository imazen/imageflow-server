using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Common.Extensibility.StreamCache;
using Imazen.HybridCache.MetaStore;
using Xunit;
using Xunit.Abstractions;

namespace Imazen.HybridCache.Tests
{
    /// <summary>
    /// Stress tests that thrash the cache under Zipfian access patterns to measure
    /// the impact of eviction sort order on hit/miss rates.
    ///
    /// Zipfian distribution models real-world web traffic: a small number of images
    /// are very popular while most are rarely accessed. Correct LFU eviction (evict
    /// least-used first) should maintain high hit rates for popular items even under
    /// heavy cache pressure.
    /// </summary>
    public class EvictionBenchmarkTests
    {
        private readonly ITestOutputHelper _output;

        public EvictionBenchmarkTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Generates Zipfian-distributed item indices. Item 0 is most popular,
        /// item N-1 is least popular. Probability of item k ~ 1/(k+1)^s
        /// </summary>
        private static int[] GenerateZipfianSequence(int numItems, int numRequests, double skew = 1.0, int seed = 42)
        {
            var rng = new Random(seed);
            var cdf = new double[numItems];

            // Build CDF: P(k) ~ 1/(k+1)^s
            double sum = 0;
            for (int k = 0; k < numItems; k++)
            {
                sum += 1.0 / Math.Pow(k + 1, skew);
                cdf[k] = sum;
            }
            // Normalize
            for (int k = 0; k < numItems; k++)
                cdf[k] /= sum;

            var sequence = new int[numRequests];
            for (int i = 0; i < numRequests; i++)
            {
                double r = rng.NextDouble();
                int idx = Array.BinarySearch(cdf, r);
                if (idx < 0) idx = ~idx;
                sequence[i] = Math.Min(idx, numItems - 1);
            }
            return sequence;
        }

        /// <summary>
        /// Runs a cache thrashing workload and returns hit rate statistics.
        /// </summary>
        private async Task<(int hits, int misses, int errors, double hitRate)> RunCacheThrashWorkload(
            string testDir,
            int numDistinctItems,
            int numRequests,
            long maxCacheBytes,
            int itemSizeBytes,
            int[] accessSequence)
        {
            var cancellationToken = CancellationToken.None;

            var cacheOptions = new HybridCacheOptions(testDir)
            {
                Subfolders = 32, // Fewer subfolders for small test
                AsyncCacheOptions = new AsyncCacheOptions()
                {
                    MaxQueuedBytes = maxCacheBytes, // Allow queue up to cache size
                    WriteSynchronouslyWhenQueueFull = true
                },
                CleanupManagerOptions = new CleanupManagerOptions()
                {
                    MaxCacheBytes = maxCacheBytes,
                    MinCleanupBytes = itemSizeBytes * 2, // Free at least 2 items per cleanup
                    MinAgeToDelete = TimeSpan.FromMilliseconds(1), // Allow immediate deletion
                    RetryDeletionAfter = TimeSpan.FromMilliseconds(1),
                    AccessTrackingBits = 16 // Smaller tracking table for test
                }
            };

            var metaStoreOptions = new MetaStoreOptions(testDir)
            {
                Shards = 2 // Fewer shards for small test
            };

            var database = new MetaStore.MetaStore(metaStoreOptions, cacheOptions, null);
            var cache = new HybridCache(database, cacheOptions, null);

            int hits = 0;
            int misses = 0;
            int errors = 0;

            try
            {
                await cache.StartAsync(cancellationToken);

                // Pre-generate unique keys for each item
                var itemKeys = new byte[numDistinctItems][];
                for (int i = 0; i < numDistinctItems; i++)
                {
                    itemKeys[i] = BitConverter.GetBytes(i)
                        .Concat(BitConverter.GetBytes(i * 31))
                        .Concat(BitConverter.GetBytes(i * 97))
                        .Concat(BitConverter.GetBytes(i * 127))
                        .ToArray(); // 16 bytes
                }

                var itemData = new byte[itemSizeBytes];
                new Random(123).NextBytes(itemData);

                for (int reqIdx = 0; reqIdx < numRequests; reqIdx++)
                {
                    var itemIndex = accessSequence[reqIdx];
                    var key = itemKeys[itemIndex];
                    var contentType = "image/jpeg";

                    Task<IStreamCacheInput> DataProvider(CancellationToken token)
                    {
                        return Task.FromResult(
                            new StreamCacheInput(contentType, new ArraySegment<byte>(itemData))
                                .ToIStreamCacheInput());
                    }

                    try
                    {
                        var result = await cache.GetOrCreateBytes(key, DataProvider, cancellationToken, false);

                        if (result.Status == "DiskHit")
                            hits++;
                        else
                            misses++; // WriteSucceeded, etc. = cache miss (had to generate)

                        await result.Data.DisposeAsync();
                    }
                    catch
                    {
                        errors++;
                    }

                    // Periodically let the async queue drain
                    if (reqIdx % 100 == 0)
                    {
                        await cache.AsyncCache.AwaitEnqueuedTasks();
                    }
                }

                // Final drain
                await cache.AsyncCache.AwaitEnqueuedTasks();
            }
            finally
            {
                try { await cache.StopAsync(cancellationToken); }
                catch { }
                try { Directory.Delete(testDir, true); }
                catch { }
            }

            double hitRate = (double)hits / (hits + misses);
            return (hits, misses, errors, hitRate);
        }

        /// <summary>
        /// Thrashes the cache with a Zipfian workload where the cache can only hold ~20% of
        /// distinct items. With correct LFU eviction (evict least-used), the popular items
        /// should stay cached and hit rate should be decent (~40-60%). With wrong eviction
        /// (evict most-used), hit rate would be terrible (~5-15%).
        /// </summary>
        [Fact]
        public async Task ZipfianWorkload_CorrectEviction_AchievesReasonableHitRate()
        {
            var testDir = Path.Combine(Path.GetTempPath(), $"hybridcache-eviction-{Guid.NewGuid()}");
            Directory.CreateDirectory(testDir);

            const int numDistinctItems = 500;
            const int numRequests = 3000;
            const int itemSizeBytes = 4096;
            // Cache holds ~100 items (20% of 500)
            // Using overhead estimate: each item ~8KB on disk with metadata
            const long maxCacheBytes = 100 * (itemSizeBytes + 6144);

            var accessSequence = GenerateZipfianSequence(numDistinctItems, numRequests, skew: 1.0);

            _output.WriteLine($"Config: {numDistinctItems} items, {numRequests} requests, cache fits ~100 items (20%)");
            _output.WriteLine($"Cache size: {maxCacheBytes / 1024}KB, item size: {itemSizeBytes}B");

            // Show distribution of accesses
            var itemCounts = new int[numDistinctItems];
            foreach (var idx in accessSequence) itemCounts[idx]++;
            var sorted = itemCounts.OrderByDescending(x => x).ToArray();
            _output.WriteLine($"Top 10 items get {sorted.Take(10).Sum()} of {numRequests} requests ({100.0 * sorted.Take(10).Sum() / numRequests:F1}%)");
            _output.WriteLine($"Top 100 items get {sorted.Take(100).Sum()} of {numRequests} requests ({100.0 * sorted.Take(100).Sum() / numRequests:F1}%)");

            var (hits, misses, errors, hitRate) = await RunCacheThrashWorkload(
                testDir, numDistinctItems, numRequests, maxCacheBytes, itemSizeBytes, accessSequence);

            _output.WriteLine($"Results: {hits} hits, {misses} misses, {errors} errors");
            _output.WriteLine($"Hit rate: {hitRate:P1}");

            // With correct eviction (least-used first), Zipfian workload should achieve
            // a meaningful hit rate. The exact threshold depends on many factors (timing,
            // MinAgeToDelete, etc.) but it should be well above random chance.
            // With wrong eviction, this would typically be <15%.
            Assert.True(hitRate > 0.20,
                $"Hit rate {hitRate:P1} is too low — eviction may be removing popular items. " +
                $"Expected >20% with Zipfian skew=1.0 and 20% cache capacity.");
            Assert.Equal(0, errors);
        }

        /// <summary>
        /// Uniform random access pattern — all items equally likely. This should have
        /// ~20% hit rate (matching cache capacity ratio) regardless of eviction order,
        /// serving as a baseline comparison.
        /// </summary>
        [Fact]
        public async Task UniformWorkload_BaselineHitRate()
        {
            var testDir = Path.Combine(Path.GetTempPath(), $"hybridcache-uniform-{Guid.NewGuid()}");
            Directory.CreateDirectory(testDir);

            const int numDistinctItems = 500;
            const int numRequests = 3000;
            const int itemSizeBytes = 4096;
            const long maxCacheBytes = 100 * (itemSizeBytes + 6144);

            // Uniform random access — all items equally likely
            var rng = new Random(42);
            var accessSequence = Enumerable.Range(0, numRequests)
                .Select(_ => rng.Next(numDistinctItems))
                .ToArray();

            _output.WriteLine($"Config: {numDistinctItems} items, {numRequests} requests, cache fits ~100 items (20%)");
            _output.WriteLine($"Uniform random access pattern (baseline)");

            var (hits, misses, errors, hitRate) = await RunCacheThrashWorkload(
                testDir, numDistinctItems, numRequests, maxCacheBytes, itemSizeBytes, accessSequence);

            _output.WriteLine($"Results: {hits} hits, {misses} misses, {errors} errors");
            _output.WriteLine($"Hit rate: {hitRate:P1}");

            // Uniform access should still achieve some hits from temporal locality
            // (recently added items get hit before eviction). Not a hard assertion,
            // just logging for comparison.
            Assert.Equal(0, errors);
        }

        /// <summary>
        /// High-concurrency thrashing — multiple concurrent tasks hitting the cache
        /// simultaneously with Zipfian distribution.
        /// </summary>
        [Fact]
        public async Task ConcurrentZipfianWorkload_StressTest()
        {
            var testDir = Path.Combine(Path.GetTempPath(), $"hybridcache-concurrent-{Guid.NewGuid()}");
            Directory.CreateDirectory(testDir);

            const int numDistinctItems = 200;
            const int requestsPerWorker = 500;
            const int numWorkers = 8;
            const int itemSizeBytes = 2048;
            // Cache holds ~40 items (20% of 200)
            const long maxCacheBytes = 40 * (itemSizeBytes + 6144);

            var cancellationToken = CancellationToken.None;

            var cacheOptions = new HybridCacheOptions(testDir)
            {
                Subfolders = 16,
                AsyncCacheOptions = new AsyncCacheOptions()
                {
                    MaxQueuedBytes = maxCacheBytes * 2,
                    WriteSynchronouslyWhenQueueFull = true
                }
            };

            var metaStoreOptions = new MetaStoreOptions(testDir) { Shards = 2 };
            var cleanupOptions = new CleanupManagerOptions()
            {
                MaxCacheBytes = maxCacheBytes,
                MinCleanupBytes = itemSizeBytes * 2,
                MinAgeToDelete = TimeSpan.FromMilliseconds(1),
                RetryDeletionAfter = TimeSpan.FromMilliseconds(1),
                AccessTrackingBits = 16
            };

            var database = new MetaStore.MetaStore(metaStoreOptions, cacheOptions, null);
            var cache = new HybridCache(database, cacheOptions, null);

            int totalHits = 0;
            int totalMisses = 0;
            int totalErrors = 0;

            try
            {
                await cache.StartAsync(cancellationToken);

                // Pre-generate keys
                var itemKeys = new byte[numDistinctItems][];
                for (int i = 0; i < numDistinctItems; i++)
                {
                    itemKeys[i] = BitConverter.GetBytes(i)
                        .Concat(BitConverter.GetBytes(i * 31))
                        .Concat(BitConverter.GetBytes(i * 97))
                        .Concat(BitConverter.GetBytes(i * 127))
                        .ToArray();
                }

                var itemData = new byte[itemSizeBytes];
                new Random(123).NextBytes(itemData);

                // Each worker gets its own Zipfian sequence (different seed)
                var workerTasks = Enumerable.Range(0, numWorkers).Select(workerIdx =>
                    Task.Run(async () =>
                    {
                        var sequence = GenerateZipfianSequence(numDistinctItems, requestsPerWorker,
                            skew: 1.0, seed: 42 + workerIdx);
                        int hits = 0, misses = 0, errors = 0;

                        for (int i = 0; i < requestsPerWorker; i++)
                        {
                            var key = itemKeys[sequence[i]];

                            Task<IStreamCacheInput> DataProvider(CancellationToken token)
                            {
                                return Task.FromResult(
                                    new StreamCacheInput("image/jpeg", new ArraySegment<byte>(itemData))
                                        .ToIStreamCacheInput());
                            }

                            try
                            {
                                var result = await cache.GetOrCreateBytes(key, DataProvider, cancellationToken, false);
                                if (result.Status == "DiskHit")
                                    hits++;
                                else
                                    misses++;
                                await result.Data.DisposeAsync();
                            }
                            catch
                            {
                                errors++;
                            }
                        }

                        Interlocked.Add(ref totalHits, hits);
                        Interlocked.Add(ref totalMisses, misses);
                        Interlocked.Add(ref totalErrors, errors);
                    })).ToArray();

                await Task.WhenAll(workerTasks);
                await cache.AsyncCache.AwaitEnqueuedTasks();
            }
            finally
            {
                try { await cache.StopAsync(cancellationToken); }
                catch { }
                try { Directory.Delete(testDir, true); }
                catch { }
            }

            int totalRequests = totalHits + totalMisses;
            double hitRate = totalRequests > 0 ? (double)totalHits / totalRequests : 0;

            _output.WriteLine($"Concurrent: {numWorkers} workers x {requestsPerWorker} requests = {totalRequests} total");
            _output.WriteLine($"Results: {totalHits} hits, {totalMisses} misses, {totalErrors} errors");
            _output.WriteLine($"Hit rate: {hitRate:P1}");

            // The test passes if no exceptions/crashes — concurrent cache thrashing
            // should not corrupt state or deadlock
            Assert.Equal(0, totalErrors);
        }

        /// <summary>
        /// Pure in-memory simulation comparing LFU (evict least-used, correct) vs
        /// anti-LFU (evict most-used, the old bug) under Zipfian workload.
        /// No filesystem needed — this isolates the eviction sort impact cleanly.
        /// </summary>
        [Fact]
        public void EvictionSimulation_CorrectVsReversedSort_HitRateDelta()
        {
            const int numItems = 1000;
            const int cacheCapacity = 100; // 10% of items
            const int numRequests = 50_000;
            const double skew = 1.0;

            var sequence = GenerateZipfianSequence(numItems, numRequests, skew);

            // Show distribution
            var itemCounts = new int[numItems];
            foreach (var idx in sequence) itemCounts[idx]++;
            var top100Sum = itemCounts.OrderByDescending(x => x).Take(cacheCapacity).Sum();
            _output.WriteLine($"Zipfian(s={skew}): top {cacheCapacity} items get {top100Sum} of {numRequests} requests ({100.0 * top100Sum / numRequests:F1}%)");
            _output.WriteLine($"Theoretical optimal hit rate if top {cacheCapacity} always cached: {100.0 * top100Sum / numRequests:F1}%");
            _output.WriteLine("");

            // --- Simulate LFU (correct: evict least-used) ---
            var lfuResult = SimulateEviction(sequence, cacheCapacity, evictMostUsed: false);
            _output.WriteLine($"CORRECT (evict least-used): {lfuResult.hits} hits / {numRequests} = {lfuResult.hitRate:P1}");
            _output.WriteLine($"  Evictions: {lfuResult.evictions}");

            // --- Simulate anti-LFU (BUG: evict most-used) ---
            var antiResult = SimulateEviction(sequence, cacheCapacity, evictMostUsed: true);
            _output.WriteLine($"BUGGED  (evict most-used):  {antiResult.hits} hits / {numRequests} = {antiResult.hitRate:P1}");
            _output.WriteLine($"  Evictions: {antiResult.evictions}");

            _output.WriteLine("");
            _output.WriteLine($"Hit rate delta: {(lfuResult.hitRate - antiResult.hitRate):P1} " +
                $"({lfuResult.hitRate / antiResult.hitRate:F1}x improvement)");

            // Correct eviction should significantly outperform reversed eviction
            Assert.True(lfuResult.hitRate > antiResult.hitRate * 1.5,
                $"LFU hit rate ({lfuResult.hitRate:P1}) should be at least 1.5x anti-LFU ({antiResult.hitRate:P1})");

            // Correct eviction should be close to the theoretical optimal
            double theoreticalOptimal = (double)top100Sum / numRequests;
            Assert.True(lfuResult.hitRate > theoreticalOptimal * 0.7,
                $"LFU hit rate ({lfuResult.hitRate:P1}) should be within 70% of theoretical optimal ({theoreticalOptimal:P1})");
        }

        /// <summary>
        /// Simulates a cache with simple eviction policy.
        /// Returns hit/miss/eviction statistics.
        /// </summary>
        private static (int hits, int misses, int evictions, double hitRate) SimulateEviction(
            int[] accessSequence, int capacity, bool evictMostUsed)
        {
            // Cache: set of items currently in cache
            var cache = new HashSet<int>();
            // Usage counters (like BucketCounter)
            var usageCounts = new Dictionary<int, int>();
            int hits = 0, misses = 0, evictions = 0;

            foreach (var item in accessSequence)
            {
                // Track usage (always, like BucketCounter.Increment)
                usageCounts.TryGetValue(item, out var count);
                usageCounts[item] = Math.Min(count + 1, ushort.MaxValue);

                if (cache.Contains(item))
                {
                    hits++;
                }
                else
                {
                    misses++;

                    // Need to add to cache — evict if full
                    if (cache.Count >= capacity)
                    {
                        // This mirrors GetDeletionCandidates + EvictSpace:
                        // The old bug used OrderByDescending (evict most-used)
                        // The fix uses OrderBy (evict least-used)
                        int victim;
                        if (evictMostUsed)
                        {
                            victim = cache.OrderByDescending(c => usageCounts.GetValueOrDefault(c, 0)).First();
                        }
                        else
                        {
                            victim = cache.OrderBy(c => usageCounts.GetValueOrDefault(c, 0)).First();
                        }
                        cache.Remove(victim);
                        evictions++;
                    }
                    cache.Add(item);
                }
            }

            double hitRate = (double)hits / accessSequence.Length;
            return (hits, misses, evictions, hitRate);
        }
    }
}
