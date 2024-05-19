using System;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Abstractions.Concurrency;
using Xunit;

public class WithCancellationAndTimeoutTests
{
    [Fact]
    public async Task WithCancellationAndTimeout_CompletedTask_ReturnsOriginalTask()
    {
        // Arrange
        var completedTask = Task.FromResult(42);
        var cancellationToken = new CancellationToken();
        var timeout = TimeSpan.FromSeconds(1);

        // Act
        var result = await completedTask.WithCancellationAndTimeout(cancellationToken, timeout);

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task WithCancellationAndTimeout_CanceledToken_ReturnsCanceledTask()
    {
        // Arrange
        var task = Task.Delay(TimeSpan.FromSeconds(1));
        var cancellationToken = new CancellationToken(canceled: true);
        var timeout = TimeSpan.FromSeconds(1);

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => task.WithCancellationAndTimeout(cancellationToken, timeout));
    }

    [Fact]
    public async Task WithCancellationAndTimeout_TimeoutOccurs_ThrowsTimeoutException()
    {
        // Arrange
        var task = Task.Delay(TimeSpan.FromSeconds(1));
        var cancellationToken = new CancellationToken();
        var timeout = TimeSpan.FromMilliseconds(100);

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() => task.WithCancellationAndTimeout(cancellationToken, timeout));
    }

    [Fact]
    public async Task WithCancellationAndTimeout_FastPath_UsesWithCancellation()
    {
        // Arrange
        var task = Task.Delay(TimeSpan.FromSeconds(1));
        var cancellationToken = new CancellationToken(canceled: true);
        var timeout = Timeout.InfiniteTimeSpan;

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => task.WithCancellationAndTimeout(cancellationToken, timeout));
    }

    [Fact]
    public async Task WithCancellationAndTimeout_TaskCompletesBeforeTimeout_ReturnsOriginalResult()
    {
        // Arrange
        var task = Task.FromResult(42);
        var cancellationToken = new CancellationToken();
        var timeout = TimeSpan.FromSeconds(1);

        // Act
        var result = await task.WithCancellationAndTimeout(cancellationToken, timeout);

        // Assert
        Assert.Equal(42, result);
    }
    [Fact]
    public async Task WithCancellationAndTimeout_TaskCompletesBeforeTimeout_WithDefaultToken_ReturnsOriginalResult()
    {
        // Arrange
        var task = Task.FromResult(42);
        var cancellationToken = default(CancellationToken);
        var timeout = TimeSpan.FromSeconds(1);

        // Act
        var result = await task.WithCancellationAndTimeout(cancellationToken, timeout);

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task WithCancellationAndTimeout_TimeoutOccurs_WithDefaultToken_ThrowsTimeoutException()
    {
        // Arrange
        var task = Task.Delay(TimeSpan.FromSeconds(1));
        var cancellationToken = default(CancellationToken);
        var timeout = TimeSpan.FromMilliseconds(100);

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() => task.WithCancellationAndTimeout(cancellationToken, timeout));
    }

    [Fact]
    public async Task WithCancellationAndTimeout_TaskCompletesBeforeTimeout_WithNonCancellableToken_ReturnsOriginalResult()
    {
        // Arrange
        var task = Task.FromResult(42);
        var cancellationToken = CancellationToken.None;
        var timeout = TimeSpan.FromSeconds(1);

        // Act
        var result = await task.WithCancellationAndTimeout(cancellationToken, timeout);

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task WithCancellationAndTimeout_TimeoutOccurs_WithNonCancellableToken_ThrowsTimeoutException()
    {
        // Arrange
        var task = Task.Delay(TimeSpan.FromSeconds(1));
        var cancellationToken = CancellationToken.None;
        var timeout = TimeSpan.FromMilliseconds(100);

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() => task.WithCancellationAndTimeout(cancellationToken, timeout));
    }
}