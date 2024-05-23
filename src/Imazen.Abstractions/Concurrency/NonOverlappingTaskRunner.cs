
using System;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Abstractions.Concurrency;
using Imazen.Abstractions.Logging;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Should be thread-safe on all methods
/// </summary>
/// <typeparam name="T"></typeparam>
public class NonOverlappingTaskRunner<T> : IHostedService, IDisposable, INonOverlappingRunner<T>
{
    private readonly Func<CancellationToken, Task<T>> _taskFactory;
    private readonly TimeSpan _timeout;
    private readonly bool _taskMustBeDisposed;
    private readonly object _lock = new object();
    private TaskCompletionSource<T>? _currentTaskSource;
    private CancellationTokenSource? _cancellationTokenSource;
    private ITestLogging? _logger;

    public NonOverlappingTaskRunner(Func<CancellationToken, Task<T>> taskFactory, TimeSpan timeout, bool taskMustBeDisposed, ITestLogging? logger = default)
    {
        _taskFactory = taskFactory;
        _timeout = timeout;
        _taskMustBeDisposed = taskMustBeDisposed;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // No need to start anything here, as the task will be started on-demand in RunAsync
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger?.WriteLine("StopAsync() called");
        lock (_lock)
        {
            _cancellationTokenSource?.Cancel();
        }
        _logger?.WriteLine("cancellationTokenSource.Cancel() completed");
        return Task.CompletedTask;
    }

  public async ValueTask<T> RunAsync(TimeSpan callerTimeout = default, CancellationToken callerCancellationToken = default)
    {
        TaskCompletionSource<T>? taskSource;

        lock (_lock)
        {
            if (_currentTaskSource == null || _currentTaskSource.Task.IsCompleted)
            {
                _logger?.WriteLine("Creating new task source");
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
                _currentTaskSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

                if (_timeout != Timeout.InfiniteTimeSpan && _timeout != TimeSpan.Zero && _timeout != default)
                {
                    _logger?.WriteLine("Starting task with timeout: {0}", _timeout);
                    using var timeoutCancellationTokenSource = new CancellationTokenSource(_timeout);
                    using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                        _cancellationTokenSource.Token, timeoutCancellationTokenSource.Token);

                    _ = RunTaskAsync(_currentTaskSource, linkedCancellationTokenSource.Token);
                }
                else
                {
                    _logger?.WriteLine("Starting task without timeout");
                    _ = RunTaskAsync(_currentTaskSource, _cancellationTokenSource.Token);
                }
            }
            else
            {
                _logger?.WriteLine("Reusing existing task source");
            }

            taskSource = _currentTaskSource;
        }

        try
        {
            if (callerTimeout != Timeout.InfiniteTimeSpan && callerTimeout != TimeSpan.Zero && callerTimeout != default)
            {
                _logger?.WriteLine("Waiting for task with caller timeout: {0}", callerTimeout);
                return await taskSource!.Task.WithCancellationAndTimeout(callerCancellationToken, callerTimeout);
            }
            else
            {
                _logger?.WriteLine("Waiting for task without caller timeout");
                return await taskSource!.Task.WithCancellation(callerCancellationToken);
            }
        }
        catch (OperationCanceledException) when (taskSource!.Task.IsCanceled)
        {
            _logger?.WriteLine("Warning: Task source cancellation occurred");
            throw;
        }
    }
        // catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        // {
        //     // Caller cancellation, ignore and return default value
        //     return default!;
        // }
        // catch (TaskCanceledException) when (callerCancellationToken.IsCancellationRequested)
        // {
        //     // Task cancellation, ignore and return default value
        //     return default!;
        // }
    

    private async Task RunTaskAsync(TaskCompletionSource<T> taskSource, CancellationToken cancellationToken)
    {
        bool setState = false;
        try
        {
            _logger?.WriteLine("Running task");
            var result = await _taskFactory(cancellationToken);
            if (result == null)
            {
                _logger?.WriteLine("Task completed with null result");
            }
            else
            {
                _logger?.WriteLine("Task completed with result: {0}", result);
            }
            if (_taskMustBeDisposed && result is IDisposable disposable)
            {
                _logger?.WriteLine("Disposing task result");
                disposable.Dispose();
            }
            _logger?.WriteLine("Setting task result");
            taskSource.TrySetResult(result);
            setState = true;
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.WriteLine("Warning: Task cancellation requested");
            taskSource.TrySetCanceled(oce.CancellationToken);
            setState = true;
        }
        catch (TaskCanceledException tce)
        {
            _logger?.WriteLine("Warning: Task cancelled");
            taskSource.TrySetCanceled(tce.CancellationToken);
            setState = true;
        }
        catch (Exception ex)
        {
            _logger?.WriteLine("Error: Task threw an exception {0}", ex);
            taskSource.TrySetException(ex);
            setState = true;
        }
        finally
        {
            if (!setState)
            {
                _logger?.WriteLine("Task did not complete or error or cancel. Setting cancellation");
                taskSource.TrySetCanceled(cancellationToken);
            }
            _logger?.WriteLine("RunTaskAsync() exiting");
        }
        
    }

    public T? FireAndForget()
    {
        _logger?.WriteLine("FireAndForget() called");
        var t = RunAsync(default,default);
        if (t.IsCompleted)
        {
            return t.Result;
        }
        return default;
    }

    private bool _disposed;
    private readonly SemaphoreSlim _disposalSemaphore = new SemaphoreSlim(1, 1);

    public async ValueTask DisposeAsync()
    {
        _logger?.WriteLine("DisposeAsync() called");
        await _disposalSemaphore.WaitAsync();
        try
        {
            await DisposeAsyncCore();
        }
        finally
        {
            _disposalSemaphore.Release();
            _logger?.WriteLine("DisposeAsync() complete");
        }
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_disposed)
        {
            _logger?.WriteLine("DisposeAsync() - Already disposed");
            return;
        }

        if (_cancellationTokenSource != null)
        {
            
#if NETSTANDARD2_1_OR_GREATER
            _logger?.WriteLine("_cancellationTokenSource.CancelAsync()");
            await _cancellationTokenSource.CancelAsync();
#else
            _logger?.WriteLine("_cancellationTokenSource.Cancel()");
            _cancellationTokenSource.Cancel();
            await Task.CompletedTask;
#endif
            _logger?.WriteLine("_cancellationTokenSource.Dispose()");
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }

        _disposed = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            _logger?.WriteLine("Dispose() - Already disposed");
            return;
        }

        _disposalSemaphore.Wait();
        try
        {
            DisposeSyncCore();
        }
        finally
        {
            _disposalSemaphore.Release();
            _logger?.WriteLine("Dispose() complete");
        }
        

        _disposed = true;
    }

    protected virtual void DisposeSyncCore()
    {
        if (_cancellationTokenSource != null)
        {
            _logger?.WriteLine("_cancellationTokenSource.Cancel()");
            _cancellationTokenSource.Cancel();
            _logger?.WriteLine("_cancellationTokenSource.Dispose()");
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }
    }
}