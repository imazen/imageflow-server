using System.Diagnostics.CodeAnalysis;

namespace Imazen.Routing.Matching;

internal readonly record struct 
    MatchSegment(string? Name, SegmentBoundary StartsOn, SegmentBoundary EndsOn, List<StringCondition>? Conditions)
{
    public override string ToString()
    {
        if (Name == null && EndsOn.IsLiteralEnd)
        {
            var literal = StartsOn.AsCaseSensitiveLiteral;
            if (literal != null)
            {
                return literal;
            }
        }
        var conditionsString = Conditions == null ? "" : string.Join(":", Conditions);
        if (conditionsString.Length > 0)
        {
            conditionsString = ":" + conditionsString;
        }
        return $"{{{Name ?? ""}:{StartsOn}:{EndsOn}{conditionsString}}}";
    }
    public bool ConditionsMatch(MatchingContext context, ReadOnlySpan<char> text)
    {
        if (Conditions == null) return true;
        foreach (var condition in Conditions)
        {
            if (!condition.Evaluate(text, context))
            {
                return false;
            }
        }
        return true;
    }
    
    
    public bool IsOptional => StartsOn.IsOptional;

    internal static bool TryParseSegmentExpression(ExpressionParsingOptions options, 
        ReadOnlyMemory<char> exprMemory, 
        Stack<MatchSegment> laterSegments, 
        out bool justALiteral,
        [NotNullWhen(true)]out MatchSegment? segment, 
        [NotNullWhen(false)]out string? error)
    {
        var expr = exprMemory.Span;
        justALiteral = false;
        if (expr.IsEmpty)
        {
            segment = null;
            error = "Empty segment";
            return false;
        }
        error = null;
        
        if (expr[0] == '{')
        {
            if (expr[^1] != '}')
            {
                error = $"Unmatched '{{' in segment expression {{{expr.ToString()}}}";
                segment = null;
                return false;
            }
            var innerMem = exprMemory[1..^1];
            if (innerMem.Length == 0)
            {
                error = "Segment {} cannot be empty. Try {name}, {name:condition1:condition2}";
                segment = null;
                return false;
            }
            return TryParseLogicalSegment(options, innerMem, laterSegments, out segment, out error);

        }
        // it's a literal
        // Check for invalid characters like &
        if (!options.AllowStarLiteral && expr.IndexOf('*') != -1)
        {
            error = "Literals cannot contain * operators, they must be enclosed in {} such as {name:**:?}";
            segment = null;
            return false;
        }
        if (!options.AllowQuestionLiteral && expr.IndexOf('?') != -1)
        {
            error = "Did you forget the [query] flag? Path literals cannot contain ? (optional) operators, they must be enclosed in {} such as {name:?} or {name:**:?}";
            segment = null;
            return false;
        }

        justALiteral = true;
        segment = CreateLiteral(expr, options);
        return true;
    }

    private static bool TryParseLogicalSegment(ExpressionParsingOptions options,
        in ReadOnlyMemory<char> innerMemory,
        Stack<MatchSegment> laterSegments,
        [NotNullWhen(true)] out MatchSegment? segment,
        [NotNullWhen(false)] out string? error)
    {

        string? name = null;
        SegmentBoundary? segmentStartLogic = null;
        SegmentBoundary? segmentEndLogic = null;
        segment = null;
        
        List<StringCondition>? conditions = null;
        var inner = innerMemory.Span;
        // Enumerate segments delimited by : (ignoring \:, and breaking on \\:)
        int startsAt = 0;
        int segmentCount = 0;
        bool doubleStarFound = false;
        while (true)
        {
            int colonIndex = ExpressionParsingHelpers.FindCharNotEscaped(inner[startsAt..], ':', '\\');
            var thisPartMemory = colonIndex == -1 ? innerMemory[startsAt..] : innerMemory[startsAt..(startsAt + colonIndex)];
            bool isCondition = true;
            if (segmentCount == 0)
            {
                isCondition = ExpressionParsingHelpers.GetGlobChars(thisPartMemory.Span) != ExpressionParsingHelpers.GlobChars.None;
                if (!isCondition && thisPartMemory.Length > 0)
                {
                    name = thisPartMemory.ToString();
                    if (!ExpressionParsingHelpers.ValidateSegmentName(name, inner, out error))
                    {
                        return false;
                    }
                }
            }

            if (isCondition)
            {
                if (!TryParseConditionOrSegment(options, colonIndex == -1, thisPartMemory, inner,  ref segmentStartLogic, ref segmentEndLogic, ref conditions, ref doubleStarFound, laterSegments, out error))
                {
                    return false;
                }
            }
            segmentCount++;
            if (colonIndex == -1)
            {
                break; // We're done
            }
            startsAt += colonIndex + 1;
        }
        
        segmentStartLogic ??= SegmentBoundary.DefaultStart;
        segmentEndLogic ??= SegmentBoundary.DefaultEnd;
        
        // TODO: Throw error on * or **, since they change nothing
        // if (!doubleStarFound && !options.CaptureSlashesByDefault && !segmentStartLogic.Value.MatchesEntireSegment)
        // {
        //     conditions ??= new List<StringCondition>();
        //     // exclude '/' from chars
        //     // But what if starts(/) or eq(/) or ends(/) is used? This is clumsy
        //     conditions.Add(StringCondition.ExcludeSlashes);
        // }
        //

        segment = new MatchSegment(name, segmentStartLogic.Value, segmentEndLogic.Value, conditions);

        if (segmentEndLogic.Value.AsEndSegmentReliesOnSubsequentSegmentBoundary && laterSegments.Count > 0)
        {
            var next = laterSegments.Peek();
            // if (next.IsOptional)
            // {
            //     error = $"The segment '{inner.ToString()}' cannot be matched deterministically since it precedes an optional segment. Add an until() condition or put a literal between them.";
            //     return false;
            // }
            if (!next.StartsOn.SupportsScanning)
            {
                error = $"The segment '{segment}' cannot be matched deterministically since it precedes non searchable segment '{next}'";
                return false;
            }
        }

        error = null;
        return true;
    }



    private static bool TryParseConditionOrSegment(ExpressionParsingOptions options,
        bool isFinalCondition,
        in ReadOnlyMemory<char> conditionMemory,
        in ReadOnlySpan<char> segmentText,
        ref SegmentBoundary? segmentStartLogic,
        ref SegmentBoundary? segmentEndLogic,
        ref List<StringCondition>? conditions,
        ref bool doubleStarFound,
        Stack<MatchSegment> laterSegments,
        [NotNullWhen(false)] out string? error)
    {
        error = null;
        var conditionSpan = conditionMemory.Span;
        var globChars = ExpressionParsingHelpers.GetGlobChars(conditionSpan);
        var makeOptional = (globChars & ExpressionParsingHelpers.GlobChars.Optional) ==
                           ExpressionParsingHelpers.GlobChars.Optional
                           || conditionSpan.Is("optional");
        if ((globChars & ExpressionParsingHelpers.GlobChars.DoubleStar) ==
            ExpressionParsingHelpers.GlobChars.DoubleStar)
        {
            doubleStarFound = true;
        }
        if (makeOptional)
        {
            segmentStartLogic ??= SegmentBoundary.DefaultStart;
            segmentStartLogic = segmentStartLogic.Value.SetOptional(true);
        }

        // We ignore the glob chars, they don't constrain behavior any.
        if (globChars != ExpressionParsingHelpers.GlobChars.None
            || conditionSpan.Is("optional"))
        {
            return true;
        }

        if (!ExpressionParsingHelpers.TryParseCondition(conditionMemory, out var functionNameMemory, out var args,
                out error))
        {
            return false;
        }

        var functionName = functionNameMemory.ToString() ?? throw new InvalidOperationException("Unreachable code");

        
        var conditionConsumed = false;
        if (args is { Count: 1 })
        {
            var optional = segmentStartLogic?.IsOptional ?? false;
            if (SegmentBoundary.TryCreate(functionName, options.OrdinalIgnoreCase, optional, args[0].Span, out var sb))
            {
                if (segmentStartLogic is { MatchesEntireSegment: true })
                {
                    error =
                        $"The segment {segmentText.ToString()} already uses equals(), this cannot be combined with other conditions.";
                    return false;
                }
                if (sb.Value.IsEndingBoundary)
                {
                    if (segmentEndLogic is { HasDefaultEndWhen: false })
                    {
                        error = $"The segment {segmentText.ToString()} has conflicting end conditions; do not mix equals, length, ends-with, and suffix conditions";
                        return false;
                    }
                    segmentEndLogic = sb;
                    conditionConsumed = true;
                }
                else
                {
                    if (segmentStartLogic is { HasDefaultStartWhen: false })
                    {
                        error = $"The segment {segmentText.ToString()} has multiple start conditions; do not mix starts_with, after, and equals conditions";
                        return false;
                    }
                    segmentStartLogic = sb;
                    conditionConsumed = true;
                }
                
            } 
        }
        if (!conditionConsumed)
        {
            conditions ??= new List<StringCondition>();
            if (!TryParseCondition(options, conditions, functionName, args, out var condition, out error))
            {
                //TODO: add more context to error
                return false;
            }
            
            conditions.Add(condition.Value);
        }
        return true;
    }

    private static bool TryParseCondition(ExpressionParsingOptions options, 
        List<StringCondition> conditions, string functionName, 
        List<ReadOnlyMemory<char>>? args, [NotNullWhen(true)]out StringCondition? condition, [NotNullWhen(false)] out string? error)
    {
        var c = StringCondition.TryParse(out var cError, functionName, args, options.OrdinalIgnoreCase);
        if (c == null)
        {
            condition = null;
            error = cError ?? throw new InvalidOperationException("Unreachable code");
            return false;
        }
        condition = c.Value;
        error = null;
        return true;
    }


    private static MatchSegment CreateLiteral(ReadOnlySpan<char> literal, ExpressionParsingOptions options)
    {
        return new MatchSegment(null, 
            SegmentBoundary.Literal(literal, options.OrdinalIgnoreCase), 
            SegmentBoundary.LiteralEnd, null);
    }
}