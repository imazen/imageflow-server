namespace Imazen.Routing.Tests.Helpers;

using Xunit;
using System.Threading.Tasks;
using System.Collections.Generic;
using Imazen.Routing.Helpers;
using System;

public class ConcurrencyHelpersTests
{
    [Fact]
    public async Task WhenAnyMatchesOrDefault_AllTasksFailToMatch_ReturnsNull()
    {
        var tasks = new List<Task<int>>
        {
            Task.FromResult(1),
            Task.FromResult(2),
            Task.FromResult(3)
        };

        var result = await ConcurrencyHelpers.WhenAnyMatchesOrDefault(tasks, i => i > 3);

        Assert.Equal(default(int), result);
    }

    [Fact]
    public async Task WhenAnyMatchesOrDefault_AnyTaskMatches_ReturnsThatTaskResult()
    {
        var tasks = new List<Task<int>>
        {
            Task.FromResult(1),
            Task.FromResult(2),
            Task.FromResult(3)
        };

        var result = await ConcurrencyHelpers.WhenAnyMatchesOrDefault(tasks, i => i == 2);

        Assert.Equal(2, result);
    }
    [Fact]
    public async Task WhenAnyMatchesOrDefault_IgnoresFailedTasks()
    {
        var tasks = new List<Task<int>>
        {
            Task.FromException<int>(new Exception("Test exception")),
            Task.FromResult(2),
            Task.FromException<int>(new Exception("Test exception"))
        };

        var result = await ConcurrencyHelpers.WhenAnyMatchesOrDefault(tasks, i => i == 2);

        Assert.Equal(2, result);
    }
    
 
    
    [Fact]
    public async Task WhenAnyMatchesOrDefault_OuterTaskIsCancelled_ThrowsTaskCanceledException()
    {
        var cts = new CancellationTokenSource();
        var tasks = new List<Task<int>>
        {
            Task.Run(int () => { 
                // Throw the waiter cancellation token. 
                cts.Cancel(); cts.Token.ThrowIfCancellationRequested();
                return 0; 
            }, CancellationToken.None)
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConcurrencyHelpers.WhenAnyMatchesOrDefault(tasks, i => true, cts.Token));
    }

    [Fact]
    public async Task WhenAnyMatchesOrDefault_TaskIsFaulted_ReturnsDefault()
    {
        var tasks = new List<Task<int>>
        {
            Task.FromException<int>(new Exception("Test exception"))
        };

        Assert.Equal(default, await ConcurrencyHelpers.WhenAnyMatchesOrDefault(tasks, i => true));
    }
}