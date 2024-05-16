using Microsoft.Extensions.Logging;

namespace Imazen.Routing.Tests.Serving;

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