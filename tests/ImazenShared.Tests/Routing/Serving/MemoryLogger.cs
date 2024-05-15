using Microsoft.Extensions.Logging;

namespace Imazen.Routing.Tests.Serving;


public readonly record struct MemoryLogEntry
{
    public string Message { get; init; }
    public string Category { get; init; }
    public EventId EventId { get; init; }
    public LogLevel Level { get; init; }
    
    // scope stack snapshot
    public object[]? Scopes { get; init; }
    
    public override string ToString()
    {
        if (Scopes is { Length: > 0 })
        {
            return $"{Level}: {Category}[{EventId.Id}] {Message} Scopes: {string.Join(" > ", Scopes)}";
        }
        return $"{Level}: {Category}[{EventId.Id}] {Message}";
    }
}
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

public class MemoryLoggerFactory : ILoggerFactory
{
    private readonly Func<string, LogLevel, bool>? filter;
    private readonly List<MemoryLogEntry> logs;

    public MemoryLoggerFactory(LogLevel orHigher, List<MemoryLogEntry>? backingList = null)
    {
        filter = (_, level) => level >= orHigher;
        logs = backingList ?? new List<MemoryLogEntry>();
    }
    public MemoryLoggerFactory(Func<string, LogLevel, bool>? filter, List<MemoryLogEntry>? backingList = null)
    { 
        this.filter = filter;
        logs = backingList ?? new List<MemoryLogEntry>();
    }

    public void AddProvider(ILoggerProvider provider)
    {
        throw new NotSupportedException();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new MemoryLogger(categoryName, filter, logs);
    }

    public void Dispose()
    {
    }

    public List<MemoryLogEntry> GetLogs() => logs;
    
    public void Clear() => logs.Clear();
}
