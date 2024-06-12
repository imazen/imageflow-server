using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Imazen.Routing.Matching;

// TODO: split into data used during runtime and during parsing..
public record MatchingContext
{
    public bool OrdinalIgnoreCase { get; init; }
    public required IReadOnlyCollection<string> SupportedImageExtensions { get; init; }
    
    internal static MatchingContext DefaultCaseInsensitive => new()
    {
        OrdinalIgnoreCase = true,
        SupportedImageExtensions = new []{"jpg", "jpeg", "png", "gif", "webp"}
    };
    internal static MatchingContext DefaultCaseSensitive => new()
    {
        OrdinalIgnoreCase = false,
        SupportedImageExtensions = new []{"jpg", "jpeg", "png", "gif", "webp"}
    };

    /// <summary>
    /// If true, all segments will capture the / character by default. If false, segments must specify {:**} to capture slashes.
    /// </summary>
    public bool CaptureSlashesByDefault { get; init; }
}

public partial record class MatchExpression
{
    private MatchExpression(MatchSegment[] segments)
    {
        Segments = segments;
    }

    private MatchSegment[] Segments;
    
    public int SegmentCount => Segments.Length;
    
    private static bool TryCreate(IReadOnlyCollection<MatchSegment> segments, [NotNullWhen(true)] out MatchExpression? result, [NotNullWhen(false)]out string? error)
    {
        if (segments.Count == 0)
        {
            result = null;
            error = "Zero segments found in expression";
            return false;
        }

        result = new MatchExpression(segments.ToArray());
        error = null;
        return true;
    }
    
    
#if NET8_0_OR_GREATER
    [GeneratedRegex(@"^(([^{]+)|((?<!\\)\{.*(?<!\\)\}))+$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex SplitSections();
    #else
    
    private static readonly Regex SplitSectionsVar = 
        new(@"^(([^*{]+)|((?<!\\)\{.*(?<!\\)\}))+$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50));
    private static Regex SplitSections() => SplitSectionsVar;
    #endif
    public static MatchExpression Parse(MatchingContext context, string expression)
    {
        if (!TryParse(context, expression, out var result, out var error))
        {
            throw new ArgumentException(error, nameof(expression));
        }
        return result!;
    }

    private static IEnumerable<ReadOnlyMemory<char>>SplitExpressionSections(ReadOnlyMemory<char> input)
    {
        int lastOpen = -1;
        int consumed = 0;
        while (true)
        {
            if (lastOpen == -1)
            {
                lastOpen = ExpressionParsingHelpers.FindCharNotEscaped(input.Span[consumed..], '{', '\\');
                if (lastOpen != -1)
                {
                    lastOpen += consumed;
                    // Return the literal before the open {
                    if (lastOpen > consumed)
                    {
                        yield return input[consumed..lastOpen];
                    }
                    consumed = lastOpen + 1;
                }
                else
                {
                    // The rest of the string is a literal
                    if (consumed < input.Length)
                    {
                        yield return input[consumed..];
                    }
                    yield break;
                }
            }
            else
            {
                // We have an open { pending
                var close = ExpressionParsingHelpers.FindCharNotEscaped(input.Span[consumed..], '}', '\\');
                if (close != -1)
                {
                    close += consumed;
                    // return the {segment}
                    yield return input[lastOpen..(close + 1)];
                    consumed = close + 1;
                    lastOpen = -1;
                }
                else
                {
                    // The rest of the string is a literal - a dangling one!
                    if (consumed < input.Length)
                    {
                        yield return input.Slice(consumed);
                    }
                    yield break;
                }
            }
        }
    }
    
    
    public static bool TryParse(MatchingContext context, string expression,
        [NotNullWhen(true)] out MatchExpression? result, 
        [NotNullWhen(false)]out string? error)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Match expression cannot be empty";
            result = null;
            return false;
        }
        // enumerate the segments in expression using SplitSections. 
        // The entire regex should match. 
        // If it doesn't, return false and set error to the first unmatched character.
        // If it does, create a MatchSegment for each match, and add it to the result.
        // Work right-to-left
        
        var matches = SplitExpressionSections(expression.AsMemory()).ToArray();
        var segments = new Stack<MatchSegment>();
        for (int i = matches.Length - 1; i >= 0; i--)
        {
            var segment = matches[i];
            if (segment.Length == 0) throw new InvalidOperationException($"SplitSections returned an empty segment. {matches}");
            if (!MatchSegment.TryParseSegmentExpression(context, segment, segments, out var parsedSegment, out error))
            {
                result = null;
                return false;
            }
            segments.Push(parsedSegment.Value);
        }

        return TryCreate(segments, out result, out error);
    }
    public readonly record struct MatchExpressionCapture(string Name, ReadOnlyMemory<char> Value);
    public readonly record struct MatchExpressionSuccess(IReadOnlyList<MatchExpressionCapture>? Captures);

    public bool IsMatch(in MatchingContext context, in ReadOnlyMemory<char> input)
    {
        return TryMatch(context, input, out _, out _, out _);
    }
    public bool IsMatch(in MatchingContext context, string input)
    {
        return TryMatch(context, input.AsMemory(), out _, out _, out _);
    }

    public bool TryMatchVerbose(in MatchingContext context, in ReadOnlyMemory<char> input,
        [NotNullWhen(true)] out MatchExpressionSuccess? result,
        [NotNullWhen(false)] out string? error)
    {
        if (!TryMatch(context, input, out result, out error, out var ix))
        {
            MatchSegment? segment = ix >= 0 && ix < Segments.Length ? Segments[ix.Value] : null;
            error = $"{error}. Failing segment[{ix}]: {segment}";
            return false;
        }
        return true;
    }
    
    public MatchExpressionSuccess MatchOrThrow(in MatchingContext context, in ReadOnlyMemory<char> input)
    {
        var matched = this.TryMatchVerbose(context, input, out var result, out var error);
        if (!matched)
        {
            throw new ArgumentException($"Expression {this} incorrectly failed to match {input} with error {error}");
        }
        return result!.Value;
    }
    
    public Dictionary<string,string> CaptureDictOrThrow(in MatchingContext context, string input)
    {
        var match = MatchOrThrow(context, input.AsMemory());
        return match.Captures!.ToDictionary(x => x.Name, x => x.Value.ToString());
    }
    
    public bool TryMatch(in MatchingContext context, in ReadOnlyMemory<char> input, [NotNullWhen(true)] out MatchExpressionSuccess? result,
        [NotNullWhen(false)] out string? error, [NotNullWhen(false)] out int? failingSegmentIndex)
    {
        // We scan with SegmentBoundary to establish
        // A) the start and end of each segment's var capture
        // B) the start and end of each segment's capture
        // C) if the segment (when optional) is present
        
        // Consecutive optional segments or a glob followed by an optional segment are not allowed.
        // At least not yet.
        
        // Once we have the segment boundaries, we can use the segment's conditions to validate the capture.
        var inputSpan = input.Span;
        List<MatchExpressionCapture>? captures = null;
        var charactersConsumed = 0;
        var remainingInput = inputSpan;
        var openSegmentIndex = -1;
        var openSegmentAbsoluteStart = -1;
        var openSegmentAbsoluteEnd = -1;
        var currentSegment = 0;
        while (true)
        {
            var boundaryStarts = -1;
            var boundaryFinishes = -1;
            var foundBoundaryOrEnd = false;
            SegmentBoundary foundBoundary = default;
            var closingBoundary = false;
            // No more segments to try?
            if (currentSegment >= Segments.Length)
            { 
                if (openSegmentIndex != -1)
                {
                    // We still have an open segment, so we close it and capture it.
                    boundaryStarts = boundaryFinishes = inputSpan.Length;
                    foundBoundaryOrEnd = true;
                    foundBoundary = default;
                    closingBoundary = true;
                }else if (remainingInput.Length == 0)
                {
                    // We ran out of segments AND input. Success!
                    result = new MatchExpressionSuccess(captures);
                    error = null;
                    failingSegmentIndex = null;
                    return true;
                }
                else
                {
                    result = null;
                    error = "The input was not fully consumed by the match expression";
                    failingSegmentIndex = Segments.Length - 1;
                    return false;
                }
            }
            else
            {
                // If there's an open segment and it's the same as the currentSegment, use the EndsOn
                var searchingStart = openSegmentIndex != currentSegment;
                closingBoundary = !searchingStart;
                var searchSegment =
                    searchingStart ? Segments[currentSegment].StartsOn : Segments[currentSegment].EndsOn;
                var startingFresh = (openSegmentIndex == -1);
                if (!searchingStart && openSegmentIndex == currentSegment)
                {
                    // Check for null-op end conditions
                    if (searchSegment.AsEndSegmentReliesOnStartSegment)
                    {
                        // The start segment must have been equals or a literal
                        boundaryStarts = boundaryFinishes = charactersConsumed;
                        foundBoundaryOrEnd = true;
                    } else if (searchSegment.AsEndSegmentReliesOnSubsequentSegmentBoundary)
                    {
                        // Move on to the next segment (or past the last segment, which triggers a match)
                        currentSegment++;
                        continue;
                    }
                }
                if (!foundBoundaryOrEnd && !startingFresh && !searchSegment.SupportsScanning)
                {
                    error = $"The segment cannot cannot be scanned for";
                    failingSegmentIndex = currentSegment;
                    result = null;
                    return false;
                }
                if (!foundBoundaryOrEnd && startingFresh && !searchSegment.SupportsMatching)
                {
                    error = $"The segment cannot be matched for";
                    failingSegmentIndex = currentSegment;
                    result = null;
                    return false;
                }
                
                // Relying on these to throw exceptions if the constructed expression can
                // not be matched deterministically.
                var s = -1;
                var f = -1;
                if (!foundBoundaryOrEnd)
                {
                    var searchResult = (startingFresh
                        ? searchSegment.TryMatch(remainingInput, out s, out f)
                        : searchSegment.TryScan(remainingInput, out s, out f));
                    boundaryStarts = s == -1 ? -1 : charactersConsumed + s;
                    boundaryFinishes = f == -1 ? -1 : charactersConsumed + f;
                    foundBoundaryOrEnd = searchResult;
                    foundBoundary = searchSegment;
                }
                if (!foundBoundaryOrEnd)
                {
                    foundBoundary = default;
                    if (Segments[currentSegment].IsOptional)
                    {
                        // We didn't find the segment, but it's optional, so we can skip it.
                        currentSegment++;
                        continue;
                    }
                    // It's mandatory, and we didn't find it.
                    result = null;
                    error = searchingStart ? "The start of the segment could not be found in the input"
                        : "The end of the segment could not be found in the input";
                    failingSegmentIndex = currentSegment;
                    return false;
                }
            }

            if (foundBoundaryOrEnd)
            {
                Debug.Assert(boundaryStarts != -1 && boundaryFinishes != -1);
                // We can get here under 3 conditions:
                // 1. We found the start of a segment and a previous segment is open
                // 2. We found the end of a segment and the current segment is open.
                // 3. We matched the start of a segment, no previous segment was open.

                // So first, we close and capture any open segment. 
                // This happens if we found the start of a segment and a previous segment is open.
                // Or if we found the end of our current segment.
                if (openSegmentIndex != -1)
                {
                    var openSegment = Segments[openSegmentIndex];
                    var variableStart = openSegment.StartsOn.IncludesMatchingTextInVariable
                        ? openSegmentAbsoluteStart
                        : openSegmentAbsoluteEnd;

                    var variableEnd = (foundBoundary != default && foundBoundary.IsEndingBoundary &&
                                       foundBoundary.IncludesMatchingTextInVariable)
                        ? boundaryFinishes
                        : boundaryStarts;
                    
                    var conditionsOk = openSegment.ConditionsMatch(context, inputSpan[variableStart..variableEnd]);
                    if (!conditionsOk)
                    {
                        // Even if the segment is optional, we refuse to match it if the conditions don't match.
                        // We could lift this restriction later
                        result = null;
                        error = $"The text did not meet the conditions of the segment";
                        failingSegmentIndex = openSegmentIndex;
                        return false;
                    }

                    if (openSegment.Name != null)
                    {
                        captures ??= new List<MatchExpressionCapture>();
                        captures.Add(new(openSegment.Name,
                            input[variableStart..variableEnd]));
                    }
                    // We consume the characters (we had a formerly open segment).
                    charactersConsumed = boundaryFinishes;
                    remainingInput = inputSpan[charactersConsumed..];
                }

                if (!closingBoundary){ 
                    openSegmentIndex = currentSegment;
                    openSegmentAbsoluteStart = boundaryStarts;
                    openSegmentAbsoluteEnd =  boundaryFinishes;
                    // TODO: handle non-consuming char case?
                    charactersConsumed = boundaryFinishes;
                    remainingInput = inputSpan[charactersConsumed..];
                    continue; // Move on to the next segment
                }
                else
                {
                    openSegmentIndex = -1;
                    openSegmentAbsoluteStart = -1;
                    openSegmentAbsoluteEnd = -1;
                    currentSegment++;
                }
                
            }
        }
    }
}

    

internal readonly record struct 
    MatchSegment(string? Name, SegmentBoundary StartsOn, SegmentBoundary EndsOn, List<StringCondition>? Conditions)
{
    override public string ToString()
    {
        if (StartsOn.MatchesEntireSegment && Name == null && EndsOn.AsEndSegmentReliesOnStartSegment)
        {
            return $"'{StartsOn}'";
        }
        var conditionsString = Conditions == null ? "" : string.Join(":", Conditions);
        if (conditionsString.Length > 0)
        {
            conditionsString = ":" + conditionsString;
        }
        return $"{Name ?? ""}:{StartsOn}:{EndsOn}{conditionsString}";
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

    internal static bool TryParseSegmentExpression(MatchingContext context, 
        ReadOnlyMemory<char> exprMemory, 
        Stack<MatchSegment> laterSegments, 
        [NotNullWhen(true)]out MatchSegment? segment, 
        [NotNullWhen(false)]out string? error)
    {
        var expr = exprMemory.Span;
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
                error = "Segment {} cannot be empty. Try {*}, {name}, {name:condition1:condition2}";
                segment = null;
                return false;
            }
            return TryParseLogicalSegment(context, innerMem, laterSegments, out segment, out error);

        }
        // it's a literal
        // Check for invalid characters like &
        if (expr.IndexOf('*') != -1 || expr.IndexOf('?') != -1)
        {
            error = "Literals cannot contain * or ? operators, they must be enclosed in {} such as {name:?} or {name:**:?}";
            segment = null;
            return false;
        }
        segment = CreateLiteral(expr, context);
        return true;
    }

