using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Imazen.Routing.Helpers;
using Imazen.Routing.Matching.Templating;

namespace Imazen.Routing.Matching;

public partial record MatchExpression
{
    private MatchExpression(MatchSegment[] segments)
    {
        Segments = segments;
        AllOptional = segments.All(x => x.IsOptional);
    }

    private MatchSegment[] Segments;
    /// <summary>
    /// True if all segments are optional
    /// </summary>
    public bool AllOptional { get; init;}
    
    public int SegmentCount => Segments.Length;
    
    public override string ToString()
    {
        return string.Join("", Segments);
    }
    
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
    
    
// #if NET8_0_OR_GREATER
//     [GeneratedRegex(@"^(([^{]+)|((?<!\\)\{.*(?<!\\)\}))+$",
//         RegexOptions.CultureInvariant | RegexOptions.Singleline)]
//     private static partial Regex SplitSections();
//     #else
//     
//     private static readonly Regex SplitSectionsVar = 
//         new(@"^(([^*{]+)|((?<!\\)\{.*(?<!\\)\}))+$",
//         RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50));
//     private static Regex SplitSections() => SplitSectionsVar;
//     #endif
    public static MatchExpression Parse(ExpressionParsingOptions options, string expression)
    {
        if (!TryParse(options, expression.AsMemory(), out var result, out var error))
        {
            throw new ArgumentException(error, nameof(expression));
        }
        return result!;
    }

    private static IEnumerable<ReadOnlyMemory<char>> SplitQuerystringChars(IEnumerable<ReadOnlyMemory<char>> input)
    {
        // return items that start with {, but split the others before and after ?, &, and =
        // if the item starts with {, return it as a single item.
        foreach (var item in input)
        {
            if (item.Span[0] == '{')
            {
                yield return item;
            }
            else
            {
                // split on ? & and =
                var consumed = 0;
                while (consumed < item.Length)
                {
                    var next = item.Span[consumed..].IndexOfAny('&', '?', '=');
                    if (next == -1)
                    {
                        if (consumed < item.Length)
                        {
                            yield return item[consumed..];
                        }
                        break;
                    }

                    if (next > 0)
                    {
                        yield return item.Slice(consumed, next);
                    }
                    yield return item.Slice(consumed + next, 1);
                    consumed += next + 1;
                }
            }
        }
    }

