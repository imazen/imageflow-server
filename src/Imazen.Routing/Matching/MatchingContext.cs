namespace Imazen.Routing.Matching;

public record MatchingContext
{
  
    public bool VerboseErrors { get; init; } = true;
    
    internal static MatchingContext Default => new()
    {
        
    };
 
}