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