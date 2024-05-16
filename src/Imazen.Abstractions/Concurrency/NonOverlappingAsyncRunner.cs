using Microsoft.Extensions.Hosting;

namespace Imazen.Routing.Caching.Health;

////
/// This class constructor accepts a Func&lt;CancellationToken, Task&lt;T>> parameter and a timeout. The func always runs via Task.Run, even if initiated via RunNonOverlapping.
/// This class exposes a method RunNonOverlapping function that accepts a cancellation token (if this one is cancelled, the task returns early but the function keeps running).
/// This class only cancels the background function if StopAsync(CancellationToken ct) is called or it is disposed (it implements async dispose).
/// If RunNonOverlapping is called, and the background task is not running, it is scheduled with Task.Run
/// RunNonOverlapping returns a task that completes when the background task completes, fails, or is cancelled. These results are passed through.

public class NonOverlappingAsyncRunner<T>(
    Func<CancellationToken, ValueTask<T>> taskFactory,
    bool taskMustBeDisposed = false,
    TimeSpan timeout = default, 
    CancellationToken cancellationToken = default)
    : IHostedService, IDisposable, IAsyncDisposable
{
    private CancellationTokenSource? taskCancellation;
    private readonly object taskInitLock = new object();
    private TaskCompletionSource<T>? taskStatus;
    private Task? task;
    private bool stopped;
    private bool stopping;
    public Task StopAsync(CancellationToken stopWaitingForCancellationToken = default)
        => StopAsync(Timeout.InfiniteTimeSpan, stopWaitingForCancellationToken);
    
    public async Task StopAsync(TimeSpan stopTimeout, CancellationToken stopWaitingForCancellationToken = default)
    {
        
        try
        {
            Task? asyncCancelTask = null;
            if (stopWaitingForCancellationToken.IsCancellationRequested)
            {
                return;
            }
            
            lock (taskInitLock)
            {
                stopping = true;
#if NET8_0_OR_GREATER
                asyncCancelTask = taskCancellation?.CancelAsync();
#else
                taskCancellation?.Cancel();
#endif
            }
            try
            {
                if (stopWaitingForCancellationToken.IsCancellationRequested)
                {
                    return;
                }
                // We need to respect the stopWaitingForCancellationToken above waiting for the task to complete
                // but if nothing is running, Task.CompletedTask is returned, so we can't await it.
                if (asyncCancelTask != null && asyncCancelTask is { IsCompleted: false })
                {
                    await Task.WhenAny(asyncCancelTask, 
                        Task.Delay(stopTimeout, stopWaitingForCancellationToken)).ConfigureAwait(false);
                    
                    // This doesn't handle the waiting for activation case.
                }
                
                await RunNonOverlappingAsyncInternal(true, stopTimeout, stopWaitingForCancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }

        }finally
        {
             // even if canceled/faulted, we're done
            stopped = true;
            DisposeTaskAndCancellation();
        }
    }

    private void DisposeTaskAndCancellation()
    {
        lock (taskInitLock)
        {
            try
            {
                if (task != null)
                {
                    var taskStatusCopy = task.Status;
                    if (taskStatusCopy is TaskStatus.RanToCompletion or TaskStatus.Canceled or TaskStatus.Faulted)
                    {
                        // Dispose says it can be used with Cancel
                        task?.Dispose();
                    }
                    else if (taskMustBeDisposed)
                    {
                        throw new InvalidOperationException($"Task status was {taskStatusCopy}, and cannot be disposed.");
                    }
                }

                task = null;
            }
            finally
            {
                if (taskCancellation != null)
                {
                    if (taskCancellation.IsCancellationRequested)
                    {
                        taskCancellation.Dispose();
                    }
                    else
                    {
                        taskCancellation.Cancel();
                    }

                    taskCancellation.Dispose();
                    taskCancellation = null;
                }
            }
        }
    }
    public void Dispose()
    {
        try
        {
            var t = StopAsync();
            t.Wait(5);
        }
        finally
        {
            stopped = true;
            DisposeTaskAndCancellation();
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        try
        {
            stopping = true;
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            stopped = true;
            taskCancellation?.Dispose();
            task?.Dispose();
            taskCancellation = null;
            task = null;
        }
    }
    
    /// <summary>
    /// Returns true if the completedSyncResult value should be used instead
    /// of using taskStatus.
    /// </summary>
    /// <param name="completedSyncResult"></param>
    /// <returns></returns>
    private bool StartTask(out T completedSyncResult)
    {
        if (taskCancellation is { IsCancellationRequested: true })
        {
            completedSyncResult = default!;
            return false;
        }
        lock (taskInitLock)
        {
            if (stopping | stopped) throw new InvalidOperationException("Cannot start a task after StopAsync or Dispose has been called");
            if (taskStatus != null)
            {
                completedSyncResult = default!;
                return false;
            }
            
            if (taskCancellation is not { IsCancellationRequested: true })
            {
                // Create if missing
                if (taskCancellation == null)
                {
                    taskCancellation?.Dispose();
                    taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                }
                // And reset the timer. We reset the cancellation token when we set taskStatus == null, 
                // which clears the timer.
                if (timeout != default && timeout != TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                {
                    taskCancellation.CancelAfter(timeout);
                }
            }
            
            ValueTask<T> innerTask = taskFactory(taskCancellation.Token);
            if (innerTask.IsCompleted)
            {
                completedSyncResult = innerTask.Result;
                ResetCancellationTokenSourceUnsynchronized();
                return true;
            }
            taskStatus = new TaskCompletionSource<T>();
            task = Task.Run(async () =>
            {
                if (taskCancellation is { IsCancellationRequested: true })
                {
                    taskStatus.TrySetCanceled();
                    return;
                }
                try
                {
                    
                    var result = await innerTask.ConfigureAwait(false);
                    taskStatus.TrySetResult(result);
                }
                catch (OperationCanceledException)
                {
                    taskStatus.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    taskStatus.TrySetException(ex);
                }
                finally
                {
                    lock (taskInitLock)
                    {
                        taskStatus = null;
                        ResetCancellationTokenSourceUnsynchronized();
                    }
                }
            }, taskCancellation.Token);
            completedSyncResult = default!;
            return false;
        }
    }
    private void ResetCancellationTokenSourceUnsynchronized()
    {
#if NET6_0_OR_GREATER
        taskCancellation?.TryReset(); // Reset the timer
#else

        var t = taskCancellation;
        t?.Dispose();
        taskCancellation = null;
#endif 
    }

    /// <summary>
    /// Fires off the task and returns a value only if the function response synchronously instead of asynchronously.
    /// Ignored if we are stopping or stopped.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <returns>Returns a value only if the function response synchronously instead of asynchronously..</returns>
    public T? FireAndForget()
    {
        if (stopping || stopped) return default;
        if (taskStatus == null && StartTask(out var completedSyncResult2))
        {
            return completedSyncResult2;
        }
        return default;
    }

    /// <summary>
    /// This method returns a task that completes when the background task completes, fails, or is cancelled. The results are passed through.
    /// It won't start the background task if it is already running. If the background task is not running, it is scheduled with Task.Run.
    /// Previous results are never used, only in-progress or new results.
    /// </summary>
    /// <param name="proxyTimeout">This task will return a cancelled task if the proxyTimeout is reached. The background task will not be affected.</param>
    /// <param name="proxyCancellation">This task will return a cancelled task if the proxyCancellation is cancelled. The background task will not be affected.</param>
    /// <returns></returns>
    /// <exception cref="TaskCanceledException">Thrown when the timeout is reached or the cancellation token is activated</exception>
    /// <exception cref="Exception">Any exceptions thrown by the underlying task</exception>
    public ValueTask<T> RunNonOverlappingAsync(TimeSpan proxyTimeout = default,
        CancellationToken proxyCancellation = default)
    {
        
        if (stopping || stopped) throw new ObjectDisposedException(nameof(NonOverlappingAsyncRunner<T>));
        return RunNonOverlappingAsyncInternal(false, proxyTimeout, proxyCancellation);
    }
    
    // TODO, probably remove this duplicate implementation
    private ValueTask<T> CreateLinkedTaskWithTimeoutAndCancellation(Task<T> newTask, TimeSpan proxyTimeout, CancellationToken proxyCancellation)
    {
        // We have to create a linked task that will cancel if either the proxy or the constructor cancellation token is cancelled
        // or if the proxy timeout is reached
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, proxyCancellation);
        if (proxyTimeout != default && proxyTimeout != Timeout.InfiniteTimeSpan)
        {
            linkedCts.CancelAfter(proxyTimeout);
        }
        var linkedToken = linkedCts.Token;
        // Now we have to return a task that completes when the task completes, or when the proxy is cancelled
        // linkedToken should not cancel the original task, just the proxy
        // We can use WhenAny, but we have to do logic on the result to expose the result (or throw)
        return new ValueTask<T>(Task.WhenAny(newTask, Task.Delay(Timeout.InfiniteTimeSpan, linkedToken)).ContinueWith(taskWrapper =>
        {
            try
            {
                // taskWrapper.Result will be the task that completed, or the delay task.
                // taskWrapper does RunToCompletion even if one faults.
                // t is firstOfTheTasks
                var t = taskWrapper.Result;
                if (t.IsCanceled)
                {
                    throw new OperationCanceledException(linkedToken);
                }
                else if (t.IsFaulted)
                {
                    // The outer task will still throw an AggregateException, 
                    // but at least it won't be as nested?
                    throw t.Exception!.InnerExceptions.Count == 1
                        ? t.Exception!.InnerExceptions[0]
                        : t.Exception!;
                }
                else
                {
                    // Task.Delay will never return since we gave it infinite time, it only faults or cancels
                    return newTask.Result;
                }
            }
            finally
            {
                linkedCts.Dispose();
            }
        }, linkedToken));
    }
    
    // This variant will use await so that exceptions are unwrapped and users don't deal with AggregateExceptions
    private async ValueTask<T> CreateLinkedTaskWithTimeoutAndCancellationAsync(Task<T> newTask, TimeSpan proxyTimeout,
        CancellationToken proxyCancellation)
    {
        // We have to create a linked task that will cancel if either the proxy or the constructor cancellation token is cancelled
        // or if the proxy timeout is reached
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, proxyCancellation);
        if (proxyTimeout != default && proxyTimeout != Timeout.InfiniteTimeSpan)
        {
            linkedCts.CancelAfter(proxyTimeout);
        }
        var linkedToken = linkedCts.Token;
        // Now we have to return a task that completes when the task completes, or when the proxy is cancelled
        // linkedToken should not cancel the original task, just the proxy
        // We can use WhenAny, but we have to do logic on the result to expose the result (or throw)
        try
        {
            // taskWrapper.Result will be the task that completed, or the delay task.
            // taskWrapper does RunToCompletion even if one faults.
            var firstOfTheTasks = await Task.WhenAny(newTask, Task.Delay(Timeout.InfiniteTimeSpan, linkedToken));
            // determine which task completed
            if (firstOfTheTasks == newTask)
            {
                return await newTask;
            }
            else
            {
                // Task.Delay will never return since we gave it infinite time, it only faults or cancels
                await firstOfTheTasks;
                throw new InvalidOperationException("Task.Delay should never return");
            }
        }
        finally
        {
            linkedCts.Dispose();
        }
    }
    
        
    private ValueTask<T> RunNonOverlappingAsyncInternal(bool stoppingWaiting, TimeSpan proxyTimeout = default, CancellationToken proxyCancellation = default)
    {
        lock (taskInitLock)
        {
            if (proxyTimeout == Timeout.InfiniteTimeSpan) proxyTimeout = default;
            
            // If we're in wait-for-existing-tasks mode, we can't start a new task
            if (stoppingWaiting && taskStatus == null)
            {
                return new ValueTask<T>(default(T)!);
            }
            
            if (proxyCancellation != default || proxyTimeout != default)
            {
                if (taskStatus == null)
                {
                    if (StartTask(out var completedSyncResult))
                    {
                        // It completed synchronously, so we can just return the result
                        return new ValueTask<T>(completedSyncResult);
                    }
                }
                if (taskStatus != null)
                {
                    return CreateLinkedTaskWithTimeoutAndCancellationAsync(taskStatus.Task, proxyTimeout, proxyCancellation);
                }
                // StartTask should have set taskStatus
                throw new InvalidOperationException("Task.Delay should never return");
            }
            // No proxy cancellation or timeout? Ee can just return the task
            if (taskStatus != null)
            {
                return new ValueTask<T>(taskStatus.Task);
            }
            if (stoppingWaiting)
            {
                throw new InvalidOperationException("Unreachable code");
            }
            return StartTask(out var completedSyncResult2) ? new ValueTask<T>(completedSyncResult2) : new ValueTask<T>(taskStatus!.Task);
        }
    }

    
    public Task StartAsync(CancellationToken cancellation)
    {
        return Task.CompletedTask;
    }


    
}
    
    