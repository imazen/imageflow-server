using Microsoft.Extensions.Logging;

namespace Imazen.Routing.Tests.Serving;

public class MemoryLogger(string categoryName, Func<string, LogLevel, bool>? filter, List<MemoryLogEntry> logs)
    : ILogger
{
    private readonly object @lock = new();

    private static readonly AsyncLocal<Stack<object>> Scopes = new AsyncLocal<Stack<object>>();

    public IDisposable BeginScope<TState>(TState state)
    where TState : notnull
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        Scopes.Value ??= new Stack<object>();

        Scopes.Value.Push(state);
        return new DisposableScope();
    }


    public bool IsEnabled(LogLevel logLevel)
    {
        return filter == null || filter(categoryName, logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var scopesCopy = Scopes.Value?.ToArray();
        lock (@lock)
        {
            logs.Add(new MemoryLogEntry
            {
                Scopes = scopesCopy,
                Message = formatter(state, exception),
                Category = categoryName,
                EventId = eventId,
                Level = logLevel
            });
        }
    }

    private class DisposableScope : IDisposable
    {
        public void Dispose()
        {
            var scopes = Scopes.Value;
            if (scopes != null && scopes.Count > 0)
            {
                scopes.Pop();
            }
        }
    }
    
}