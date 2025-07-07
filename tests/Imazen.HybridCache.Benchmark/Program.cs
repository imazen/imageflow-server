using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Imazen.Common.Extensibility.StreamCache;
using Imazen.HybridCache.MetaStore;
using Imazen.Routing.Promises.Pipelines;
using Imazen.Routing.Requests;
using MELT;
using Microsoft.Extensions.Logging;
using Microsoft.IO;

namespace Imazen.HybridCache.Benchmark
{
    class Program
    {
        private const string UniqueName = "HybridCache";
        static async Task Main(string[] args)
        {
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("Canceling...");
                cts.Cancel();
                e.Cancel = true;
            };

            //await TestAsyncMediumLimitedCacheWavesMetaStore(cts.Token);
            await TestSyncVeryLimitedCacheWavesMetaStore(cts.Token);
            //await TestMassiveFileQuantityMetaStore(cts.Token);
            //await TestMassiveFileQuantity(cts.Token);
            //await TestSyncVeryLimitedCacheWaves(cts.Token);
            //await TestRandomAsyncCache(cts.Token);
            //await TestRandomAsyncVeryLimitedCache(cts.Token);

            //await TestRandomSynchronousVeryLimitedCache(cts.Token);
            //await TestRandomSynchronousLimitedCache(cts.Token);

            //await TestRandomSynchronousNoEviction(false, cts.Token);
            //await TestRandomSynchronousNoEviction(true, cts.Token);


        }
        private static HybridCacheAdvancedOptions CreateHybridCacheAdvancedOptions(
            [CallerMemberName]
            string uniqueName = "HybridCache", 
            int subfolders = 1, 
            long maxCacheBytes = 0, 
            bool moveFilesIntoPlace = false,
            TimeSpan minAgeToDelete = default,
            long minCleanupBytes = 0)
        {
            return new HybridCacheAdvancedOptions(uniqueName, "")
            {
                Subfolders = subfolders,
                AsyncCacheOptions = new AsyncCacheOptions()
                {
                    UniqueName = uniqueName,
                    MoveFilesIntoPlace = moveFilesIntoPlace
                },
                CleanupManagerOptions = new CleanupManagerOptions()
                {
                    MaxCacheBytes = maxCacheBytes,
                    MinAgeToDelete = minAgeToDelete,
                    MinCleanupBytes = minCleanupBytes,
                },
            };
        }

        private static async Task TestAsyncMediumLimitedCacheWavesMetaStore(CancellationToken cancellationToken)
        {
            var options = new TestParams()
            {
                CacheOptions = CreateHybridCacheAdvancedOptions(UniqueName, 2048, 409600000, moveFilesIntoPlace: false),
                MetaStoreOptions = new MetaStoreOptions("")
                {
                    Shards = 32
                },
                UseMetaStore = true,
                FileSize = 81920,
                FileCount = 10000,
                RequestCountPerWave = 1000,
                RequestWaves = 10,
                RebootCount = 12,
                RequestWavesIntermission = TimeSpan.FromMilliseconds(15),
                CreationTaskDelay = TimeSpan.FromMilliseconds(0),
                CreationThreadSleep = TimeSpan.FromMilliseconds(0),
                DisplayLog = false,
                Synchronous = false,
                MaxLogEntries = 75,
                WaitForKeypress = true,
            };
            Console.WriteLine("Starting HybridCache test with the async queue disabled and the cache limited to 1/5th the needed size");
            await TestRandom(options, cancellationToken);
        }
        
        
        private static async Task TestSyncVeryLimitedCacheWavesMetaStore(CancellationToken cancellationToken)
        {
            var options = new TestParams()
            {
                CacheOptions = CreateHybridCacheAdvancedOptions("HybridCache", 1, 8192000),
                MetaStoreOptions = new MetaStoreOptions("")
                {
                    Shards = 1
                },
                UseMetaStore = true,
                FileSize = 81920,
                FileCount = 4000,
                RequestCountPerWave = 400,
                RequestWaves = 30,
                RebootCount = 12,
                RequestWavesIntermission = TimeSpan.FromMilliseconds(15),
                CreationTaskDelay = TimeSpan.FromMilliseconds(0),
                CreationThreadSleep = TimeSpan.FromMilliseconds(0),
                DisplayLog = false,
                Synchronous = false,
                MaxLogEntries = 75,
                WaitForKeypress = true,
            };
            Console.WriteLine("Starting HybridCache test with the async queue disabled and the cache limited to 1/50th the needed size");
            await TestRandom(options, cancellationToken);
        }
        private static async Task TestMassiveFileQuantityMetaStore(CancellationToken cancellationToken)
        {
            var options = new TestParams()
            {
                FileSize = 0,
                FileCount = 6000000,
                CacheOptions = CreateHybridCacheAdvancedOptions(maxCacheBytes: (long)4096 * 2 * 1000 * 1000),
                RequestCountPerWave = 20000,
                RequestWaves = 1,
                UseMetaStore = true,
                RequestWavesIntermission = TimeSpan.Zero,
                RebootCount = 5,
                CreationTaskDelay = TimeSpan.FromMilliseconds(0),
                CreationThreadSleep = TimeSpan.FromMilliseconds(0),
                DisplayLog = false,
                WaitForKeypress = true
            };
            Console.WriteLine("Starting HybridCache test async disabled and 6,000,000 files in waves of 20,000 requests using MetaStore");
            await TestRandom(options, cancellationToken);
        }

