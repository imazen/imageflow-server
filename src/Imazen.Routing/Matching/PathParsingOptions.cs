namespace Imazen.Routing.Matching;

public record PathParsingOptions
{
    
    internal ExpressionParsingOptions ToExpressionParsingOptions()
    {
        return new()
        {
            OrdinalIgnoreCase = OrdinalIgnoreCase,
            AllowStarLiteral = AllowStarLiteral,
            AllowQuestionLiteral = false,
            //CaptureSlashesByDefault = CaptureSlashesByDefault
        };
    }
    /// <summary>
    /// Does not affect character classes.
    /// </summary>
    public bool OrdinalIgnoreCase { get; init; } = false;
    
    public bool AllowStarLiteral { get; init; } = false;
    /// <summary>
    /// If true, all segments will capture the / character by default. If false, segments must specify {:**} to capture slashes.
    /// </summary>
    // public bool CaptureSlashesByDefault { get; init; } = false;
    //
    public static PathParsingOptions SubtractFromFlags(List<string> flags, PathParsingOptions defaults)
    {
        if (flags.Remove("path-ignore-case"))
        {
            defaults = defaults with { OrdinalIgnoreCase = true };
        }
        if (flags.Remove("allow-star-literal"))
        {
            defaults = defaults with { AllowStarLiteral = true };
        }
        return defaults;
    }
    
    public static PathParsingOptions DefaultCaseInsensitive => new()
    {
        OrdinalIgnoreCase = true,
    };
    public static PathParsingOptions DefaultCaseSensitive => new()
    {
        OrdinalIgnoreCase = false,
    };
}