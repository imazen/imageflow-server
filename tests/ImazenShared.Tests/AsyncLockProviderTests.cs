using Imazen.Common.Concurrency;
using Xunit;

namespace Imazen.Common.Tests
{
    public class AsyncLockProviderTests
    {
        /// <summary>
        /// Tests that locks are cleaned up promptly, even in the case of contention and exceptions
        /// </summary>
        /// <exception cref="Exception"></exception>
        [Fact]
        public async Task TestActiveLockCount()
        {
            var provider = new AsyncLockProvider();
            var task = provider.TryExecuteAsync("1", 1500,CancellationToken.None,  async () =>
            {
                await Task.Delay(50);
            });
            var task2 = provider.TryExecuteAsync("2", 1500, CancellationToken.None, async () =>
            {
                await Task.Delay(50);
            });
            var task2B = provider.TryExecuteAsync("2", 1500,CancellationToken.None,  async () =>
            {
                await Task.Delay(50);
            });
            var task3 = provider.TryExecuteAsync("3", 1500,CancellationToken.None,  async () =>
            {
                await Task.Delay(50);
                throw new Exception();
            });
            Assert.Equal(3, provider.GetActiveLockCount());
            await task;
            await task2;
            await task2B;
            await Assert.ThrowsAsync<Exception>(async () => await task3);
            Assert.Equal(0, provider.GetActiveLockCount());
        }

        /// <summary>
        /// Test that contending callbacks for the same key do not run concurrently
        /// </summary>
        [Fact]
        public void TestConcurrency()
        {
            var provider = new AsyncLockProvider();
            int sharedValue = 0;
            var tasks = Enumerable.Range(0, 10).Select(async unused =>
                Assert.True(await provider.TryExecuteAsync("1", 15000, CancellationToken.None, async () =>
                {
                    var oldValue = sharedValue;
                    sharedValue++;
                    await Task.Delay(5);
                    sharedValue = oldValue;
                }))).ToArray();
            Assert.True(provider.MayBeLocked("1"));
            Assert.Equal(1, provider.GetActiveLockCount());
            Task.WaitAll(tasks);
            Assert.Equal(0, sharedValue);
            Assert.Equal(0, provider.GetActiveLockCount());
            
        }
    }
}