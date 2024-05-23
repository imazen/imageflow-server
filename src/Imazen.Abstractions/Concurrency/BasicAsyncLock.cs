using System.ComponentModel;
using Imazen.Abstractions.Logging;

namespace Imazen.Abstractions.Concurrency
{
    /// <summary>
    /// Not re-entrant
    /// </summary>
    public sealed class BasicAsyncLock
    {
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private readonly Task<IDisposable> releaser;
        private readonly ITestLogging? logger;

        public BasicAsyncLock()
        {
            releaser = Task.FromResult((IDisposable)new Releaser(this));
        }
        public BasicAsyncLock(ITestLogging logger)
        {
            this.logger = logger;
            releaser = Task.FromResult((IDisposable)new Releaser(this));
        }

        public Task<IDisposable> LockAsync() => LockAsyncWithTimeout(Timeout.Infinite, CancellationToken.None);
        public Task<IDisposable> LockAsyncWithTimeout(int timeoutMilliseconds = Timeout.Infinite, CancellationToken cancellationToken = default)
        {
            if (timeoutMilliseconds == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds),"Value cannot be 0; BasicAsyncLock does not allow quick-testing of the semaphore.");
            }
            var wait = semaphore.WaitAsync(timeoutMilliseconds, cancellationToken);
            
            // Check for early response
            if (wait.Status == TaskStatus.RanToCompletion)
            {
                if (!wait.Result)
                {
                    // Timed out
                    throw new TaskCanceledException("LockAsyncWithTimeout timed out waiting for semaphore");
                }
                return releaser;
            }
            
            
            return wait.ContinueWith((semaphoreEnteredTask, state) =>
            {
                if (semaphoreEnteredTask.Status == TaskStatus.RanToCompletion)
                {
                    if (semaphoreEnteredTask.Result)
                    {
                        return (IDisposable)state!;
                    }
                    throw new TaskCanceledException("LockAsyncWithTimeout timed out waiting for semaphore");
                }
                throw new InvalidOperationException();
            }, releaser.Result, 
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        }
        
        private sealed class Releaser : IDisposable
        {
            private readonly BasicAsyncLock asyncLock;
            internal Releaser(BasicAsyncLock toRelease) { asyncLock = toRelease; }

            public void Dispose()
            {
                asyncLock.semaphore.Release();
            }
        }
    }
}