        private static async Task TestMassiveFileQuantity(CancellationToken cancellationToken)
        {
            var options = new TestParams()
            {
                CacheOptions = CreateHybridCacheAdvancedOptions(maxCacheBytes: (long)4096 * 2 * 1000 * 1000),
                FileSize = 64,
                FileCount = 60000,
                RequestCountPerWave = 2000,
                RequestWaves = 5,
                RequestWavesIntermission = TimeSpan.Zero,
                CreationTaskDelay = TimeSpan.FromMilliseconds(0),
                CreationThreadSleep = TimeSpan.FromMilliseconds(0),
                DisplayLog = true
            };
            Console.WriteLine("Starting HybridCache test async disabled and 60,000 files in waves of 2000 requests");
            await TestRandom(options, cancellationToken);
        }
        
        private static async Task TestSyncVeryLimitedCacheWaves(CancellationToken cancellationToken)
        {
            var options = new TestParams()
            {
                CacheOptions = CreateHybridCacheAdvancedOptions(maxCacheBytes: 8192000),
                FileSize = 81920,
                FileCount = 500,
                RequestCountPerWave = 1000,
                RequestWaves = 5,
                RequestWavesIntermission = TimeSpan.Zero,
                CreationTaskDelay = TimeSpan.FromMilliseconds(100),
                CreationThreadSleep = TimeSpan.FromMilliseconds(200),
                DisplayLog = false
            };
            Console.WriteLine("Starting HybridCache test with the async queue disabled and the cache limited to 1/5th the needed size");
            await TestRandom(options, cancellationToken);
        }
        
        private static async Task TestRandomAsyncCache(CancellationToken cancellationToken)
        {
            var options = new TestParams()
            {
                CacheOptions = CreateHybridCacheAdvancedOptions(maxCacheBytes: 4088000),
                MaxQueuedBytes = 5 * 1000 * 1000,
                FileSize = 81920,
                FileCount = 100,
                RequestCountPerWave = 500,
                RequestWaves = 50,
                RequestWavesIntermission = TimeSpan.FromMilliseconds(200),
                CreationTaskDelay = TimeSpan.FromMilliseconds(100),
                CreationThreadSleep = TimeSpan.FromMilliseconds(200),
                DisplayLog = false
            };
            Console.WriteLine("Starting HybridCache test with the async queue enabled and the cache limited to 1/5th the needed size");
            await TestRandom(options, cancellationToken);
        }
        
