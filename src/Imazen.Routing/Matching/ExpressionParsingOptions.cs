using System.Collections.ObjectModel;

namespace Imazen.Routing.Matching;

public record ExpressionParsingOptions
{
    
    public static ExpressionParsingOptions Default { get; } = new ExpressionParsingOptions();
    /// <summary>
    /// Does not affect character classes.
    /// </summary>
    public bool OrdinalIgnoreCase { get; init; } = false;
    
    public bool AllowStarLiteral { get; init; } = false;
    
    public bool AllowQuestionLiteral { get; init; } = false;

    public bool AllowOptionalSegmentBetweenSlashes { get; init; } = false;

    public bool MatchOptionalTrailingSlash { get; init; } = false;
    // /// <summary>
    // /// If true, all segments will capture the / character by default. If false, segments must specify {:**} to capture slashes.
    // /// </summary>
    // public bool CaptureSlashesByDefault { get; init; } = true;
    //
    internal static ExpressionParsingOptions SubtractFromFlags(ExpressionFlags? expressionFlags, out ExpressionFlags? remainingFlags, ExpressionParsingOptions defaults)
    {
        if (expressionFlags == null)
        {
            remainingFlags = null;
            return defaults;
        }
        var flags = expressionFlags?.Flags.ToList();
        if (flags == null || expressionFlags == null)
        {
            remainingFlags = null;
            return defaults;
        }
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
        if (flags.Remove("allow-optional-segment-between-slashes"))
        {
            defaults = defaults with { AllowOptionalSegmentBetweenSlashes = true };
        }
        if (flags.Remove("/"))
        {
            defaults = defaults with { MatchOptionalTrailingSlash = true };
        }
        remainingFlags = new ExpressionFlags(new ReadOnlyCollection<string>(flags), expressionFlags.Pairs);
        return defaults;
    }
    public static ExpressionParsingOptions ParseComplete(ReadOnlyMemory<char> expressionWithFlags, out ReadOnlyMemory<char> remainingExpression)
    {
        if (!ExpressionFlags.TryParseFromEnd(expressionWithFlags, out remainingExpression, out var flags, out var error,
            ExpressionFlagParsingOptions.Permissive.WithValidationRegex(ExpressionFlags.LowercaseDash())))
        {
            throw new ArgumentException(error, nameof(expressionWithFlags));
        }
        var context = SubtractFromFlags(flags, out var remainingFlags, Default);

        if (remainingFlags?.IsEmpty == false)
        {
            throw new ArgumentException($"Unrecognized flags: {remainingFlags}", nameof(expressionWithFlags));
        }
        return context;
    }
}