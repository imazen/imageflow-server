using System.Diagnostics;
using Imazen.Abstractions.Concurrency;
using Imazen.Abstractions.Logging;

public class NonOverlappingCachedRunner<T> : INonOverlappingRunner<T>
{
    private readonly Func<CancellationToken, Task<T>> _taskFactory;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan defaultReuseResultWithin;
    private readonly ITestLogging? _logger;
    private readonly BasicAsyncLock _lock = new();

    private long _lastCompletionTimeTicks;
    private T? _lastResult;
    private CancellationTokenSource? _cts;
    private bool allowStartingTasks = true;
    private bool disposeCalled = false;

    public NonOverlappingCachedRunner(Func<CancellationToken, Task<T>> taskFactory, TimeSpan timeout, TimeSpan defaultReuseResultWithin, ITestLogging? logger = default)
    {
        _taskFactory = taskFactory;
        _timeout = timeout;
        this.defaultReuseResultWithin = defaultReuseResultWithin;
        _logger = logger;
    }

    private async Task<T> RunUnderlyingTaskAsync(TimeSpan reuseResultWithin, TimeSpan callerTimeout = default, CancellationToken callerCancellationToken = default)
    {
        if (callerTimeout == Timeout.InfiniteTimeSpan || callerTimeout == TimeSpan.Zero || callerTimeout == default)
        {
            callerTimeout = Timeout.InfiniteTimeSpan;
        }
        using (await _lock.LockAsyncWithTimeout((int)callerTimeout.TotalMilliseconds, callerCancellationToken))
        {
            var reuseTicks = reuseResultWithin.Ticks;
            var agoTicks = Stopwatch.GetTimestamp() - _lastCompletionTimeTicks;
            if (agoTicks <= reuseTicks && _lastResult is not null)
            {

                _logger?.WriteLine($"Reusing previous result from {TimeSpan.FromTicks(agoTicks)} ago: {_lastResult}");
                return _lastResult;
            }
            if (!allowStartingTasks)
            {
                throw new OperationCanceledException("This NonOverlappingCachedRunner has been stopped or disposed and cannot start new tasks");
            }

            
            if (_timeout == Timeout.InfiniteTimeSpan || _timeout == TimeSpan.Zero || _timeout == default)
            {
                _cts = new CancellationTokenSource();
            }
            else
            {
                _cts = new CancellationTokenSource(_timeout);
            }
            try
            {
                _logger?.WriteLine("Starting task");
                var result = await _taskFactory(_cts.Token);
                _logger?.WriteLine("Task completed successfully");
                _lastResult = result;
                
                _lastCompletionTimeTicks = Stopwatch.GetTimestamp();
                return result;
            }
            catch(OperationCanceledException ex) when (ex.CancellationToken == _cts.Token)
            {
                _logger?.WriteLine("Task was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger?.WriteLine($"Task failed: {ex}");
                throw;
            }
            finally
            {
                _cts?.Dispose();
                _logger?.WriteLine("Disposed of CancellationTokenSource");
                _cts = null;
            }
        }
    }

    public async ValueTask<T> RunAsyncReuse(TimeSpan reuseResultWithin, TimeSpan callerTimeout = default,
        CancellationToken callerCancellationToken = default)
    {
        if (!allowStartingTasks)
        {
            if (disposeCalled)
            {
                throw new ObjectDisposedException("This NonOverlappingCachedRunner has been disposed and cannot start new tasks");
            }
            throw new InvalidOperationException("This NonOverlappingCachedRunner has been stopped nd cannot start new tasks");
        }
        if (callerTimeout == Timeout.InfiniteTimeSpan || callerTimeout == TimeSpan.Zero || callerTimeout == default)
        {
            if (callerCancellationToken == default)
            {
                return await RunUnderlyingTaskAsync(reuseResultWithin,default, default);
            }
            return await RunUnderlyingTaskAsync(reuseResultWithin,default, callerCancellationToken).WithCancellation(callerCancellationToken);
        }
        return await RunUnderlyingTaskAsync(reuseResultWithin, callerTimeout, callerCancellationToken).WithCancellationAndTimeout(callerCancellationToken, callerTimeout);
    }
    
    public async ValueTask<T> RunAsync(TimeSpan callerTimeout = default, CancellationToken callerCancellationToken = default)
    {
        return await RunAsyncReuse(defaultReuseResultWithin, callerTimeout, callerCancellationToken);
    }

    public T? FireAndForget()
    {
        _ = RunUnderlyingTaskAsync(defaultReuseResultWithin);
        return default;
    }
    
    public T? FireAndForget(TimeSpan reuseResultWithin)
    {
        _ = RunUnderlyingTaskAsync(reuseResultWithin);
        return default;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        allowStartingTasks = false;
#if NETSTANDARD2_1
        _logger?.WriteLine("cts.CancelAsync()");
        _cts?.CancelAsync();
#else
        _logger?.WriteLine("cts.Cancel()");
        _cts?.Cancel();
#endif
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        disposeCalled = true;
        allowStartingTasks = false;
        _logger?.WriteLine("cts?.Cancel()");
        _cts?.Cancel();
    }

    public ValueTask DisposeAsync()
    {
        disposeCalled = true;
        allowStartingTasks = false;
#if NETSTANDARD2_1
        _logger?.WriteLine("cts.CancelAsync()");
        _cts?.CancelAsync();
#else
        _logger?.WriteLine("cts.Cancel()");
        _cts?.Cancel();
#endif
        return default;
    }
}