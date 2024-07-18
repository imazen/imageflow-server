using Imazen.Routing.HttpAbstractions;

namespace Imazen.Routing.Matching;

public record struct MultiMatchResult
{
    public bool Success { get; init; }
    public Dictionary<string, ReadOnlyMemory<char>>? Captures { get; init; }
    
    /// <summary>
    /// These are only populated if [query] is used. 
    /// </summary>
    public string[]? ExcessQueryKeys { get; init; }
    public string? Error { get; init; }
    
    public IReadOnlyQueryWrapper? OriginalQuery { get; init; }
}