    private static IEnumerable<List<ReadOnlyMemory<char>>> SplitBy(IEnumerable<ReadOnlyMemory<char>> input, char c)
    {
        var current = new List<ReadOnlyMemory<char>>();
        foreach (var item in input)
        {
            if (item.Length == 1 && item.Span[0] == c)
            {
                yield return current;
                current = new List<ReadOnlyMemory<char>>();
                continue;
            }
            current.Add(item);
        }
        yield return current;
        
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
    
    
    public static bool TryParse(ExpressionParsingOptions options, ReadOnlyMemory<char> expression,
        [NotNullWhen(true)] out MatchExpression? result, 
        [NotNullWhen(false)]out string? error)
    {
        if (expression.Length == 0 || expression.Span.IsWhiteSpace())
        {
            error = "Match expression cannot be empty";
            result = null;
            return false;
        }
        var matches = SplitExpressionSections(expression).ToArray();
        return TryParseInternal(options, matches, out result, out error);
    }
    private static bool TryParseInternal(ExpressionParsingOptions options, ReadOnlyMemory<char>[] expressionSections,
        [NotNullWhen(true)] out MatchExpression? result, 
        [NotNullWhen(false)]out string? error)
    {
        var segments = new Stack<MatchSegment>();
        for (int i = expressionSections.Length - 1; i >= 0; i--)
        {
            var segment = expressionSections[i];
            if (segment.Length == 0) throw new InvalidOperationException($"SplitSections returned an empty segment. {expressionSections}");
            if (!MatchSegment.TryParseSegmentExpression(options, segment, segments, out _,out var parsedSegment, out error))
            {
                result = null;
                return false;
            }
            segments.Push(parsedSegment.Value);
        }
        return TryCreate(segments, out result, out error);
    }
    public static bool TryParseWithSmartQuery(ParsingOptions parsingDefaults,  ReadOnlyMemory<char> expression,
        out MatchExpression? pathMatcherResult, 
        out Dictionary<string, MatchExpression>? queryValueMatchersResult,
        [NotNullWhen(false)]out string? error)
    {
        if (expression.Length == 0 || expression.Span.IsWhiteSpace())
        {
            error = "Match expression cannot be empty";
            pathMatcherResult = null;
            queryValueMatchersResult = null;
            return false;
        }

        var context = parsingDefaults;
        
        // enumerate the segments in expression using SplitSections. 
        // The entire regex should match. 
        // If it doesn't, return false and set error to the first unmatched character.
        // If it does, create a MatchSegment for each match, and add it to the result.
        // Work right-to-left
        
        var lexed = SplitQuerystringChars(SplitExpressionSections(expression)).ToArray();
        
        // Split those before the first ? into path segments
        var queryStartSegmentIndex = Array.FindIndex(lexed, x => x.Span.Length == 1 && x.Span[0] == '?');
        var pathSegments = lexed.Take(queryStartSegmentIndex > -1 ? queryStartSegmentIndex : lexed.Length).ToArray();
        if (!TryParseInternal(context.PathParsingOptions.ToExpressionParsingOptions(), pathSegments, out var pathMatcher, out error))
        {
            pathMatcherResult = null;
            queryValueMatchersResult = null;
            if (context.IgnorePath && pathSegments.Length == 0)
            {
                //We can ignore this error, path is optional
            }else{
                return false;
            }
        }
        // No query portion, or just a ? at the end
        if (queryStartSegmentIndex == -1 || queryStartSegmentIndex == lexed.Length - 1)
        {
            pathMatcherResult = pathMatcher;
            queryValueMatchersResult = null;
            return true;
        }
        // multiple question marks 
        var nextQuestion = Array.FindIndex(lexed, queryStartSegmentIndex + 1, x => x.Span.Length == 1 && x.Span[0] == '?');
        if (nextQuestion > -1)
        {
            error = "Multiple '?' characters found in expression. Didn't want a query string? Put the optional '?' flag inside the curly braces like {var:?} instead.";
            pathMatcherResult = null;
            queryValueMatchersResult = null;
            return false;
        }
        
        var querySegments = lexed.Skip(pathSegments.Length + 1).ToArray();
        var pairs = SplitBy(querySegments, '&').ToArray();
        var queryValueMatchers = new Dictionary<string, MatchExpression>(context.QueryParsingOptions.KeysOrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            if (pair.Count == 0) continue; // Skip empty &s
            var keyAndValue = SplitBy(pair, '=').ToArray();
            if (keyAndValue.Length != 2)
            {
                error = $"Query segment '{Stringify(pair)}' does not contain exactly one literal '=' character. Didn't want a query string? Put the optional '?' flag inside the curly braces like {{var:?}} instead.";
                pathMatcherResult = null;
                queryValueMatchersResult = null;
                return false;
            }
            var keySegments = keyAndValue[0];
            if (keySegments.Count != 1 || keySegments[0].Length == 0 || keySegments[0].Span[0] == '{')
            {
                error = $"Query key must be a plain literal, found '{Stringify(keySegments)}' in pair '{Stringify(pair)}' in expression {Stringify(querySegments)}). Didn't want a query string? Put the optional '?' flag inside the curly braces like {{var:?}} instead.";
                pathMatcherResult = null;
                queryValueMatchersResult = null;
                return false;
            }
            if (keySegments[0].Span[0] == '{')
            {
                error = $"Query keys cannot contain expressions: error in '{Stringify(pair)}'";
                pathMatcherResult = null;
                queryValueMatchersResult = null;
                return false;
            }
            var key = keySegments[0].ToString();
            
            var valueSegments = keyAndValue[1].ToArray();
            if (!TryParseInternal(context.QueryParsingOptions.ToExpressionParsingOptions(), valueSegments, out var valueMatcher, out error))
            {
                error = $"Error parsing query value expression '{Stringify(pair)}': {error}";
                pathMatcherResult = null;
                queryValueMatchersResult = null;
                return false;
            }
            queryValueMatchers[key] = valueMatcher;
        }
        pathMatcherResult = pathMatcher;
        
        queryValueMatchersResult = queryValueMatchers;
        
        var allSegments = (pathMatcher?.Segments ?? []).Concat(queryValueMatchers.Values.SelectMany(x => x.Segments))
            .Where(s => !string.IsNullOrEmpty(s.Name))
            .Select(s => s.Name).ToArray();
        
        if (allSegments.Length != allSegments.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            var duplicate = allSegments.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).First(x => x.Count() > 1).Key;
            error = $"You used {{'{duplicate}'}} more than once; each capture must use a unique variable name.";
            pathMatcherResult = null;
            queryValueMatchersResult = null;
            return false;
        }
        
        
        error = null;
        return true;
    }
    private static string Stringify(IEnumerable<ReadOnlyMemory<char>> segments)
    {
        return string.Join("", segments.Select(x => x.ToString()));
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
            if (context.VerboseErrors)
            {
                MatchSegment? segment = ix >= 0 && ix < Segments.Length ? Segments[ix.Value] : null;
                error = $"{error}. Failing segment[{ix}]: {segment}";
            }

            return false;
        }
        return true;
    }
    
    public MatchExpressionSuccess MatchOrThrow(in MatchingContext context, in ReadOnlyMemory<char> input)
    {
        var opts = context with { VerboseErrors = true };
        var matched = this.TryMatchVerbose(opts, input, out var result, out var error);
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
    
    private bool TryMatch(in MatchingContext context, in ReadOnlyMemory<char> input, [NotNullWhen(true)] out MatchExpressionSuccess? result,
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
                        error = "The text did not meet the conditions of the segment";
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

    public IReadOnlyDictionary<string, MatcherVariableInfo> GetMatcherVariableInfo()
    {
        var dict = new Dictionary<string, MatcherVariableInfo>(StringComparer.Ordinal); // Use Ordinal for variable names
        foreach (var segment in Segments)
        {
            if (!string.IsNullOrEmpty(segment.Name))
            {
                // Matcher parsing should have already caught duplicates
                if (!dict.ContainsKey(segment.Name!))
                {
                    dict.Add(segment.Name!, new MatcherVariableInfo(segment.Name!, segment.IsOptional));
                }
            }
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, MatcherVariableInfo>(dict);
    }
}