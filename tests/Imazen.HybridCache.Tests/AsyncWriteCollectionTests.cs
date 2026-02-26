using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Imazen.HybridCache.Tests
{
    public class AsyncWriteCollectionTests
    {
        private static AsyncWrite CreateWrite(string key, int dataSize = 100)
        {
            var data = new byte[dataSize];
            return new AsyncWrite(key, new ArraySegment<byte>(data), "image/jpeg");
        }

        [Fact]
        public async Task Queue_BasicEnqueueAndComplete()
        {
            var collection = new AsyncWriteCollection(1024 * 1024);
            var writerCalled = false;

            var result = collection.Queue(CreateWrite("key1"), async w =>
            {
                writerCalled = true;
                await Task.CompletedTask;
            });

            Assert.Equal(AsyncWriteCollection.AsyncQueueResult.Enqueued, result);
            await collection.AwaitAllCurrentTasks();
            Assert.True(writerCalled);
        }

        [Fact]
        public async Task Queue_DuplicateKeyReturnsAlreadyPresent()
        {
            var collection = new AsyncWriteCollection(1024 * 1024);
            var gate = new TaskCompletionSource<bool>();

            // First write blocks until we release it
            collection.Queue(CreateWrite("key1"), async w => { await gate.Task; });

            // Second write with same key should be rejected
            var result = collection.Queue(CreateWrite("key1"), async w => { await Task.CompletedTask; });
            Assert.Equal(AsyncWriteCollection.AsyncQueueResult.AlreadyPresent, result);

            gate.SetResult(true);
            await collection.AwaitAllCurrentTasks();
        }

        [Fact]
        public async Task Queue_FullQueueReturnsQueueFull()
        {
            // Queue that can hold ~200 bytes (one entry is data.Length + 100 overhead)
            var collection = new AsyncWriteCollection(250);
            var gate = new TaskCompletionSource<bool>();

            // First write: 100 byte data + 100 overhead = 200 bytes, fits in 250
            var result1 = collection.Queue(CreateWrite("key1", 100), async w => { await gate.Task; });
            Assert.Equal(AsyncWriteCollection.AsyncQueueResult.Enqueued, result1);

            // Second write would push us over 250 bytes
            var result2 = collection.Queue(CreateWrite("key2", 100), async w => { await Task.CompletedTask; });
            Assert.Equal(AsyncWriteCollection.AsyncQueueResult.QueueFull, result2);

            gate.SetResult(true);
            await collection.AwaitAllCurrentTasks();
        }

        [Fact]
        public async Task Get_ReturnsEntryWhileInFlight()
        {
            var collection = new AsyncWriteCollection(1024 * 1024);
            var gate = new TaskCompletionSource<bool>();
            var write = CreateWrite("key1");

            collection.Queue(write, async w => { await gate.Task; });

            // Should be able to get it while in-flight
            var fetched = collection.Get("key1");
            Assert.NotNull(fetched);
            Assert.Equal("key1", fetched.Key);

            gate.SetResult(true);
            await collection.AwaitAllCurrentTasks();

            // After completion, should be removed
            Assert.Null(collection.Get("key1"));
        }

        /// <summary>
        /// Regression: writerDelegate that throws must not cause UnobservedTaskException.
        /// Before fix, the exception would propagate to the Task, but Remove() in the
        /// finally block would remove it from the collection before AwaitAllCurrentTasks
        /// could observe it.
        /// </summary>
        [Fact]
        public async Task Queue_WriterDelegateException_DoesNotCauseUnobservedTaskException()
        {
            var collection = new AsyncWriteCollection(1024 * 1024);
            var unobservedException = false;

            void Handler(object sender, UnobservedTaskExceptionEventArgs args)
            {
                unobservedException = true;
            }

            TaskScheduler.UnobservedTaskException += Handler;
            try
            {
                var result = collection.Queue(CreateWrite("key1"), w =>
                {
                    throw new InvalidOperationException("Simulated write failure");
                });
                Assert.Equal(AsyncWriteCollection.AsyncQueueResult.Enqueued, result);

                // Wait for the task to complete (it will fail internally)
                await Task.Delay(200);
                await collection.AwaitAllCurrentTasks();

                // Entry should be cleaned up despite the exception
                Assert.Null(collection.Get("key1"));

                // Force GC + finalizers to trigger UnobservedTaskException if it exists
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(100);

                Assert.False(unobservedException,
                    "UnobservedTaskException was raised — writerDelegate exception leaked to the Task");
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= Handler;
            }
        }

        /// <summary>
        /// Regression: after a throwing writerDelegate completes and is cleaned up,
        /// the same key should be re-enqueueable (bytes properly reclaimed).
        /// </summary>
        [Fact]
        public async Task Queue_AfterWriterException_KeyCanBeRequeued()
        {
            var collection = new AsyncWriteCollection(1024 * 1024);

            // First write throws
            collection.Queue(CreateWrite("key1"), w =>
            {
                throw new InvalidOperationException("Simulated failure");
            });

            await Task.Delay(200);
            await collection.AwaitAllCurrentTasks();

            // Key should be removed, allowing re-enqueue
            var secondCallCompleted = false;
            var result = collection.Queue(CreateWrite("key1"), async w =>
            {
                secondCallCompleted = true;
                await Task.CompletedTask;
            });
            Assert.Equal(AsyncWriteCollection.AsyncQueueResult.Enqueued, result);

            await collection.AwaitAllCurrentTasks();
            Assert.True(secondCallCompleted);
        }

        /// <summary>
        /// Regression: queuedBytes must be reclaimed after writerDelegate throws,
        /// so the queue doesn't report QueueFull when it's actually empty.
        /// </summary>
        [Fact]
        public async Task Queue_AfterWriterException_BytesAreReclaimed()
        {
            // Tiny queue: can hold exactly one 100-byte entry (100 data + 100 overhead = 200)
            var collection = new AsyncWriteCollection(250);

            // First write throws
            collection.Queue(CreateWrite("key1", 100), w =>
            {
                throw new InvalidOperationException("Simulated failure");
            });

            await Task.Delay(200);
            await collection.AwaitAllCurrentTasks();

            // Queue should have space again (bytes reclaimed by Remove)
            var result = collection.Queue(CreateWrite("key2", 100), async w =>
            {
                await Task.CompletedTask;
            });
            Assert.Equal(AsyncWriteCollection.AsyncQueueResult.Enqueued, result);

            await collection.AwaitAllCurrentTasks();
        }

        /// <summary>
        /// Regression: AwaitAllCurrentTasks must not throw if called when the collection
        /// is empty or all tasks have already completed and been removed.
        /// </summary>
        [Fact]
        public async Task AwaitAllCurrentTasks_EmptyCollection_Succeeds()
        {
            var collection = new AsyncWriteCollection(1024 * 1024);
            await collection.AwaitAllCurrentTasks(); // Should not throw
        }

        [Fact]
        public async Task AwaitAllCurrentTasks_AfterAllTasksComplete_Succeeds()
        {
            var collection = new AsyncWriteCollection(1024 * 1024);

            collection.Queue(CreateWrite("key1"), async w => { await Task.CompletedTask; });

            // Let it complete and remove itself
            await Task.Delay(200);

            // Should not throw even though collection is now empty
            await collection.AwaitAllCurrentTasks();
        }

        /// <summary>
        /// Concurrent enqueue of many unique keys should all succeed and
        /// all complete without queuedBytes drifting.
        /// </summary>
        [Fact]
        public async Task Queue_ConcurrentEnqueues_AllComplete()
        {
            var collection = new AsyncWriteCollection(100 * 1024 * 1024);
            var completedCount = 0;

            var tasks = new Task[100];
            for (int i = 0; i < 100; i++)
            {
                var key = $"key-{i}";
                tasks[i] = Task.Run(() =>
                {
                    var result = collection.Queue(CreateWrite(key, 50), async w =>
                    {
                        await Task.Delay(10);
                        Interlocked.Increment(ref completedCount);
                    });
                    Assert.Equal(AsyncWriteCollection.AsyncQueueResult.Enqueued, result);
                });
            }

            await Task.WhenAll(tasks);
            await collection.AwaitAllCurrentTasks();

            Assert.Equal(100, completedCount);

            // After all complete, queue should accept new work (bytes fully reclaimed)
            var finalResult = collection.Queue(CreateWrite("final", 50), async w =>
            {
                await Task.CompletedTask;
            });
            Assert.Equal(AsyncWriteCollection.AsyncQueueResult.Enqueued, finalResult);
            await collection.AwaitAllCurrentTasks();
        }
    }
}
