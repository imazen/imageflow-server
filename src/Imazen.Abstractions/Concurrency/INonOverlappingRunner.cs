using Microsoft.Extensions.Hosting;

public interface INonOverlappingRunner<T> : IHostedService, IDisposable, IAsyncDisposable
{
    ValueTask<T> RunAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default);
    T? FireAndForget();
}