        private static async Task TestRandomAsyncVeryLimitedCache(CancellationToken cancellationToken)
        {
            var options = new TestParams()
            {
                CacheOptions = CreateHybridCacheAdvancedOptions(maxCacheBytes: 8192000),
                MaxQueuedBytes = 100 * 1000 * 1000,
                FileSize = 81920,
                FileCount = 500,
                RequestCountPerWave = 2000,
                RequestWaves = 5,
                RequestWavesIntermission = TimeSpan.FromSeconds(1),
                CreationTaskDelay = TimeSpan.FromMilliseconds(1000),
                CreationThreadSleep = TimeSpan.FromMilliseconds(0),
                DisplayLog = false
            };
            Console.WriteLine("Starting HybridCache test with the async queue enabled and the cache limited to 1/5th the needed size");
            await TestRandom(options, cancellationToken);
        }
        private static async Task TestRandomSynchronousVeryLimitedCache(CancellationToken cancellationToken)
        {
            var options = new TestParams()
            {
                CacheOptions = CreateHybridCacheAdvancedOptions(maxCacheBytes: 8192000),
                FileSize = 81920,
                FileCount = 500,
                RequestCountPerWave = 2000,
                CreationTaskDelay = TimeSpan.FromMilliseconds(2000),
                CreationThreadSleep = TimeSpan.FromMilliseconds(500),
                DisplayLog = false
            };
            Console.WriteLine("Starting HybridCache test with the async queue disabled and the cache limited to 1/5th the needed size");
            await TestRandom(options, cancellationToken);
        }


        private static async Task TestRandomSynchronousLimitedCache(CancellationToken cancellationToken)
        {
            var options = new TestParams()
            {
                CacheOptions = CreateHybridCacheAdvancedOptions(maxCacheBytes: 20000000),
                FileCount = 500,
                FileSize = 81920,
                RequestCountPerWave = 1000,
                CreationTaskDelay = TimeSpan.FromMilliseconds(2000),
                CreationThreadSleep = TimeSpan.FromMilliseconds(500),
                DisplayLog = false
            };
            Console.WriteLine("Starting HybridCache test with the async queue disabled and the cache limited to half the needed size");
            await TestRandom(options, cancellationToken);
        }
        
        private static async Task TestRandomSynchronousNoEviction(bool getContentType, CancellationToken cancellationToken)
        {
            var options = new TestParams()
            {
                CacheOptions = CreateHybridCacheAdvancedOptions(maxCacheBytes: 100000000),
                
                RetrieveContentType = getContentType,
                FileCount = 500,
                FileSize = 81920,
                RequestCountPerWave = 20000,
                CreationTaskDelay = TimeSpan.FromMilliseconds(2000),
                CreationThreadSleep = TimeSpan.FromMilliseconds(500)
            };
            Console.WriteLine("Starting HybridCache test with the async queue disabled but no eviction required");
            await TestRandom(options, cancellationToken);
        }

        private class TestParams
        {
            internal int MaxQueuedBytes { get; set; } = 0;
            internal bool WriteSynchronouslyWhenQueueFull { get; set; } = true;
            internal bool FailRequestsOnEnqueueLockTimeout { get; set;} = true;

            internal int FileSize { get; set; } = 81920;
            internal int FileCount { get; set; } = 1000;
            internal int RequestCountPerWave { get; set; } = 10000;

            internal int RequestWaves { get; set; } = 1;
            
            internal TimeSpan RequestWavesIntermission { get; set; } = TimeSpan.FromSeconds(1);
            internal TimeSpan CreationTaskDelay { get; set; } = TimeSpan.FromSeconds(1);
            internal TimeSpan CreationThreadSleep { get; set; } = TimeSpan.FromSeconds(1);
            internal bool RetrieveContentType { get; set; }
            
            internal bool DisplayLog { get; set; }
            internal HybridCacheAdvancedOptions CacheOptions { get; set; } = new HybridCacheAdvancedOptions("","");

