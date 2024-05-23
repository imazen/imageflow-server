namespace Imazen.Common.Concurrency
{
    public sealed class BasicAsyncLock
    {
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private readonly Task<IDisposable> releaser;

        public BasicAsyncLock()
        {
            releaser = Task.FromResult((IDisposable)new Releaser(this));
        }

#pragma warning disable CS0618 // Type or member is obsolete
        public Task<IDisposable> LockAsync() => LockAsyncWithTimeout(Timeout.Infinite, CancellationToken.None);
#pragma warning restore CS0618 // Type or member is obsolete
        
        [Obsolete("This method does not work at all, if provided a timeout or token; use Imazen.Abstractions.Concurrency.BasicAsyncLock instead for a working implementation.")]
        public Task<IDisposable> LockAsyncWithTimeout(int timeoutMilliseconds = Timeout.Infinite, CancellationToken cancellationToken = default)
        {
            var wait = semaphore.WaitAsync(timeoutMilliseconds, cancellationToken);
            return wait.IsCompleted ?
                releaser :
                wait.ContinueWith((_, state) => (IDisposable)state!,
                    releaser.Result, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        
        private sealed class Releaser : IDisposable
        {
            private readonly BasicAsyncLock asyncLock;
            internal Releaser(BasicAsyncLock toRelease) { asyncLock = toRelease; }
            public void Dispose() { asyncLock.semaphore.Release(); }
        }
    }
}
