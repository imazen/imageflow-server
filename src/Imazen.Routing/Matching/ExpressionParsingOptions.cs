namespace Imazen.Routing.Matching;

public record ExpressionParsingOptions
{
    /// <summary>
    /// Does not affect character classes.
    /// </summary>
    public bool OrdinalIgnoreCase { get; init; } = false;
    
    public bool AllowStarLiteral { get; init; } = false;
    
    public bool AllowQuestionLiteral { get; init; } = false;
    // /// <summary>
    // /// If true, all segments will capture the / character by default. If false, segments must specify {:**} to capture slashes.
    // /// </summary>
    // public bool CaptureSlashesByDefault { get; init; } = true;
    //
    internal static ExpressionParsingOptions SubtractFromFlags(List<string> flags, ExpressionParsingOptions defaults)
    {
        if (flags.Remove("ignore-case"))
        {
            defaults = defaults with { OrdinalIgnoreCase = true };
        }
        if (flags.Remove("allow-star-literal"))
        {
            defaults = defaults with { AllowStarLiteral = true };
        }
        if (flags.Remove("allow-question-literal"))
        {
            defaults = defaults with { AllowQuestionLiteral = true };
        }
        // if (flags.Remove("capture-slashes"))
        // {
        //     defaults = defaults with { CaptureSlashesByDefault = true };
        // }
        return defaults;
    }
    public static ExpressionParsingOptions ParseComplete(ReadOnlyMemory<char> expressionWithFlags, out ReadOnlyMemory<char> remainingExpression)
    {
        if (!ExpressionFlags.TryParseFromEnd(expressionWithFlags, out var expression, out var flags, out var error))
        {
            throw new ArgumentException(error, nameof(expressionWithFlags));
        }
        
        var context = SubtractFromFlags(flags!, new ExpressionParsingOptions());
        remainingExpression = expression;
        if (flags!.Count > 0)
        {
            throw new ArgumentException($"Unrecognized flags: {string.Join(", ", flags)}", nameof(expressionWithFlags));
        }
        return context;
    }
}