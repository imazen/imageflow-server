using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Imazen.Abstractions.HttpStrings;
using Imazen.Routing.Helpers;
using Imazen.Routing.HttpAbstractions;

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
        // if (flags.Remove("capture-slashes"))
        // {
        //     defaults = defaults with { CaptureSlashesByDefault = true };
        // }
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

public record QueryParsingOptions
{
    public bool KeysOrdinalIgnoreCase { get; init; } = false;
    public bool ValuesOrdinalIgnoreCase { get; init; } = false;
    public bool AllowStarLiteral { get; init; } = false;
    //public bool QueryValuesCaptureSlashes { get; init; } = false;
    public bool ProhibitExcessQueryKeys { get; init; } = false;

    
    internal ExpressionParsingOptions ToExpressionParsingOptions()
    {
        return new()
        {
            OrdinalIgnoreCase = ValuesOrdinalIgnoreCase,
            AllowStarLiteral = AllowStarLiteral,
            AllowQuestionLiteral = true,
            //CaptureSlashesByDefault = QueryValuesCaptureSlashes
        };
    }
    public static QueryParsingOptions DefaultCaseInsensitive => new()
    {
        KeysOrdinalIgnoreCase = true,
        ValuesOrdinalIgnoreCase = true,
    };
    
    public static QueryParsingOptions DefaultCaseSensitive => new()
    {
        KeysOrdinalIgnoreCase = false,
        ValuesOrdinalIgnoreCase = false,
    };

    public static QueryParsingOptions SubtractFromFlags(List<string> flags, QueryParsingOptions defaults)
    {
        if (flags.Remove("query-ignore-case"))
        {
            defaults = defaults with { KeysOrdinalIgnoreCase = true, ValuesOrdinalIgnoreCase = true };
        }

        if (flags.Remove("query-keys-ignore-case"))
        {
            defaults = defaults with { KeysOrdinalIgnoreCase = true };
        }

        if (flags.Remove("query-values-ignore-case"))
        {
            defaults = defaults with { ValuesOrdinalIgnoreCase = true };
        }

        // if (flags.Remove("query-values-capture-slashes"))
        // {
        //     defaults = defaults with { QueryValuesCaptureSlashes = true };
        // }

        if (flags.Remove("query-prohibit-excess"))
        {
            defaults = defaults with { ProhibitExcessQueryKeys = true };
        }


        if (flags.Remove("allow-star-literal"))
        {
            defaults = defaults with { AllowStarLiteral = true };
        }

        return defaults;
    }
}

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
public record ParsingOptions
{
    /// <summary>
    /// If true, `?` will be allowed as a literal, and the query and path will be concatenated before being matched, and the combination must be completely captured.
    /// QueryParsingOptions will be ignored.
    /// </summary>
    public bool RawQueryAndPath { get; init; } = false;
    /// <summary>
    /// If true, the query keys will be sorted alphabetically before being concatenated and matched.
    /// </summary>
    public bool SortRawQueryValuesFirst { get; init; } = false;
    /// <summary>
    /// If true, matching will be done for any path. No path match expression is required.
    /// </summary>
    public bool IgnorePath { get; init; } = false;
    
    /// <summary>
    /// If no query matcher is specified, query-prohibit-excess will be respected but nothing else.
    /// 
    /// </summary>
    public QueryParsingOptions QueryParsingOptions { get; init; } = new();
    
    public PathParsingOptions PathParsingOptions { get; init; } = new();

    
    public static ParsingOptions DefaultCaseInsensitive => new()
    {
        QueryParsingOptions = QueryParsingOptions.DefaultCaseInsensitive,
        PathParsingOptions = PathParsingOptions.DefaultCaseInsensitive
    };
    public static ParsingOptions DefaultCaseSensitive => new()
    {
        QueryParsingOptions = QueryParsingOptions.DefaultCaseSensitive,
        PathParsingOptions = PathParsingOptions.DefaultCaseSensitive
    };


    public static ParsingOptions SubtractFromFlags(List<string> flags)
    {
        var context = new ParsingOptions();
        if (flags.Remove("ignore-case"))
        {
            context = context with { QueryParsingOptions = QueryParsingOptions.DefaultCaseInsensitive, PathParsingOptions = PathParsingOptions.DefaultCaseInsensitive };
        }
        if (flags.Remove("case-sensitive"))
        {
            context = context with { QueryParsingOptions = QueryParsingOptions.DefaultCaseSensitive, PathParsingOptions = PathParsingOptions.DefaultCaseSensitive };
        }
        if (flags.Remove("raw"))
        {
            context = context with { RawQueryAndPath = true };
        }
        if (flags.Remove("sort-raw-query-first"))
        {
            context = context with { SortRawQueryValuesFirst = true };
        }
        
        if (flags.Remove("ignore-path"))
        {
            context = context with { IgnorePath = true };
        }

        context = context with
        {
            QueryParsingOptions = QueryParsingOptions.SubtractFromFlags(flags, context.QueryParsingOptions)
        };
        context = context with
        {
            PathParsingOptions = PathParsingOptions.SubtractFromFlags(flags, context.PathParsingOptions)
        };
        return context;
    }
}


