namespace Imazen.Routing.Matching;

public record MatchingContext
{
    public required IReadOnlyCollection<string> SupportedImageExtensions { get; init; }
    
    public bool VerboseErrors { get; init; } = true;
    
    internal static MatchingContext Default => new()
    {
        SupportedImageExtensions = new []{"jpg", "jpeg", "png", "gif", "webp"}
    };
 
}