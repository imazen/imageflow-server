using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Imazen.Abstractions.HttpStrings;
using Imazen.Routing.HttpAbstractions;
using Microsoft.Extensions.Primitives;
using Imazen.Routing.Matching.Templating;
using Imazen.Routing.Requests;
using Imazen.Routing.RoutingExpressions;

namespace Imazen.Routing.Matching;

public record MultiValueMatcher(
    MatchExpression? PathMatcher,
    IReadOnlyDictionary<string, MatchExpression>? QueryValueMatchers,
    ParsingOptions ParsingOptions,
    ExpressionFlags? UnusedFlags)
{
    public Dictionary<string, MatcherVariableInfo>? GetMatcherVariableInfo()
    {
        return PathMatcher?.GetMatcherVariableInfo().Concat(
            QueryValueMatchers?.SelectMany(x => x.Value.GetMatcherVariableInfo()) ?? [])
            .ToDictionary(p => p.Key, p => p.Value);
    }
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


    public static MultiValueMatcher Parse(string expressionWithFlags)
    {
        return TryParse(expressionWithFlags.AsMemory(), out var result, out var error)
            ? result
            : throw new ArgumentException(error);
    }
    // as string
    public static bool TryParse(string expressionWithFlags,
        [NotNullWhen(true)] out MultiValueMatcher? result, [NotNullWhen(false)] out string? error)
    {
        return TryParse(expressionWithFlags.AsMemory(), out result, out error);
    }
    public static bool TryParse(ReadOnlyMemory<char> expressionWithFlags,
        [NotNullWhen(true)] out MultiValueMatcher? result, [NotNullWhen(false)] out string? error)
    {
        if (!ExpressionFlags.TryParseFromEnd(expressionWithFlags, out var expression, out var flags, out error,
            ExpressionFlags.LowercaseDash()))
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

    internal MultiMatchResult Match(in MatchingContext context, MutableRequest request, in string? headerAsQuery = null)
    {
        var path = request.Path.AsMemory();
        var query = request.ReadOnlyQueryWrapper;
        ReadOnlyMemory<char>? rawQuery  = null;
        ReadOnlyMemory<char>? rawPathAndQuery = null;
        ReadOnlyMemory<char>? sorted = null;
        var headers = headerAsQuery != null ? QueryHelpers.ParseQuery(headerAsQuery) : null;
        return Match(context, path, query, headers, ref rawQuery, ref rawPathAndQuery, ref sorted);
    }
    
    internal MultiMatchResult Match(in MatchingContext context, in string pathAndQuery, in string? headerAsQuery = null)
    {
        var pathEnd = pathAndQuery.IndexOf('?') > -1 ? pathAndQuery.IndexOf('?') : pathAndQuery.Length;
        var path = ParsingOptions.IgnorePath ? "" : pathAndQuery[..pathEnd];
        var query = QueryHelpers.ParseQuery(pathAndQuery[pathEnd..]);
        var queryWrapper = new DictionaryQueryWrapper(query);
        ReadOnlyMemory<char>? rawQuery  = pathAndQuery[pathEnd..].AsMemory();
        ReadOnlyMemory<char>? rawPathAndQuery = pathAndQuery.AsMemory();
        ReadOnlyMemory<char>? sorted = null;
        var headers = headerAsQuery != null ? QueryHelpers.ParseQuery(headerAsQuery) : null;
        return Match(context, path.AsMemory(), queryWrapper, headers, ref rawQuery, ref rawPathAndQuery, ref sorted);
    }

    internal MultiMatchResult Match(in MatchingContext context, in ReadOnlyMemory<char> path,
        IReadOnlyQueryWrapper? query, IDictionary<string, StringValues>? headers,
        ref ReadOnlyMemory<char>? rawQuery, ref ReadOnlyMemory<char>? rawPathAndQuery, ref ReadOnlyMemory<char>? pathAndSortedQuery)
    {
        if (ParsingOptions.RequireAcceptWebP && 
            (headers == null
             || !headers.TryGetValue(HttpHeaderNames.Accept, out var accept)
             || !accept.Any(s => s != null && s.Contains("image/webp"))))
        {
            return new MultiMatchResult()
                { Success = false, Error = "'image/webp' was not found in the HTTP Accept header string" };
        }

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
                Captures = pathMatchResult?.Captures?.ToDictionary(x => x.Name, x => x.Value.ToString())
            };
        }

        List<string>? matchedKeysList = null;
        Dictionary<string, string>? captures =
            pathMatchResult?.Captures?.ToDictionary(x => x.Name, x => x.Value.ToString());

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
                captures ??= new Dictionary<string, string>();
                if (valueMatchResult.Value.Captures != null)
                {
                    foreach (var capture in valueMatchResult.Value.Captures)
                    {
                        // Will fail on dupe names.
                        captures.Add(capture.Name, capture.Value.ToString());
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