    private static bool TryParseLogicalSegment(MatchingContext context,
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
                if (!TryParseConditionOrSegment(context, colonIndex == -1, thisPartMemory, inner,  ref segmentStartLogic, ref segmentEndLogic, ref conditions, laterSegments, out error))
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



    private static bool TryParseConditionOrSegment(MatchingContext context,
        bool isFinalCondition,
        in ReadOnlyMemory<char> conditionMemory,
        in ReadOnlySpan<char> segmentText,
        ref SegmentBoundary? segmentStartLogic,
        ref SegmentBoundary? segmentEndLogic,
        ref List<StringCondition>? conditions,
        Stack<MatchSegment> laterSegments,
        [NotNullWhen(false)] out string? error)
    {
        error = null;
        var conditionSpan = conditionMemory.Span;
        var globChars = ExpressionParsingHelpers.GetGlobChars(conditionSpan);
        var makeOptional = (globChars & ExpressionParsingHelpers.GlobChars.Optional) ==
                           ExpressionParsingHelpers.GlobChars.Optional
                           || conditionSpan.Is("optional");
        var hasDoubleStar = (globChars & ExpressionParsingHelpers.GlobChars.DoubleStar) ==
                            ExpressionParsingHelpers.GlobChars.DoubleStar;
        if (makeOptional)
        {
            segmentStartLogic ??= SegmentBoundary.DefaultStart;
            segmentStartLogic = segmentStartLogic.Value.SetOptional(true);
        }
        if (!hasDoubleStar && context.CaptureSlashesByDefault)
        {
            conditions ??= new List<StringCondition>();
            // exclude '/' from chars
            conditions.Add(StringCondition.ExcludeForwardSlash);
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
            if (SegmentBoundary.TryCreate(functionName, context.OrdinalIgnoreCase, optional, args[0].Span, out var sb))
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
            if (!TryParseCondition(context, conditions, functionName, args, out var condition, out error))
            {
                //TODO: add more context to error
                return false;
            }
            
            conditions.Add(condition.Value);
        }
        return true;
    }

    private static bool TryParseCondition(MatchingContext context, 
            List<StringCondition> conditions, string functionName, 
            List<ReadOnlyMemory<char>>? args, [NotNullWhen(true)]out StringCondition? condition, [NotNullWhen(false)] out string? error)
    {
        var c = StringCondition.TryParse(out var cError, functionName, args, context.OrdinalIgnoreCase);
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


    private static MatchSegment CreateLiteral(ReadOnlySpan<char> literal, MatchingContext context)
    {
        return new MatchSegment(null, 
            SegmentBoundary.Literal(literal, context.OrdinalIgnoreCase), 
            SegmentBoundary.LiteralEnd, null);
    }
}


internal readonly record struct SegmentBoundary
{
    private readonly SegmentBoundary.Flags Behavior;
    private readonly SegmentBoundary.When On;
    private readonly string? Chars;
    private readonly char Char;


    private SegmentBoundary(
        SegmentBoundary.Flags behavior,
        SegmentBoundary.When on,
        string? chars,
        char c
    )
    {
        this.Behavior = behavior;
        this.On = on;
        this.Chars = chars;
        this.Char = c;
    }
    [Flags]
    private enum SegmentBoundaryFunction
    {
        None = 0,
        Equals = 1,
        StartsWith = 2,
        IgnoreCase = 16,
        IncludeInVar = 32,
        EndingBoundary = 64,
        SegmentOptional = 128,
        FixedLength = 256
    }

    private static SegmentBoundaryFunction FromString(string name, bool useIgnoreCaseVariant, bool segmentOptional)
    {
        var fn= name switch
        {
            "equals" or "" or "eq" => SegmentBoundaryFunction.Equals | SegmentBoundaryFunction.IncludeInVar,
            "starts_with" or "starts-with" or "starts" => SegmentBoundaryFunction.StartsWith | SegmentBoundaryFunction.IncludeInVar,
            "ends_with" or "ends-with" or "ends" => SegmentBoundaryFunction.StartsWith | SegmentBoundaryFunction.IncludeInVar | SegmentBoundaryFunction.EndingBoundary,
            "prefix" => SegmentBoundaryFunction.StartsWith,
            "suffix" => SegmentBoundaryFunction.StartsWith | SegmentBoundaryFunction.EndingBoundary,
            "len" or "length" => SegmentBoundaryFunction.FixedLength | SegmentBoundaryFunction.EndingBoundary | SegmentBoundaryFunction.IncludeInVar,
            _ => SegmentBoundaryFunction.None
        };
        if (fn == SegmentBoundaryFunction.None)
        {
            return fn;
        }
        if (useIgnoreCaseVariant)
        {
            fn |= SegmentBoundaryFunction.IgnoreCase;
        }
        if (segmentOptional)
        {
            if (fn == SegmentBoundaryFunction.FixedLength)
            {
                // When a fixed length segment is optional, we don't make a end boundary for it.
                return SegmentBoundaryFunction.None;
            }
            fn |= SegmentBoundaryFunction.SegmentOptional;
        }
        return fn;
    }

    public static SegmentBoundary Literal(ReadOnlySpan<char> literal, bool ignoreCase) =>
        StringEquals(literal, ignoreCase, false);
    
    

    public static SegmentBoundary LiteralEnd = new(Flags.EndingBoundary, When.SegmentFullyMatchedByStartBoundary, null, '\0');

    public bool HasDefaultStartWhen => On == When.StartsNow;
    public static SegmentBoundary DefaultStart = new(Flags.IncludeMatchingTextInVariable, When.StartsNow, null, '\0');
    public bool HasDefaultEndWhen => On == When.InheritFromNextSegment;
    public static SegmentBoundary DefaultEnd = new(Flags.EndingBoundary, When.InheritFromNextSegment, null, '\0');
    public static SegmentBoundary EqualsEnd = new(Flags.EndingBoundary, When.SegmentFullyMatchedByStartBoundary, null, '\0');
    
    public bool IsOptional => (Behavior & Flags.SegmentOptional) == Flags.SegmentOptional;


    public bool IncludesMatchingTextInVariable =>
        (Behavior & Flags.IncludeMatchingTextInVariable) == Flags.IncludeMatchingTextInVariable;

    public bool IsEndingBoundary =>
        (Behavior & Flags.EndingBoundary) == Flags.EndingBoundary;

    public bool SupportsScanning =>
        On != When.StartsNow &&
        SupportsMatching;

    public bool SupportsMatching =>
        On != When.InheritFromNextSegment &&
        On != When.SegmentFullyMatchedByStartBoundary;

    public bool MatchesEntireSegment =>
        On == When.EqualsOrdinal || On == When.EqualsOrdinalIgnoreCase || On == When.EqualsChar;

   
    public SegmentBoundary SetOptional(bool optional)
        => new(optional ? Flags.SegmentOptional | Behavior : Behavior ^ Flags.SegmentOptional, On, Chars, Char);


    public bool AsEndSegmentReliesOnStartSegment =>
        On == When.SegmentFullyMatchedByStartBoundary;

    public bool AsEndSegmentReliesOnSubsequentSegmentBoundary =>
        On == When.InheritFromNextSegment;


    
    public static bool TryCreate(string function, bool useIgnoreCase, bool segmentOptional, ReadOnlySpan<char> arg0,
       [NotNullWhen(true)] out SegmentBoundary? result)
    {
        var fn = FromString(function, useIgnoreCase, segmentOptional);
        if (fn == SegmentBoundaryFunction.None)
        {
            result = null;
            return false;
        }
        return TryCreate(fn, arg0, out result);
    }

    private static bool TryCreate(SegmentBoundaryFunction function, ReadOnlySpan<char> arg0, out SegmentBoundary? result)
    {
        var argType = ExpressionParsingHelpers.GetArgType(arg0);
        
        if ((argType & ExpressionParsingHelpers.ArgType.String) == 0)
        {
            result = null;
            return false;
        }

        var includeInVar = (function & SegmentBoundaryFunction.IncludeInVar) == SegmentBoundaryFunction.IncludeInVar;
        var ignoreCase = (function & SegmentBoundaryFunction.IgnoreCase) == SegmentBoundaryFunction.IgnoreCase;
        var startsWith = (function & SegmentBoundaryFunction.StartsWith) == SegmentBoundaryFunction.StartsWith;
        var equals = (function & SegmentBoundaryFunction.Equals) == SegmentBoundaryFunction.Equals;
        var segmentOptional = (function & SegmentBoundaryFunction.SegmentOptional) == SegmentBoundaryFunction.SegmentOptional;
        var endingBoundary = (function & SegmentBoundaryFunction.EndingBoundary) == SegmentBoundaryFunction.EndingBoundary;
        var segmentFixedLength = (function & SegmentBoundaryFunction.FixedLength) == SegmentBoundaryFunction.FixedLength;
        if (startsWith)
        {
            result = StartWith(arg0, ignoreCase, includeInVar, endingBoundary).SetOptional(segmentOptional);
            return true;
        }
        if (equals)
        {
            if (endingBoundary) throw new InvalidOperationException("Equals cannot be an ending boundary");
            result = StringEquals(arg0, ignoreCase, includeInVar).SetOptional(segmentOptional);
            return true;
        }
        if (segmentFixedLength)
        {
            if (segmentOptional)
            {
                // We don't support optional fixed length segments at this time.
                result = null;
                return false;
            }
            // len requires a number
            if ((argType & ExpressionParsingHelpers.ArgType.UnsignedInteger) > 0)
            {
                //parse the number into char
                var len = int.Parse(arg0.ToString());
                result = FixedLengthEnd(len);
                return true;
            }
            result = null;
            return false;
        }
        throw new InvalidOperationException("Unreachable code");
    }
        
    private static SegmentBoundary StartWith(ReadOnlySpan<char> asSpan, bool ordinalIgnoreCase, bool includeInVar,bool endingBoundary)
    {
        var flags = includeInVar ? Flags.IncludeMatchingTextInVariable : Flags.None;
        if (endingBoundary)
        {
            flags |= Flags.EndingBoundary;
        }
        
        if (asSpan.Length == 1 &&
            (!ordinalIgnoreCase || ExpressionParsingHelpers.IsCommonCaseInsensitiveChar(asSpan[0])))
        {
            return new(flags,
                When.AtChar, null, asSpan[0]);
        }

        return new(flags,
            ordinalIgnoreCase ? When.AtStringIgnoreCase : When.AtString, asSpan.ToString(), '\0');
    }
    
    private static SegmentBoundary StringEquals(ReadOnlySpan<char> asSpan, bool ordinalIgnoreCase, bool includeInVar)
    {
        if (asSpan.Length == 1 &&
            (!ordinalIgnoreCase || ExpressionParsingHelpers.IsCommonCaseInsensitiveChar(asSpan[0])))
        {
            return new(includeInVar ? Flags.IncludeMatchingTextInVariable : Flags.None,
                When.EqualsChar, null, asSpan[0]);
        }

        return new(includeInVar ? Flags.IncludeMatchingTextInVariable : Flags.None,
            ordinalIgnoreCase ? When.EqualsOrdinalIgnoreCase : When.EqualsOrdinal, asSpan.ToString(), '\0');
    }
    private static SegmentBoundary FixedLengthEnd(int length)
    {
        if (length < 1) throw new ArgumentOutOfRangeException(nameof(length)
            , "Fixed length must be greater than 0");
        if (length > char.MaxValue) throw new ArgumentOutOfRangeException(nameof(length)
            , "Fixed length must be less than or equal to " + char.MaxValue);
        return new SegmentBoundary(Flags.IncludeMatchingTextInVariable | Flags.EndingBoundary,
            When.FixedLength
            , null, (char)length);
    }
    [Flags]
    private enum Flags : byte
    {
        None = 0,
        SegmentOptional = 1,
        IncludeMatchingTextInVariable = 4,
        EndingBoundary = 64,
    }


    private enum When : byte
    {
        /// <summary>
        /// Cannot be combined with Optional.
        /// Cannot be used for determining the end of a segment.
        /// 
        /// </summary>
        StartsNow,
        EndOfInput,
        SegmentFullyMatchedByStartBoundary,

        /// <summary>
        /// The default for ends
        /// </summary>
        InheritFromNextSegment,
        AtChar,
        AtString,
        AtStringIgnoreCase,
        EqualsOrdinal,
        EqualsChar,
        EqualsOrdinalIgnoreCase,
        FixedLength,
    }


    public bool TryMatch(ReadOnlySpan<char> text, out int start, out int end)
    {
        if (!SupportsMatching)
        {
            throw new InvalidOperationException("Cannot match a segment boundary with " + On);
        }

        start = 0;
        end = 0;
        if (On == When.EndOfInput)
        {
            return text.Length == 0;
        }

        if (On == When.StartsNow)
        {
            return true;
        }

        if (text.Length == 0) return false;
        switch (On)
        {
            case When.FixedLength:
                if (text.Length >= this.Char)
                {
                    start = 0;
                    end = this.Char;
                    return true;
                }
                return false;
            case When.AtChar or When.EqualsChar:
                if (text[0] == Char)
                {
                    start = 0;
                    end = 1;
                    return true;
                }

                return false;

            case When.AtString or When.EqualsOrdinal:
                var charSpan = Chars.AsSpan();
                if (text.StartsWith(charSpan, StringComparison.Ordinal))
                {
                    start = 0;
                    end = charSpan.Length;
                    return true;
                }

                return true;
            case When.AtStringIgnoreCase or When.EqualsOrdinalIgnoreCase:
                var charSpan2 = Chars.AsSpan();
                if (text.StartsWith(charSpan2, StringComparison.OrdinalIgnoreCase))
                {
                    start = 0;
                    end = charSpan2.Length;
                    return true;
                }

                return true;
            default:
                return false;
        }
    }

    public bool TryScan(ReadOnlySpan<char> text, out int start, out int end)
    {
        if (!SupportsScanning)
        {
            throw new InvalidOperationException("Cannot scan a segment boundary with " + On);
        }

        // Like TryMatch, but searches for the first instance of the boundary
        start = 0;
        end = 0;
        if (On == When.EndOfInput)
        {
            start = end = text.Length;
            return true;
        }

        if (text.Length == 0) return false;
        switch (On)
        {
            case When.FixedLength:
                if (text.Length >= this.Char)
                {
                    start = this.Char;
                    end = this.Char;
                    return true;
                }
                return false;
            case When.AtChar or When.EqualsChar:
                var index = text.IndexOf(Char);
                if (index == -1) return false;
                start = index;
                end = index + 1;
                return true;
            case When.AtString or When.EqualsOrdinal:
                var searchSpan = Chars.AsSpan();
                var searchIndex = text.IndexOf(searchSpan);
                if (searchIndex == -1) return false;
                start = searchIndex;
                end = searchIndex + searchSpan.Length;
                return true;
            case When.AtStringIgnoreCase or When.EqualsOrdinalIgnoreCase:
                var searchSpanIgnoreCase = Chars.AsSpan();
                var searchIndexIgnoreCase = text.IndexOf(searchSpanIgnoreCase, StringComparison.OrdinalIgnoreCase);
                if (searchIndexIgnoreCase == -1) return false;
                start = searchIndexIgnoreCase;
                end = searchIndexIgnoreCase + searchSpanIgnoreCase.Length;
                return true;
            default:
                return false;
        }


    }
    private string? MatchString => On switch
    {
        When.AtChar or When.EqualsChar => Char.ToString(),
        When.AtString or When.AtStringIgnoreCase or
            When.EqualsOrdinal or When.EqualsOrdinalIgnoreCase => Chars,
        _ => null
    };
    public override string ToString()
    {
        var isStartBoundary = Flags.EndingBoundary == (Behavior & Flags.EndingBoundary);
        var name = On switch
        {
            When.StartsNow => "now",
            When.EndOfInput => "unterminated",
            When.SegmentFullyMatchedByStartBoundary => "noop",
            When.InheritFromNextSegment => "unterminated",
            When.AtChar or When.AtString or When.AtStringIgnoreCase =>
                ((Behavior & Flags.IncludeMatchingTextInVariable) != 0)
                    ? (isStartBoundary ? "starts-with" : "ends-with")
                    : (isStartBoundary ? "prefix" : "suffix"),
            When.EqualsOrdinal or When.EqualsChar or When.EqualsOrdinalIgnoreCase => "equals",
            When.FixedLength => $"len",
            _ => throw new InvalidOperationException("Unreachable code")
        };
        var ignoreCase = On is When.AtStringIgnoreCase or When.EqualsOrdinalIgnoreCase ? "-i" : "";
        var optional = (Behavior & Flags.SegmentOptional) != 0 ? "?": "";
        if (On == When.FixedLength)
        {
            return $"{name}({(int)Char}){ignoreCase}{optional}";
        }
        if (Chars != null)
        {
            name = $"{name}({Chars}){ignoreCase}{optional}";
        }
        else if (Char != '\0')
        {
            name = $"{name}({Char}){ignoreCase}{optional}";
        }
        return $"{name}{optional}";
    }
}




