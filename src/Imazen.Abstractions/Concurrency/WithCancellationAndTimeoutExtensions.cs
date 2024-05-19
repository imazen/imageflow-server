using Imazen.Abstractions.HttpStrings;

namespace Imazen.Abstractions.Concurrency;

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. 
// Modified by Imazen LLC. Modifications are licensed under the MIT license.
using System;
using System.Threading;
using System.Threading.Tasks;
/// <summary>
/// Utility methods for working across threads.
/// </summary>
internal static class WithCancellationAndTimeoutExtensions
{

 public static Task<T> WithCancellationAndTimeout<T>(this Task<T> task, CancellationToken cancellationToken, TimeSpan timeout)
    {
        ArgumentNullThrowHelper.ThrowIfNull(task, nameof(task));

        if (task.IsCompleted || !cancellationToken.CanBeCanceled)
        {
            return task;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        if (timeout == Timeout.InfiniteTimeSpan || timeout == TimeSpan.Zero || timeout == default)
        {
            return task.WithCancellation(cancellationToken);
        }

        return WithCancellationAndTimeoutSlow(task, cancellationToken, timeout);
    }

    public static Task WithCancellationAndTimeout(this Task task, CancellationToken cancellationToken, TimeSpan timeout)
    {
        ArgumentNullThrowHelper.ThrowIfNull(task, nameof(task));

        var noTimeout = (timeout == Timeout.InfiniteTimeSpan || timeout == TimeSpan.Zero || timeout == default);
        var noToken = !cancellationToken.CanBeCanceled;

        if (task.IsCompleted || (noTimeout && noToken))
        {
            return task;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (noTimeout)
        {
            return task.WithCancellation(cancellationToken);
        }

        return WithCancellationAndTimeoutSlow(task, cancellationToken, timeout);
    }
        private static async Task<T> WithCancellationAndTimeoutSlow<T>(Task<T> task, CancellationToken cancellationToken, TimeSpan timeout)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await task.WithCancellation(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
        {
            throw new TimeoutException();
        }
    }

    private static async Task WithCancellationAndTimeoutSlow(Task task, CancellationToken cancellationToken, TimeSpan timeout)
    {
        CancellationTokenSource cts;
        if (cancellationToken == default || !cancellationToken.CanBeCanceled)
        {
            cts = new CancellationTokenSource(timeout);
        }else {
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
        }
        using (cts)
        {
            try
            {
                await task.WithCancellation(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
            {
                throw new TimeoutException();
            }
        }
    }
}