public record MultiValueMatcher(
    MatchExpression? PathMatcher,
    IReadOnlyDictionary<string, MatchExpression>? QueryValueMatchers,
    ParsingOptions ParsingOptions,
    ExpressionFlags? UnusedFlags)
{
    public string? GetValidationErrors()
    {
        if (ParsingOptions.RawQueryAndPath && PathMatcher == null)
        {
            return
                "No path match expression found; a path match expression is required unless you use [ignore-path], and always if you use" +
                "[raw-query-and-path]";
        }

        if (PathMatcher == null && !ParsingOptions.IgnorePath)
        {
            return "A path match expression is required unless you use [ignore-path]";
        }

        if (PathMatcher != null && ParsingOptions is { IgnorePath: true, RawQueryAndPath: true })
        {
            return
                "A path match expression is prohibited with [ignore-path] unless you use [raw-query-and-path], in which case only the query is matched.";
        }

        if (ParsingOptions.RawQueryAndPath && (ParsingOptions.QueryParsingOptions.ProhibitExcessQueryKeys))
        {
            // || ParsingOptions.QueryParsingOptions.DeleteExcessQueryKeys
            return
                "[raw-query-and-path] cannot be used with [query-prohibit-excess] or [query-delete-excess], as those only apply to structural query matching with [query]";
        }
        
        if (ParsingOptions is { IgnorePath: true, RawQueryAndPath: false } && QueryValueMatchers == null)
        {
            return "[ignore-path] cannot be used unless [raw-query-and-path] is used or a query matcher is specified";
        }
        
        return null;
    }

    public static MultiValueMatcher Parse(ReadOnlyMemory<char> expressionWithFlags)
    {
        if (!MultiValueMatcher.TryParse(expressionWithFlags, out var result, out var error))
        {
            throw new ArgumentException(error, nameof(expressionWithFlags));
        }

        return result!;
    }

    public static bool TryParse(ReadOnlyMemory<char> expressionWithFlags,
        [NotNullWhen(true)] out MultiValueMatcher? result, [NotNullWhen(false)] out string? error)
    {
        if (!ExpressionFlags.TryParseFromEnd(expressionWithFlags, out var expression, out var flags, out error))
        {
            result = null;
            return false;
        }

        var allFlags = flags ?? new List<string>();
        var context = ParsingOptions.SubtractFromFlags(allFlags);

        if (!MatchExpression.TryParseWithSmartQuery(context, expression, out var pathMatcher, out var queryMatchers,
                out error))
        {
            result = null;
            return false;
        }

        result = new MultiValueMatcher(pathMatcher, queryMatchers, context,
            new ExpressionFlags(new ReadOnlyCollection<string>(allFlags)));
        error = result.GetValidationErrors();
        if (error != null)
        {
            result = null;
            return false;
        }

        return true;
    }


    internal MultiMatchResult Match(in MatchingContext context, in string pathAndQuery)
    {
        var pathEnd = pathAndQuery.IndexOf('?') > -1 ? pathAndQuery.IndexOf('?') : pathAndQuery.Length;
        var path = ParsingOptions.IgnorePath ? "" : pathAndQuery[..pathEnd];
        var query = QueryHelpers.ParseQuery(pathAndQuery[pathEnd..]);
        var queryWrapper = new DictionaryQueryWrapper(query);
        ReadOnlyMemory<char>? rawQuery  = pathAndQuery[pathEnd..].AsMemory();
        ReadOnlyMemory<char>? rawPathAndQuery = pathAndQuery.AsMemory();
        ReadOnlyMemory<char>? sorted = null;
        return Match(context, path.AsMemory(), queryWrapper, ref rawQuery, ref rawPathAndQuery, ref sorted);
    }

    internal MultiMatchResult Match(in MatchingContext context, in ReadOnlyMemory<char> path,
        IReadOnlyQueryWrapper? query,
        ref ReadOnlyMemory<char>? rawQuery, ref ReadOnlyMemory<char>? rawPathAndQuery, ref ReadOnlyMemory<char>? pathAndSortedQuery)
    {
        var pathInput = ParsingOptions.IgnorePath ? "".AsMemory() : path;
        if (ParsingOptions.RawQueryAndPath)
        {
            if (ParsingOptions.SortRawQueryValuesFirst && query != null)
            {
                pathAndSortedQuery ??= QueryHelpers.AddQueryString(pathInput.ToString(),
                        query.Keys.OrderBy(x => x)
                            .Select(x => new KeyValuePair<string, string?>(x, query[x].ToString())))
                    .AsMemory();
                
                pathInput = pathAndSortedQuery.Value;
            }
            else if (query != null)
            {
                rawQuery ??= QueryHelpers.AddQueryString(pathInput.ToString(), query)
                    .AsMemory();
                if (rawQuery is { Length: > 0 })
                {
                    rawPathAndQuery ??= $"{pathInput}{(rawQuery.Value.Span[0] != '?' ? "?" : "")}{rawQuery.Value}"
                        .AsMemory();
                    pathInput = rawPathAndQuery.Value;
                }
            }
        }
        

        MatchExpression.MatchExpressionSuccess? pathMatchResult = null;

        if (PathMatcher != null)
        {
            if (!PathMatcher.TryMatchVerbose(context, pathInput, out pathMatchResult, out var error))
            {
                // Return early if the path doesn't match.
                return new MultiMatchResult { Success = false, Error = context.VerboseErrors ? $"Path '{pathInput}' did not match the expression '{PathMatcher}'" : "Path did not match the expression" };
            }
        }
        var prohibitExcess = ParsingOptions.QueryParsingOptions.ProhibitExcessQueryKeys;

        //If there are no query matchers, but there is input query, we (fail) if prohibitExcess
        if (QueryValueMatchers == null)
        {
            if (query is { Count: > 0 })
            {
                if (prohibitExcess)
                {
                    return new MultiMatchResult
                    {
                        Success = false, Error =
                            context.VerboseErrors
                                ? $"[query-prohibit-excess] is set and no querystring is present in the expression, but the input had querystring {query}"
                                : "Querystring was present, but is not allowed by the expression and [query-prohibit-excess] is set"
                    };
                }
            }

            //return a match with the excess query
            return new MultiMatchResult
            {
                Success = true, ExcessQueryKeys = query is { Count: > 0 } ? query.Keys.ToArray() : null, OriginalQuery = query,
                Captures = pathMatchResult?.Captures?.ToDictionary(x => x.Name, x => x.Value)
            };
        }

        List<string>? matchedKeysList = null;
        Dictionary<string, ReadOnlyMemory<char>>? captures =
            pathMatchResult?.Captures?.ToDictionary(x => x.Name, x => x.Value);

        // We have query matchers. Now, some or all may be optional, so we can't short circuit.
        foreach (var pair in QueryValueMatchers!)
        {
            string inputKey = pair.Key;
            var valueOptional = pair.Value.AllOptional;
            if (!query!.TryGetValue(pair.Key, out string? inputValue))
            {
                if (ParsingOptions.QueryParsingOptions.KeysOrdinalIgnoreCase)
                {
                    // TODO, we need a better way. The querystring interface doesn't allow for knowing if we can case-insensitive query tho 
                    var found = query!.Keys.FirstOrDefault(x =>
                        x.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                    if (found != null)
                    {
                        inputKey = found;
                        inputValue = query[found];
                    }
                }
            }

            if (inputValue == null)
            {
                if (!valueOptional)
                {
                    var error = context.VerboseErrors
                        ? $"Required query key '{pair.Key}' not found in the input. Value should match '{pair.Value}'"
                        : "The input is missing a required querystring key";
                    return new MultiMatchResult { Success = false, Error = error };
                }
            }
            else
            {
                if (!pair.Value.TryMatchVerbose(context, inputValue.AsMemory(), out var valueMatchResult,
                        out var valueError))
                {
                    var error = context.VerboseErrors
                        ? $"Query key '{pair.Key}' value '{inputValue}' does not match expression '{pair.Value}'. ${valueError}"
                        : "A query value did not match its corresponding expression";
                    return new MultiMatchResult { Success = false, Error = error };
                }

                //Combine the success captures into the result.
                captures ??= new Dictionary<string, ReadOnlyMemory<char>>();
                if (valueMatchResult.Value.Captures != null)
                {
                    foreach (var capture in valueMatchResult.Value.Captures)
                    {
                        // Will fail on dupe names.
                        captures.Add(capture.Name, capture.Value);
                    }
                }

                //Mark the input query key we processed, so we can later collect the excess.
                matchedKeysList ??= new List<string>();
                matchedKeysList.Add(inputKey);
            }

        }

        // We can subtract matchedKeys from the query keys to get the excess.
        var excessKeyNames = query == null
            ? []
            : (matchedKeysList == null
                ? query.Keys.ToArray()
                : query!.Keys.Where(x => !matchedKeysList.Contains(x)).ToArray());

        if (prohibitExcess && excessKeyNames.Length > 0)
        {
            return new MultiMatchResult
            {
                Success = false,
                Error = context.VerboseErrors
                    ? $"Excess query keys found: {string.Join(", ", excessKeyNames)}"
                    : "Excess query keys found"
            };
        }

        return new MultiMatchResult { Success = true, Captures = captures, ExcessQueryKeys = excessKeyNames, OriginalQuery = query };
    }

}

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

public record ExpressionFlags(ReadOnlyCollection<string> Flags)
{
    public static bool TryParseFromEnd(ReadOnlyMemory<char> expression,out ReadOnlyMemory<char> remainingExpression, out List<string> result, 
        [NotNullWhen(false)]
        out string? error)
    {
        var flags = new List<string>();
        var span = expression.Span;
        
        if (span.Length == 0 || span[^1] != ']')
        {
            //It's ok for there to be none
            result = flags;
            error = null;
            remainingExpression = expression;
            return true;
        }
        var startAt = span.LastIndexOf('[');
        if (startAt == -1)
        {
            result = flags;
            error = "Flags must be enclosed in [], only found closing ]";
            remainingExpression = expression;
            return false;
        }
        remainingExpression = expression[..startAt];
        var inner = expression[(startAt + 1)..^1];
        var innerSpan = inner.Span;
        while (innerSpan.Length > 0)
        {
            var commaIndex = innerSpan.IndexOf(',');
            if (commaIndex == -1)
            {
                flags.Add(inner.ToString());
                break;
            }
            flags.Add(inner[..commaIndex].ToString());
            inner = inner[(commaIndex + 1)..];
            innerSpan = inner.Span;
        }
        // validate only a-z-
        foreach (var flag in flags)
        {
            if (!flag.All(x => x == '-' || (x >= 'a' && x <= 'z')))
            {
                result = flags;
                error = $"Invalid flag '{flag}', only a-z- are allowed";
                return false;
            }
        }

        result = flags;
        error = null;
        return true;
    }
}



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
        // TODO: Now look for naming conflicts among them all
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

}

    

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
                error = "Segment {} cannot be empty. Try {*}, {name}, {name:condition1:condition2}";
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

    public string? AsCaseSensitiveLiteral =>
        this.Behavior == Flags.None ?
        On switch
        {
            When.EqualsOrdinal => Chars,
            When.EqualsChar => Char.ToString(),
            _ => null
        } : null;

    public bool IsLiteralEnd => Behavior == Flags.EndingBoundary && On == When.SegmentFullyMatchedByStartBoundary &&
                                Char == '\0' && Chars == null;
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
        var useCaseInsensitive = ordinalIgnoreCase && ExpressionParsingHelpers.HasAzOrNonAsciiLetters(asSpan);
        if (asSpan.Length == 1 &&
            !useCaseInsensitive)
        {
            return new(flags,
                When.AtChar, null, asSpan[0]);
        }

        return new(flags,
            useCaseInsensitive ? When.AtStringIgnoreCase : When.AtString, asSpan.ToString(), '\0');
    }
    
    private static SegmentBoundary StringEquals(ReadOnlySpan<char> asSpan, bool ordinalIgnoreCase, bool includeInVar)
    {
        var useCaseInsensitive = ordinalIgnoreCase && ExpressionParsingHelpers.HasAzOrNonAsciiLetters(asSpan);
        if (asSpan.Length == 1 && !useCaseInsensitive)
        {
            return new(includeInVar ? Flags.IncludeMatchingTextInVariable : Flags.None,
                When.EqualsChar, null, asSpan[0]);
        }

        return new(includeInVar ? Flags.IncludeMatchingTextInVariable : Flags.None,
            useCaseInsensitive ? When.EqualsOrdinalIgnoreCase : When.EqualsOrdinal, asSpan.ToString(), '\0');
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
            When.EndOfInput => ">",
            When.SegmentFullyMatchedByStartBoundary => "noop",
            When.InheritFromNextSegment => ">",
            When.AtChar or When.AtString or When.AtStringIgnoreCase =>
                ((Behavior & Flags.IncludeMatchingTextInVariable) != 0)
                    ? (isStartBoundary ? "starts" : "ends")
                    : (isStartBoundary ? "prefix" : "suffix"),
            When.EqualsOrdinal or When.EqualsChar or When.EqualsOrdinalIgnoreCase => "eq",
            When.FixedLength => $"len",
            _ => throw new InvalidOperationException("Unreachable code")
        };
        var ignoreCase = On is When.AtStringIgnoreCase or When.EqualsOrdinalIgnoreCase ? "-i" : "";
        var optional = (Behavior & Flags.SegmentOptional) != 0 ? "?": "";
        if (On == When.FixedLength)
        {
            return $"{name}{ignoreCase}({(int)Char}){optional}";
        }
        if (Chars != null)
        {
            name = $"{name}{ignoreCase}({Chars}){optional}";
        }
        else if (Char != '\0')
        {
            name = $"{name}{ignoreCase}({Char}){optional}";
        }
        return $"{name}{optional}";
    }
}




