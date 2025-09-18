using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Imazen.Routing.Caching.Health.Tests;


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Routing.Caching.Health;

public class NonOverlappingAsyncRunnerTests
{
    ITestOutputHelper _output;
    
    public NonOverlappingAsyncRunnerTests(ITestOutputHelper output)
    {
        _output = output;
        
    }
    

    private INonOverlappingRunner<int> CreateIntRunner(Func<CancellationToken, ValueTask<int>> taskFactory, TimeSpan taskTimeout = default, bool disposeTaskResult = false, int reuseResultWithinMs = 100)
    {
        //return new NonOverlappingTaskRunner<int>((t) => taskFactory(t).AsTask(), taskTimeout,  disposeTaskResult, new TestLoggerAdapter(_output));
        //return new NonOverlappingAsyncRunner<int>((t) => taskFactory(t), taskTimeout,  disposeTaskResult, default, new TestLoggerAdapter(_output));
        return new NonOverlappingCachedRunner<int>((t) => taskFactory(t).AsTask(), taskTimeout, TimeSpan.FromMilliseconds(reuseResultWithinMs), new TestLoggerAdapter(_output));
    }
    private class ArbitraryException : Exception
    {
    }

    private async ValueTask<int> TestTask30(CancellationToken ct)
    {
        await Task.Delay(30, ct);
        return 1;
    }

