namespace Imazen.Routing.Matching;

public record ReplacementOptions
{
    public bool DeleteExcessQueryKeys { get; init; } = false;
    
    public static ReplacementOptions SubtractFromFlags(List<string> flags)
    {
        var context = new ReplacementOptions();
        if (flags.Remove("query-delete-excess"))
        {
            context = context with { DeleteExcessQueryKeys = true };
        }
        return context;
    }
    
}