            internal bool UseMetaStore { get; set; }
            public int Seed { get; set; }
            public MetaStoreOptions MetaStoreOptions { get; set; } = new MetaStoreOptions("");
            public bool WaitForKeypress { get; set; }
            public int RebootCount { get; set; } = 1;
            public int MaxLogEntries { get; set; } = 50;
            public bool Synchronous { get; set; }
        }
        private static IReLogger CreateReLogger(ITestLoggerFactory loggerFactory)
        {
            
            return new ReLoggerFactory(loggerFactory, new ReLogStore(new ReLogStoreOptions())).CreateReLogger("HybridCache");
        }
        private static async Task TestRandom(TestParams options, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            
            
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}");
            Directory.CreateDirectory(path);
            Console.WriteLine($"Created cache directory {path}");
            try
            {
                options.CacheOptions.PhysicalCacheDir = path;

                for (var reboot = 0; reboot < options.RebootCount; reboot++)
                {
                    Console.WriteLine($"------------- Cache Reboot {reboot} ---------------");
                    var loggerFactory = TestLoggerFactory.Create();
                    var logger = CreateReLogger(loggerFactory);
                    ICacheDatabase<ICacheDatabaseRecord> database = new MetaStore.MetaStore(options.MetaStoreOptions, options.CacheOptions, logger);
                    HybridCache cache = new HybridCache(database, options.CacheOptions,logger);
                    try
                    {
                        Console.Write("Starting cache...");
                        var swStart = Stopwatch.StartNew();
                        await cache.StartAsync(cancellationToken);
                        swStart.Stop();
                        Console.Write($"ready in {swStart.Elapsed}\r\n");

                        await TestRandomInner(cache, options, logger, loggerFactory, cancellationToken);

                        if (options.DisplayLog)
                        {
                            var logs = loggerFactory.Sink.LogEntries.ToArray();
                            int firstLogIndex = logs.Length - Math.Min(options.MaxLogEntries, logs.Length);
                            int lastLogIndex = Math.Min(firstLogIndex, options.MaxLogEntries);
                            if (lastLogIndex > 0)
                                Console.WriteLine($"========== LOG ENTRIES 0..{lastLogIndex} ===============");
                            for (var ix = 0; ix < lastLogIndex; ix++)
                            {
                                Console.WriteLine(logs[ix].Message);
                            }

                            Console.WriteLine($"========== LOG ENTRIES {firstLogIndex}..{logs.Length} ===============");
                            for (var ix = firstLogIndex; ix < logs.Length; ix++)
                            {
                                Console.WriteLine(logs[ix].Message);
                            }

                            Console.WriteLine("============== END LOGS ===============");
                        }

                    }
                    finally
                    {
                        Console.Write("Stopping cache...");
                        var swStop = Stopwatch.StartNew();
                        await cache.StopAsync(cancellationToken);
                        swStop.Stop();
                        Console.Write($"stopped in {swStop.Elapsed}\r\n");
                    }
                }
                if (options.WaitForKeypress)
                {
                    Console.WriteLine("Press the any key to continue");
                    Console.ReadKey();
                }
            }
            finally
            {
                Console.WriteLine("Deleting cache from disk...");
                Directory.Delete(path, true);
                Console.WriteLine("Cache deleted");
            }
        }

        private static async Task TestRandomInner(HybridCache cache, TestParams options, IReLogger logger, ITestLoggerFactory loggerFactory, CancellationToken cancellationToken)
        {
            var data = new byte[options.FileSize];
            var blobAttrs = new BlobAttributes()
            {
                ContentType = "application/octet-stream",
                EstimatedBlobByteCount = options.FileSize
            };
            var blob = new MemoryBlob(data,blobAttrs, options.CreationTaskDelay,backingAllocationSize: options.FileSize);
            
            var latencyZone = new LatencyTrackingZone("TestBlobProvider", (int)(options.CreationTaskDelay + options.CreationThreadSleep).TotalMilliseconds, true);
            var blobWrapper = new BlobWrapper(latencyZone, blob);

            async ValueTask<CodeResult<IBlobWrapper>> BlobProvider(IRequestSnapshot snapshot, CancellationToken token)
            {
                if (options.CreationTaskDelay.Ticks > 0)
                {
                    await Task.Delay(options.CreationTaskDelay, cancellationToken);
                }

                if (options.CreationThreadSleep.Ticks > 0)
                {
                    Thread.Sleep(options.CreationThreadSleep);
                }

                return CodeResult<IBlobWrapper>.Ok(blobWrapper.ForkReference());
            }

            // TODO: TestSingleCacheSync is not correct at all 
            var harnessOptions = BlobCachingTestHarnessOptions.TestSingleCacheSync(cache, 
                BlobProvider, logger);
            
            var harness = new BlobCachingTestHarness(harnessOptions);
            
            
            await harness.StartAsync(cancellationToken);
            logger.LogInformation("Test harness started");
            
   
            var random = new Random(options.Seed);
            var tasks = new List<Task<Tuple<TimeSpan,HttpStatus>>>();
            
            var swTotal = Stopwatch.StartNew();
            for (var wave = 0; wave < options.RequestWaves; wave++)
            {
                if (wave != 0)
                {
                    Thread.Sleep(options.RequestWavesIntermission);
                }
                Console.Write("Wave {0}, {1} requests...", wave + 1, options.RequestCountPerWave);
                var sw = Stopwatch.StartNew();
                var memoryStreamManager =
                    new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options(){
                        BlockSize = Math.Max(2, options.FileSize),
                        LargeBufferMultiple = 2,
                        MaximumBufferSize =options.FileSize * 2 + 2

                    });
                for (var ix = 0; ix < options.RequestCountPerWave; ix++)
                {
                    Func<Task<Tuple<TimeSpan,HttpStatus>>> task = async () =>
                    {
                        var whichFile = random.Next(options.FileCount);
                        var key = BitConverter.GetBytes(whichFile);
                        var generatedFileName = "/" + Imazen.Common.Helpers.EncodingUtils.ToBase64U(key);
                        var itemSw = Stopwatch.StartNew();
                       
                        var cacheResult =  await harness.RequestBlobWrapper(generatedFileName, cancellationToken: cancellationToken);
                        if (cacheResult.TryUnwrapError(out var err))
                        {
                            itemSw.Stop();
                            logger.LogError("Error {0} fetching {1}", err, generatedFileName);
                            itemSw.Stop();
                            return new Tuple<TimeSpan, HttpStatus>(itemSw.Elapsed, err);
                        }
                        else
                        {
                            using var blobWrapper = cacheResult.Unwrap();
                            if (options.RetrieveContentType)
                            {
                                var _ = blobWrapper.Attributes.ContentType;
                            }
                            
                            using var ms = memoryStreamManager.GetStream();
                            using var consumable = await blobWrapper.GetConsumablePromise().IntoConsumableBlob();
                            using var stream = consumable.BorrowStream(DisposalPromise.CallerDisposesStreamThenBlob);
                            await stream.CopyToAsync(ms, 81920, cancellationToken);
                            itemSw.Stop();
                            return new Tuple<TimeSpan, HttpStatus>(itemSw.Elapsed, HttpStatus.Ok);

                        }
                        
                    };
                    if (options.Synchronous)
                    {
                        var result = await task();
                        tasks.Add(Task.FromResult(result));
                    }
                    else
                    {
                        tasks.Add(Task.Run(task,cancellationToken));
                    }
                }

                if (!options.Synchronous)
                {
                    await Task.WhenAll(tasks);
                }
                sw.Stop();
                var swAsync = Stopwatch.StartNew();
                await harness.AwaitEnqueuedTasks();
                swAsync.Stop();
                Console.WriteLine("completed in {0}, plus {1} for async tasks. ", sw.Elapsed, swAsync.Elapsed);
                PrintDiskUtilization(options);
            }

            swTotal.Stop();
            
            Console.WriteLine("Completed all waves in {0}", swTotal.Elapsed);
            Console.WriteLine();

            // Accumulate results
            var resultCounts = new Dictionary<HttpStatus, int>();
            var resultTimes = new Dictionary<HttpStatus, List<TimeSpan>>();
            foreach (var t in tasks)
            {
                var key = t.Result.Item2;
                if (resultCounts.TryGetValue(key, out var value))
                {
                    resultCounts[key] = value + 1;
                }
                else
                {
                    resultCounts[key] = 1;
                    resultTimes[key] = new List<TimeSpan>();
                }
                resultTimes[key].Add(t.Result.Item1);
            }

            foreach (var pair in resultCounts.OrderByDescending(p => p.Value))
            {
                Console.WriteLine("{0} - {1} occurrences - {2}kb - 1st percentile {3} 5th percentile = {4} 50th percentile = {5} 95th percentile = {6} 99th percentile = {7}",
                    pair.Key, pair.Value, 
                    pair.Value * options.FileSize / 1000,
                    GetPercentile(resultTimes[pair.Key], 0.01f),
                    GetPercentile(resultTimes[pair.Key], 0.05f),
                    GetPercentile(resultTimes[pair.Key], 0.5f),
                    GetPercentile(resultTimes[pair.Key], 0.95f),
                    GetPercentile(resultTimes[pair.Key], 0.99f));
            }
            
            var logCounts = new Dictionary<string, int>();
            var logEntryCount = 0;
            foreach (var e in loggerFactory.Sink.LogEntries)
            {
                logEntryCount++;
                var key = e.OriginalFormat;
                if (logCounts.TryGetValue(key, out var value))
                {
                    logCounts[key] = value + 1;
                }
                else
                {
                    logCounts[key] = 1;
                }
            }

            foreach (var pair in logCounts)
            {
                var percent = pair.Value * 100.0 / logEntryCount;
                Console.WriteLine("{0:00.00}% ({1} of {2}) log entries were {3}", percent, pair.Value, logEntryCount, pair.Key);
            }
            
        }


        private static TimeSpan GetPercentile(IEnumerable<TimeSpan> data, float percentile)
        {

            var longs = data.Select(ts => ts.Ticks).ToArray();
            Array.Sort(longs);
            var result = GetPercentileLongs(longs, percentile);
            return new TimeSpan(result);
        }

        private static long GetPercentileLongs(long[] data, float percentile)
        {
            if (data.Length == 0)
            {
                return 0;
            }
            var index = Math.Max(0, percentile * data.Length + 0.5f);

            return (data[(int)Math.Max(0, Math.Ceiling(index - 1.5))] +
                    data[(int)Math.Min(Math.Ceiling(index - 0.5), data.Length - 1)]) / 2;


        }

        private static void PrintDiskUtilization(TestParams options)
        {
            if (options.CacheOptions.PhysicalCacheDir == options.MetaStoreOptions.DatabaseDir)
            {
                PrintDiskUtilization("Cache", options.MetaStoreOptions.DatabaseDir,
                    options.CacheOptions.CleanupManagerOptions.MaxCacheBytes);
            }
            else
            {
                PrintDiskUtilization("Files", options.CacheOptions.PhysicalCacheDir,
                    options.CacheOptions.CleanupManagerOptions.MaxCacheBytes);
                PrintDiskUtilization("Database", options.MetaStoreOptions.DatabaseDir,
                    options.CacheOptions.CleanupManagerOptions.MaxCacheBytes);
            }
        }
        
        private static void PrintDiskUtilization(string name, string dir, long limit)
        {
            var cacheDirBytes = GetFolderBytes(dir);
            var percent = (double) cacheDirBytes.Item3 / limit * 100;
            Console.WriteLine("* {0} utilizing {1:0.00}% of limit ({2:0,0} of {3:0,0} bytes, {4} files and {5} dirs)",
                name, percent, cacheDirBytes.Item3, limit, cacheDirBytes.Item1, cacheDirBytes.Item2);
        }

        private static Tuple<int,int,long> GetFolderBytes(string folder)
        {
            long bytes = 0;
            if (!Directory.Exists(folder)) return new Tuple<int,int, long>(0,0,0);
            int fileCount = 0;
            int dirCount = 0;
            
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos( "*", SearchOption.AllDirectories))
            {
                if ((entry.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    bytes += 4096;
                    dirCount++;
                }
                else
                {
                    var fileInfo = new FileInfo(entry.FullName);
                    if (fileInfo.Exists)
                    {
                        bytes += EstimateEntryBytesWithOverhead(fileInfo.Length);
                        fileCount++;
                    }
                }
                
            }
            
            return new Tuple<int,int, long>(fileCount,dirCount, bytes);
        }
        
        private static long EstimateEntryBytesWithOverhead(long byteCount)
        {
            // Most file systems have a 4KiB block size
            var withBlockSize = (Math.Max(1, byteCount) + 4095) / 4096 * 4096;
            // Add 1KiB for the file descriptor and 1KiB for the directory descriptor, although it's probably larger
            var withFileDescriptor = withBlockSize + 1024 + 1024;

            return withFileDescriptor;
        }
    }
}