    private async ValueTask<int> TestTask80(CancellationToken ct)
    {
        await Task.Delay(80, ct);
        return 1;
    }
    private async ValueTask<int> TestTask800(CancellationToken ct)
    {
        await Task.Delay(800, ct);
        return 1;
    }
    [Fact]
    public async Task RunAsync_ShouldStartNewTask_WhenNoTaskIsRunning()
    {
        var runner = CreateIntRunner(TestTask30);
        var result = await runner.RunAsync(default, TestContext.Current.CancellationToken);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task RunAsync_DoesNotStartNewTask_WhenTaskRunning()
    {
        int executionCount = 0;

        async ValueTask<int> TestOverlappingTask(CancellationToken ct)
        {
            await Task.Delay(50, ct);
            Interlocked.Increment(ref executionCount);
            return 1;
        }

        var runner = CreateIntRunner(TestOverlappingTask);
        var task1 = runner.RunAsync(default, TestContext.Current.CancellationToken);
        var task2 = runner.RunAsync(default, TestContext.Current.CancellationToken);
        var result1 = await task1;
        var result2 = await task2;
        Assert.Equal(1, result1);
        Assert.Equal(1, result2);
        Assert.Equal(1, executionCount); // Assert that the task was only executed once
    }

    // Test fire and forget
    [Fact]
    public async Task RunAsync_ShouldNotStartNewTask_WhenFireAndForgetIsUsed()
    {
        int executionCount = 0;

        async ValueTask<int> TestOverlappingTask(CancellationToken ct)
        {
            await Task.Delay(50, ct);
            Interlocked.Increment(ref executionCount);
            return 1;
        }

        var runner = CreateIntRunner(TestOverlappingTask);
        runner.FireAndForget();

        var task1 = runner.RunAsync(TimeSpan.FromMilliseconds(500),TestContext.Current.CancellationToken);
        var task2 = runner.RunAsync(TimeSpan.FromMilliseconds(500),TestContext.Current.CancellationToken);
        var result1 = await task1;
        var result2 = await task2;
        Assert.Equal(1, result1);
        Assert.Equal(1, result2);
        Assert.Equal(1, executionCount); // Assert that the task was only executed once
    }

    [Fact]
    public async Task RunAsync_CancelsTask_WhenCallerTimeoutReached()
    {
        var runner = CreateIntRunner(TestTask800);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await runner.RunAsync(TimeSpan.FromMilliseconds(15),TestContext.Current.CancellationToken));
        await runner.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_CancelsTask_WhenCallerCancellationRequested()
    {
        var runner = CreateIntRunner(TestTask30);
        var cts = new CancellationTokenSource();
        var task = runner.RunAsync(default, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        
        // Create a cancellation token that activates in 500ms
        var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        
        await runner.StopAsync(cts2.Token);
        if (cts2.Token.IsCancellationRequested)
        {
            // The stop did not complete in time
            throw new TimeoutException();
        }
    }

    [Fact]
    public async Task RunAsync_ThrowsExceptionForSynchronousTask_WhenTaskThrowsException()
    {
        var runner = CreateIntRunner(_ => throw new ArbitraryException());
        await Assert.ThrowsAsync<ArbitraryException>(async () => await runner.RunAsync(default, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArbitraryException>(async () =>
            await runner.RunAsync(TimeSpan.FromMilliseconds(500),TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArbitraryException>(async () =>
            await runner.RunAsync(default, new CancellationTokenSource().Token));
    }

    [Fact]
    public async Task
        RunAsync_ReturnsThrownException_WhenSynchronousTaskThrowsException_AndCallerCancellationRequested()
    {
        var runner = CreateIntRunner(_ => throw new ArbitraryException());
        var cts = new CancellationTokenSource();
        await Assert.ThrowsAsync<ArbitraryException>(async () =>
        {
            var task = runner.RunAsync(default, cts.Token);
            cts.Cancel();
            await task;
        });
    }

    private static async ValueTask<int> TestTaskWith10MsDelayedException(CancellationToken ct)
    {
        await Task.Delay(10, ct);
        throw new ArbitraryException();
    }

    [Fact]
    public async Task
        RunAsync_ThrowsTaskCanceledException_WhenTaskThrowsDelayedException_AndCallerCancellationRequestedEarlier()
    {
        var runner = CreateIntRunner(TestTaskWith10MsDelayedException);
        var cts = new CancellationTokenSource();
        var task = runner.RunAsync(TimeSpan.FromMilliseconds(500), cts.Token);
        cts.Cancel(); // Cancel before task completes
        var _ = await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        // TODO: Beware, we aren't testing to see if the stopping times out
        // before the test completes, this is a false positive.
        await runner.StopAsync(CancellationToken.None);
    }

    // [Fact]
    private async Task Dispose_WhileRunAsync_ShouldNotDeadlock()
    {
        var lockAcquired = new ManualResetEventSlim(false);
        var disposeCalled = new ManualResetEventSlim(false);

        var runner = CreateIntRunner(async token =>
        {
            lockAcquired.Set();
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        });

        var runTask = Task.Run(async () => { await runner.RunAsync(); });

        // Wait for the RunAsync to acquire the lock
        lockAcquired.Wait();

        // Start a task that will call Dispose
        var disposeTask = Task.Run(() =>
        {
            runner.Dispose();
            disposeCalled.Set();
        });

        // Wait for the dispose to be called
        // FAILS: System.InvalidOperationException
        // A task may only be disposed if it is in a completion state (RanToCompletion, Faulted or Canceled).
        disposeCalled.Wait(1000);

        // If we reach here without timing out, the test has passed
        // as the dispose did not deadlock.

        // Clean up the tasks
        await Task.WhenAny(runTask, Task.Delay(1000));
        runTask.Dispose();
        disposeTask.Dispose();
    }

    [Fact]
    public async Task RunAsync_Exception_WhenTaskThrowsException_AndTimeoutAndCancellationUsedLater()
    {
        var runner = CreateIntRunner(TestTaskWith10MsDelayedException);
        var cts = new CancellationTokenSource();
        var task = runner.RunAsync(TimeSpan.FromMilliseconds(1000), cts.Token);
        await Task.Delay(100,TestContext.Current.CancellationToken);
        cts.Cancel();
        await Assert.ThrowsAsync<ArbitraryException>(async () => await task);
    }

    // Test repeated calls of Run like 5 times, including reusing after cancellation and exception
    [Fact]
    public async Task RunAsync_MultipleTimesAfterCancelAndExceptions()
    {
        int executionCount = 0;
        int throwNextCount = 0;
        int taskDelay = 30;

        var runner = CreateIntRunner(async ct =>
        {
            await Task.Delay(taskDelay, ct);
            if (ct.IsCancellationRequested) throw new TaskCanceledException();
            Interlocked.Increment(ref executionCount);
            if (throwNextCount > 0)
            {
                throwNextCount--;
                throw new ArbitraryException();
            }

            return 1;
        }, reuseResultWithinMs: 0, taskTimeout: TimeSpan.FromMilliseconds(100));
        var cts = new CancellationTokenSource();
        // try with task exception first
        throwNextCount = 1;
        var task = runner.RunAsync(TimeSpan.FromMilliseconds(1000), cts.Token);
        await Assert.ThrowsAsync<ArbitraryException>(async () => await task);
        Assert.Equal(1, executionCount);
        // Now try again, caller timeout being first (never guaranteed, the scheduler may be busy)
        // The task itself isn't cancelled. 
        task = runner.RunAsync(TimeSpan.FromMilliseconds(5), cts.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        // Now try again with caller cancellation being first
        task = runner.RunAsync(TimeSpan.FromMilliseconds(1000), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        // Now try again with task cancellation being first
        taskDelay = 1000;
        task = runner.RunAsync(default, TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        taskDelay = 0;
        // Now try again with exception being first
        throwNextCount = 1;
        task = runner.RunAsync(TimeSpan.FromMilliseconds(1000), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ArbitraryException>(async () => await task);
        Assert.Equal(3, executionCount);
        // Now try again spamming, no exceptions
        for (int i = 0; i < 5; i++)
        {
            task = runner.RunAsync(default, TestContext.Current.CancellationToken);
            await task;
            Assert.Equal(4 + i, executionCount);
        }

        executionCount = 0;
        if (runner is NonOverlappingCachedRunner<int> cachedRunner)
        {
            // Now try fire and forget 10x, expecting only one execution
            for (int i = 0; i < 10; i++)
            {
                cachedRunner.FireAndForget(TimeSpan.FromMilliseconds(1000));
            }
        }else{
            // Now try fire and forget 10x, expecting only one execution
            for (int i = 0; i < 10; i++)
            {
                runner.FireAndForget();
            }
        }
        await runner.RunAsync(default, TestContext.Current.CancellationToken);
        Assert.Equal(1, executionCount);

        runner.Dispose();
    }

    // Now test dispose
    [Fact]
    public async Task Dispose_StopsTask_WhenTaskIsRunning()
    {
        int completionCount = 0;
        int startedCount = 0;
        int cancelledCount = 0;

        async ValueTask<int> TestOverlappingTask(CancellationToken ct)
        {
            try
            {
                Interlocked.Increment(ref startedCount);
                await Task.Delay(50, ct);
                Interlocked.Increment(ref completionCount);
                return 1;
            }
            catch (TaskCanceledException)
            {
                Interlocked.Increment(ref cancelledCount);
                return 0;
            }
        }

        var runner = CreateIntRunner(TestOverlappingTask, default, true, reuseResultWithinMs: 10);
        Assert.Equal(1, await runner.RunAsync(default, TestContext.Current.CancellationToken));
        Assert.Equal(1, completionCount); // Assert that the task was only executed once
        await Task.Delay(50,TestContext.Current.CancellationToken);
        // fire and forget - may not have started
        runner.FireAndForget();
        Assert.Equal(2, startedCount);

        // We need to wait for the task to start, or it can't be disposed.
        while (startedCount < 2)
        {
            await Task.Delay(1,TestContext.Current.CancellationToken);
        }

        runner.Dispose();
        
        while (completionCount < 2 && cancelledCount < 1)
        {
            await Task.Delay(1,TestContext.Current.CancellationToken);
        }

        Assert.True(cancelledCount == 1 || completionCount == 2);
    }

    // test dispose with the last task being cancelled
    [Fact]
    public async Task Dispose_StopsTask_WhenTaskIsRunning_AndTaskIsCancelled()
    {
        var runner = CreateIntRunner(async ct =>
        {
            await Task.Delay(1, ct);
            throw new TaskCanceledException();
        });
        try
        {
            await runner.RunAsync(default, TestContext.Current.CancellationToken);
        }
        catch (TaskCanceledException)
        {
            // ignored
        }

        await runner.StopAsync(TestContext.Current.CancellationToken);
    }

    // We want to do a lot of parallel testing, parallel calls to RunAsync, and FireAndForget
    // And we want to StopAsync
    // and verify that all tasks are stopped

    // SKIP, blocks
    // TODO: fix or eliminate use of class

    [Fact]
    public async Task Dispose_StopsAllTasks_WhenMultipleTasksAreRunning()
    {
        int completionCount = 0;
        int startedCount = 0;
        int cancelledCount = 0;

        async ValueTask<int> TestOverlappingTask(CancellationToken ct)
        {
            try
            {
                Interlocked.Increment(ref startedCount);
                await Task.Delay(50, ct);
                Interlocked.Increment(ref completionCount);
                return 1;
            }
            catch (TaskCanceledException)
            {
                Interlocked.Increment(ref cancelledCount);
                throw;
            }
        }

        var runner = CreateIntRunner(TestOverlappingTask);
        var tasks = new Task[100];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = runner.RunAsync(default, TestContext.Current.CancellationToken).AsTask();
        }

        await runner.StopAsync(TestContext.Current.CancellationToken);
        // ensure all proxy tasks are cancelled
        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(async () => await Task.WhenAll(tasks));
    }

    [Fact]
    public async Task RunAsync_ConcurrentCalls_ShouldNotOverlap()
    {
        int concurrentExecutionCount = 0;
        int maxConcurrentExecutionCount = 0;

        async ValueTask<int> TestConcurrentTask(CancellationToken ct)
        {
            int currentCount = Interlocked.Increment(ref concurrentExecutionCount);
            maxConcurrentExecutionCount = Math.Max(maxConcurrentExecutionCount, currentCount);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref concurrentExecutionCount);
            return 1;
        }

        var runner = CreateIntRunner(TestConcurrentTask);
        var tasks = Enumerable.Range(0, 10).Select(_ => runner.RunAsync().AsTask()).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, maxConcurrentExecutionCount);
    }

    [Fact]
    public async Task FireAndForget_ConcurrentCalls_ShouldNotOverlap()
    {
        int concurrentExecutionCount = 0;
        int maxConcurrentExecutionCount = 0;

        async ValueTask<int> TestConcurrentTask(CancellationToken ct)
        {
            int currentCount = Interlocked.Increment(ref concurrentExecutionCount);
            maxConcurrentExecutionCount = Math.Max(maxConcurrentExecutionCount, currentCount);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref concurrentExecutionCount);
            return 1;
        }

        var runner = CreateIntRunner(TestConcurrentTask);
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() => runner.FireAndForget())).ToArray();
        await Task.WhenAll(tasks);
        await runner.RunAsync(default, TestContext.Current.CancellationToken); // Ensure all FireAndForget tasks are completed

        Assert.Equal(1, maxConcurrentExecutionCount);
    }

    [Fact]
    public async Task StopAsync_ConcurrentCalls_ShouldNotDeadlock()
    {
        var runner = CreateIntRunner(async _ =>
        {
            await Task.Delay(100);
            return 1;
        });

        var stopTasks = Enumerable.Range(0, 5).Select(_ => runner.StopAsync(default)).ToArray();
        await Task.WhenAll(stopTasks);
    }

    [Fact]
    public async Task RunAsync_ConcurrentWithStopAsync_ShouldNotDeadlock()
    {
        
        var runner = CreateIntRunner(async _ =>
        {
            await Task.Delay(100);
            return 1;
        });
    
        var runTask = runner.RunAsync(default, TestContext.Current.CancellationToken);
        
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        
        
        
        var stopTask = runner.StopAsync(cts.Token);
    
        await Task.WhenAll(runTask.AsTask(), stopTask);
        if (cts.Token.IsCancellationRequested)
        {
            // The stop did not complete in time
            throw new TimeoutException();
        }
    }
    [Fact]
    //NEVER STOPS RUNNING
    public async Task RunAsync_ConcurrentWithStopAsync_ShouldNotDeadlock2()
    {
      
        var runner = CreateIntRunner(async _ =>
        {
            await Task.Delay(100);
            return 1;
        });
    
        var tasks = new List<Task>();
    
        // Start multiple RunAsync tasks
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(runner.RunAsync(default, TestContext.Current.CancellationToken).AsTask());
        }
    
        // Delay to allow the tasks to start executing
        await Task.Delay(50,TestContext.Current.CancellationToken);
    
        // Call StopAsync concurrently
        tasks.Add(runner.StopAsync(TestContext.Current.CancellationToken));
    
        // Wait for all tasks to complete
        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task FireAndForget_ConcurrentWithStopAsync_ShouldNotDeadlock()
    {
        var runner = CreateIntRunner(async _ =>
        {
            await Task.Delay(100);
            return 1;
        });

        var fireAndForgetTask = Task.Run(() => runner.FireAndForget());
        var stopTask = runner.StopAsync(TestContext.Current.CancellationToken);

        await Task.WhenAll(fireAndForgetTask, stopTask);
    }

    [Fact]
    public async Task Constructor_TaskMustBeDisposed_ShouldDisposeTask()
    {
        var runner = CreateIntRunner(async _ =>
        {
            await Task.Delay(100);
            return 1;
        }, disposeTaskResult: true);

        await runner.RunAsync(default, TestContext.Current.CancellationToken);
        // Verify that the task is disposed after running
    }

    [Fact]
    public async Task Constructor_Timeout_ShouldFailToCancelTaskAfterTimeoutWithoutCooperation()
    {
        var runner = CreateIntRunner(async _ =>
        {
            await Task.Delay(200);
            return 1;
        }, taskTimeout: TimeSpan.FromMilliseconds(5));

        // No exception is thrown because the code doesn't cooperate in the cancellation, and there is no caller cancellation.
        var result = await runner.RunAsync(default, TestContext.Current.CancellationToken);
        Assert.Equal(1, result);
        
    }

    [Fact]
    public async Task Constructor_Timeout_ShouldCancelTaskAfterUnderlyingTimeout()
    {
        var runner = CreateIntRunner(async ct =>
        {
            await Task.Delay(1000,ct);
            return 1;
        }, taskTimeout: TimeSpan.FromMilliseconds(20));

        // Assert.Throws() Failure: No exception was thrown
        await Assert.ThrowsAsync<TaskCanceledException>(() => runner.RunAsync(default, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Dispose_ShouldStopAllTasks()
    {
            
        // DEADLOCKS: Sometimes, this simply never stops running...
        var runner = CreateIntRunner(async ct =>
        {
            await Task.Delay(1000,ct);
            return 1;
        });

        var tasks = Enumerable.Range(0, 5).Select(_ => runner.RunAsync().AsTask()).ToArray();
        runner.Dispose();

        await Assert.ThrowsAsync<TaskCanceledException>(() => Task.WhenAll(tasks));
    }
    [Fact]
    public async Task RunAsync_TaskFactoryThrowsException_ShouldPropagateException()
    {
        var runner = CreateIntRunner(_ => throw new InvalidOperationException());

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(default, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task RunAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        var runner = CreateIntRunner(async _ =>
        {
            await Task.Delay(100);
            return 1;
        });

        runner.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => runner.RunAsync(default, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task StartAsync_ShouldNotThrowException()
    {
        var runner = CreateIntRunner(async _ =>
        {
            await Task.Delay(100);
            return 1;
        });

        await runner.StartAsync(CancellationToken.None);
    }
}