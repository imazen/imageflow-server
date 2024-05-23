using Imazen.Abstractions.Concurrency;
using Imazen.Abstractions.Logging;
using Imazen.Routing.Caching.Health.Tests;
using Xunit.Abstractions;

namespace Imazen.Tests.Abstractions.Concurrency;

using System.Threading.Tasks;
using Xunit;
using Imazen.Abstractions.Concurrency;
using Imazen.Abstractions.Logging;


public class BasicAsyncLockTests
{
    private TestLoggerAdapter logger;

    public BasicAsyncLockTests(ITestOutputHelper output)
    {
        logger = new TestLoggerAdapter(output);
    }

    [Fact]
    public async Task Lock_CanBeLocked()
    {

        var asyncLock = new BasicAsyncLock(logger);

        using (await asyncLock.LockAsync())
        {
        }
    }

    [Fact]
    public async Task Lock_TimesOut()
    {

        var asyncLock = new BasicAsyncLock(logger);
        using (await asyncLock.LockAsync())
        {

            await Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await asyncLock.LockAsyncWithTimeout(timeoutMilliseconds: 100));
        }
    }

    [Fact]
    public async Task LockAsyncWithTimeout_Timeout_DoesNotOverflowSemaphore()
    {
        var asyncLock = new BasicAsyncLock();

        // Acquire the lock
        using (await asyncLock.LockAsync())
        {
            // Attempt to acquire the lock with a timeout
            var lockTask = asyncLock.LockAsyncWithTimeout(100);

            // Wait for the timeout
            await Task.Delay(200);

            // The lock should not be acquired due to the timeout
            Assert.Equal(TaskStatus.Faulted, lockTask.Status);
        }

        // After releasing the lock, acquiring it should succeed
        using (await asyncLock.LockAsync())
        {
            // Lock acquired successfully
        }
    }

    [Fact]
public async Task Lock_IsExclusive()
{
    var asyncLock = new BasicAsyncLock(logger);
    var lockAcquired = false;

    using (await asyncLock.LockAsync())
    {
        // Start a separate task that tries to acquire the lock
        var lockTask = Task.Run(async () =>
        {
            using (await asyncLock.LockAsync())
            {
                lockAcquired = true;
            }
        });

        // Wait a short time to give the other task a chance to run
        await Task.Delay(100);

        // The other task should not have acquired the lock yet
        Assert.False(lockAcquired);
    }

    // After releasing the lock, the other task should eventually acquire it
    await Task.Delay(100);
    Assert.True(lockAcquired);
}

    [Fact]
    public async Task LockAsyncWithTimeout_Cancellation_DoesNotOverflowSemaphore()
    {
        var asyncLock = new BasicAsyncLock();

        // Acquire the lock
        using (await asyncLock.LockAsync())
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(100);

            // Attempt to acquire the lock with cancellation
            var lockTask = asyncLock.LockAsyncWithTimeout(Timeout.Infinite, cts.Token);

            // Wait for the cancellation
            await Task.Delay(200);

            // The lock should not be acquired due to cancellation
            Assert.Equal(TaskStatus.Canceled, lockTask.Status);
        }

        // After releasing the lock, acquiring it should succeed
        using (await asyncLock.LockAsync())
        {
            // Lock acquired successfully
        }
    }

  
    [Fact]
    public async Task WaitAsync_TimesOut_ReturnsFalse()
    {
        // Arrange
        var semaphore = new SemaphoreSlim(0, 1);

        // Act
        var entered = await semaphore.WaitAsync(TimeSpan.FromMilliseconds(50));

        // Assert
        Assert.False(entered);
    }
    
    [Fact]
    public async Task LockAsyncWithTimeout_TimeoutAndCancellation_DoesNotOverflowSemaphore()
    {
        var asyncLock = new BasicAsyncLock();

        // Acquire the lock
        using (await asyncLock.LockAsync())
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(100);

            // Attempt to acquire the lock with timeout and cancellation
            var lockTask = asyncLock.LockAsyncWithTimeout(200, cts.Token);

            // Wait for the cancellation and timeout
            await Task.Delay(300);

            // The lock should not be acquired due to cancellation or timeout
            Assert.Equal(TaskStatus.Canceled, lockTask.Status);
        }

        // After releasing the lock, acquiring it should succeed
        using (await asyncLock.LockAsync())
        {
            // Lock acquired successfully
        }
    }
    
    
}
    
