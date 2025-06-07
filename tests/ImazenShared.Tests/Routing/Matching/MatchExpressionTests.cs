using System;
using System.Linq;
using Imazen.Abstractions.Resulting;
using Imazen.Routing.Matching;
using Xunit;
using Imazen.Routing.Parsing;
using System.Collections.Generic;
using FluentAssertions;
using sly.parser.generator;
using sly.lexer;
using sly.parser;

namespace Imazen.Tests.Routing.Matching;

public class MatchExpressionTests
{
    public static TheoryData<string, bool, string, string[]> TestAllData
    {
        get
        {
            var data = new TheoryData<string, bool, string, string[]>();
            var theories = new List<(bool, string, string[])>
            {
                (true, "/{name}/{country}{:(/):?}", new[] { "/hi/usa", "/hi/usa/" }),
                (true, "/{name}/{country:ends(/)}", new[] { "/hi/usa/" }),
                (true, "{:int}", new[] { "-1300" }),
                (false, "{:uint}", new[] { "-1300" }),
                (true, "{:int:range(-1000,1000)}", new[] { "-1000", "1000" }),
                (false, "{:int:range(-1000,1000)}", new[] { "-1001", "1001" }),
                (true, "/{name}/{country:suffix(/)}", new[] { "/hi/usa/" }),
                (true, "/{name}/{country}{:eq(/):optional}", new[] { "/hi/usa", "/hi/usa/" }),
                (true, "/{name}/{country:len(3)}", new[] { "/hi/usa" }),
                (true, "/{name}/{country:len(3)}/{state:len(2)}", new[] { "/hi/usa/CO" }),
                (false, "/{name}/{country:length(3)}", new[] { "/hi/usa2" }),
                (false, "/{name}/{country:length(3)}", new[] { "/hi/usa/" }),
                (true, "/images/{seo_string_ignored}/{sku:guid}/{image_id:integer-range(0,1111111)}{width:integer:prefix(_):optional}.{format:equals(jpg|png|gif)}", new[] { "/images/seo-string/12345678-1234-1234-1234-123456789012/12678_300.jpg", "/images/seo-string/12345678-1234-1234-1234-123456789012/12678.png" })
            };
            foreach (var (success, exp, inputs) in theories)
            {
                data.Add("Old", success, exp, inputs);
                data.Add("AST", success, exp, inputs);
            }
            return data;
        }
    }
    
    [Theory]
    [MemberData(nameof(TestAllData))]
    public void TestAll(string t, bool ok, string exp, params string[] inputs)
    {
        var parserType = t;
        var expectedSuccess = ok;
        var c = DefaultMatchingContext;
        foreach (var v in inputs)
        {
            if (parserType == "Old")
            {
                var matcher = MultiValueMatcher.Parse(exp.AsMemory());
                var result = matcher.Match(c, v);
                if (result.Success == expectedSuccess) continue;
                
                var message = result.Success
                    ? $"[{parserType}] False positive! Expression '{exp}' should not have matched '{v}'. Error: {result.Error}."
                    : $"[{parserType}] Incorrect failure! Expression '{exp}' failed to match '{v}'. Error: {result.Error}";
                Assert.Fail(message);
            }
            else // AST
            {
                var result = EvaluateWithAst(exp, v, c);
                if (result.Success == expectedSuccess) continue;
                
                var message = result.Success
                    ? $"[{parserType}] False positive! Expression '{exp}' should not have matched '{v}'. Error: {result.Error}."
                    : $"[{parserType}] Incorrect failure! Expression '{exp}' failed to match '{v}'. Error: {result.Error}";
                Assert.Fail(message);
            }
        }
    }
    
    
    public static TheoryData<string, bool, string, string, string?, string?> TestCapturesData
    {
        get
        {
            var data = new TheoryData<string, bool, string, string, string?, string?>();
            var theories = new List<(bool, string, string, string?, string?)>
            {
                (true,"/{name:ends(y)}", "/cody", "name=cody", null),
                // ints
                (true, "{a:int}", "123", "a=123", null),
                (true, "{a:int}", "-123", "a=-123", null),
                (true, "{a:int}", "0", "a=0", null),
                (true, "{a:u64}?k={v}", "123?k=h&a=b", "a=123&v=h", "a"),
                (true, "{a:u64}", "0", "a=0", null),
                (false, "{:u64}", "-123", null, null),
                (true, "/{name}/{country}{:eq(/):?}", "/hi/usa", "name=hi&country=usa", null),
                (true, "/{name}/{country}{:eq(/):?}",  "/hi/usa/", "name=hi&country=usa", null),
                (true, "/{name}/{country:len(3)}", "/hi/usa", "name=hi&country=usa", null),
                (true, "/{name}/{country:len(3)}/{state:len(2)}", "/hi/usa/CO", "name=hi&country=usa&state=CO", null),
                (true, "{country:len(3)}{state:len(2)}", "USACO", "country=USA&state=CO", null),
                (true, "/images/{seo_string_ignored}/{sku:guid}/{image_id:integer-range(0,1111111)}{width:integer:prefix(_):optional}.{format:equals(jpg|png|gif)}", "/images/seo-string/12345678-1234-1234-1234-123456789012/12678_300.jpg", "seo_string_ignored=seo-string&sku=12345678-1234-1234-1234-123456789012&image_id=12678&width=300&format=jpg", null),
                (true, "/images/{seo_string_ignored}/{sku:guid}/{image_id:integer-range(0,1111111)}{width:integer:prefix(_):optional}.{format:equals(jpg|png|gif)}", "/images/seo-string/12345678-1234-1234-1234-123456789012/12678.jpg", "seo_string_ignored=seo-string&sku=12345678-1234-1234-1234-123456789012&image_id=12678&format=jpg", null),
                (true, "/{dir}/{file}.{ext}", "/path/file.txt", "dir=path&file=file&ext=txt", null),
                (true, "/{dir}/{file:**}.{ext}", "/path/to/nested/dir/file.txt", "dir=path&file=to/nested/dir/file&ext=txt", null)
            };
            
            foreach (var (success, expr, input, captures, keys) in theories)
            {
                data.Add("Old", success, expr, input, captures, keys);
                data.Add("AST", success, expr, input, captures, keys);
            }
            return data;
        }
    }
    [Theory]
    [MemberData(nameof(TestCapturesData))]
    public void TestCaptures(string t, bool ok, string expr, string input, string? expectedCapturesString, string? excessKeys = null)
    {
        var parserType = t;
        var expectedSuccess = ok;
        if (parserType == "Old")
        {
            var matcher = MultiValueMatcher.Parse(expr.AsMemory());
            var result = matcher.Match(DefaultMatchingContext, input);

            if (result.Success != expectedSuccess)
            {
                var message = result.Success
                    ? $"[{parserType}] False positive! Expression '{expr}' should NOT have matched '{input}'."
                    : $"[{parserType}] Incorrect failure! Expression '{expr}' failed to match '{input}'. Error: {result.Error}";
                Assert.Fail(message);
            }
            if (!result.Success) return;

            Dictionary<string, string>? expectedPairs =
                expectedCapturesString is null
                    ? null
                    : Imazen.Routing.Helpers.PathHelpers.ParseQuery(expectedCapturesString)!
                        .ToDictionary(x => x.Key, x => x.Value.ToString());

            if (expectedPairs != null)
            {
                var actualPairs = result!.Captures!
                    .ToDictionary(x => x.Key, x => x.Value.ToString());
                actualPairs.Should().BeEquivalentTo(expectedPairs);
            }

            if (excessKeys != null)
            {
                var expectedExcessKeys = excessKeys.Split(',');
                Assert.Equal(expectedExcessKeys, result.ExcessQueryKeys);
            }
        }
        else // AST
        {
            var result = EvaluateWithAst(expr, input, DefaultMatchingContext);

            if (result.Success != expectedSuccess)
            {
                var message = result.Success
                    ? $"[{parserType}] False positive! Expression '{expr}' should NOT have matched '{input}'."
                    : $"[{parserType}] Incorrect failure! Expression '{expr}' failed to match '{input}'. Error: {result.Error}";
                Assert.Fail(message);
            }

            if (!result.Success) return;

            Dictionary<string, string>? expectedPairs =
                expectedCapturesString is null
                    ? null
                    : Imazen.Routing.Helpers.PathHelpers.ParseQuery(expectedCapturesString)!
                        .ToDictionary(x => x.Key, x => x.Value.ToString());

            if (expectedPairs != null)
            {
                (result.Captures ?? new Dictionary<string, string>()).Should().BeEquivalentTo(expectedPairs,
                    because: $"[{parserType}] captures for '{expr}' on '{input}' should match.");
            }
            else
            {
                Assert.True(result.Captures == null || result.Captures.Count == 0,
                    $"[{parserType}] captures should be empty when no captures expected.");
            }
        }
    }
    [Theory]
    [InlineData("{name:starts(foo):ends(bar)?}", false)]
    [InlineData("{name:starts(foo):ends(bar)}", true)]
    [InlineData("{name:starts(foo):?}", true)]
    [InlineData("{name:prefix(foo):suffix(bar)}", true)]
    [InlineData("prefix(foo){name}suffix(bar)", true)]
    [InlineData("{name:len(5):alpha()}", true)]
    [InlineData("{name:alpha():length(5,10)}", true)]
    [InlineData("{name:len(5)}", true)]
    [InlineData("{name:equals(foo):equals(bar)}", false)]
    [InlineData("{name:equals(foo|bar)}", true)]
    [InlineData("{name:starts(foo)}suffix(bar)", true)] //suffix(bar) will be seen as a literal
    [InlineData("{name:starts(foo)}/suffix(bar)", true)]
    [InlineData("{name:starts(foo)}:ends(baz)suffix(bar)", true)]
    [InlineData("{?}", true)]
    [InlineData("{*}", true)]
    [InlineData("{:?}", true)]
    [InlineData("{name:?}", true)]
    [InlineData("{name:int32}", true)]
    [InlineData("{name:int32()}", true)]
    [InlineData("{name:starts(foo}:ends(bar)}", false)]
    [InlineData("{name:starts(foo):ends(bar)}{:alpha()}", true)]
    [InlineData("{name:starts(foo):ends(bar)}/{:alpha()}", true)]
    [InlineData("{name:starts(foo)}{:ends(bar):alpha()}", false)]
    [InlineData("{name:prefix(foo):?}", true)]
    [InlineData("{name:suffix(bar)}", true)]
    [InlineData("{name:suffix(bar):?}", true)]
    [InlineData("{name:ends(bar):?}", true)]
    [InlineData("{name:contains(foo)}", true)]
    [InlineData("{name:contains(foo):?}", true)]
    [InlineData("{name:contains-i(foo)}", true)]
    [InlineData("{name:contains-i(foo):?}", true)]
    [InlineData("{name:equals(foo):?}", true)]
    [InlineData("{name:equals-i(foo)}", true)]
    [InlineData("{name:equals-i(foo):?}", true)]
    [InlineData("{name:starts-i(foo)}", true)]
    [InlineData("{name:starts-i(foo):?}", true)]
    [InlineData("{name:ends-i(bar)}", true)]
    [InlineData("{name:ends-i(bar):?}", true)]
    [InlineData("{name:len(5,10)}", true)]
    [InlineData("{name:len(5,10):?}", true)]
    [InlineData("{name:length(,5)}", true)]
    [InlineData("{name:length(5,)}", true)]
    [InlineData("{name:length(5)}", true)]
    [InlineData("{name:length(5):?}", true)]
    [InlineData("{name:length(5,10)}", true)]
    [InlineData("{name:length(5,10):?}", true)]
    [InlineData("{name:alpha}", true)]
    [InlineData("{name:alpha:?}", true)]
    [InlineData("{name:alpha-lower}", true)]
    [InlineData("{name:alpha-lower:?}", true)]
    [InlineData("{name:alpha-upper}", true)]
    [InlineData("{name:alpha-upper:?}", true)]
    [InlineData("{name:alphanumeric}", true)]
    [InlineData("{name:alphanumeric:?}", true)]
    [InlineData("{name:hex}", true)]
    [InlineData("{name:hex:?}", true)]
    [InlineData("{name:int64}", true)]
    [InlineData("{name:int64:?}", true)]
    [InlineData("{name:guid}", true)]
    [InlineData("{name:guid:?}", true)]
    [InlineData("{name:equals(foo|bar|baz)}", true)]
    [InlineData("{name:equals(foo|bar|baz):?}", true)]
    [InlineData("{name:equals-i(foo|bar|baz)}", true)]
    [InlineData("{name:equals-i(foo|bar|baz):?}", true)]
    [InlineData("{name:starts(foo|bar|baz)}", true)]
    [InlineData("{name:starts(foo|bar|baz):?}", true)]
    [InlineData("{name:starts-i(foo|bar|baz)}", true)]
    [InlineData("{name:starts-i(foo|bar|baz):?}", true)]
    [InlineData("{name:ends(foo|bar|baz)}", true)]
    [InlineData("{name:ends(foo|bar|baz):?}", true)]
    [InlineData("{name:ends-i(foo|bar|baz)}", true)]
    [InlineData("{name:ends-i(foo|bar|baz):?}", true)]
    [InlineData("{name:contains(foo|bar|baz)}", true)]
    [InlineData("{name:contains(foo|bar|baz):?}", true)]
    [InlineData("{name:contains-i(foo|bar|baz)}", true)]
    [InlineData("{name:contains-i(foo|bar|baz):?}", true)]
    [InlineData("{name:range(1,100)}", true)]
    [InlineData("{name:range(1,100):?}", true)]
    [InlineData("{name:image-ext-supported}", true)]
    [InlineData("{name:image-ext-supported:?}", true)]
    [InlineData("{name:allow([a-zA-Z0-9_\\-])}", true)]
    [InlineData("{name:allow([a-zA-Z0-9_\\-]):?}", true)]
    [InlineData("{name:starts-chars(3,[a-zA-Z])}", true)]
    [InlineData("{name:starts-chars(3,[a-zA-Z]):?}", true)]
    [InlineData("{name:ends([$%^])}", true)]
    [InlineData("{name:ends([$%^]):?}", true)]
    [InlineData("{name:ends-i([$%^])}", true)]
    [InlineData("{name:ends-i([$%^]):?}", true)]
    [InlineData("{name:starts([0-9])}", true)]
    [InlineData("{name:starts([0-9]):?}", true)]
    [InlineData("{name:starts-i([0-9])}", true)]
    [InlineData("{name:starts-i([0-9]):?}", true)]
    [InlineData("{name:starts([aeiouAEIOU])}", true)]
    [InlineData("{name:starts([aeiouAEIOU]):?}", true)]
    [InlineData("{name:starts-i([aeiouAEIOU])}", true)]
    [InlineData("{name:starts-i([aeiouAEIOU]):?}", true)]
    [InlineData("{name:ends([!@#$%^&*])}", true)]
    [InlineData("{name:ends([!@#$%^&*]):?}", true)]
    [InlineData("{name:ends-i([!@#$%^&*])}", true)]
    [InlineData("{name:ends-i([!@#$%^&*]):?}", true)]
    [InlineData("{name:ends([aeiouAEIOU])}", true)]
    [InlineData("{name:ends([aeiouAEIOU]):?}", true)]
    [InlineData("{name:ends-i([aeiouAEIOU])}", true)]
    [InlineData("{name:ends-i([aeiouAEIOU]):?}", true)]
    [InlineData("{name:ends-i([aeiouAEIOU]|[a-z]):?}", false)]
    // Ensure that they won't parse if we use any aliases or conditions as names
    [InlineData("{int}", false)]
    [InlineData("{uint}", false)]
    [InlineData("{alpha}", false)]
    [InlineData("{alphanumeric}", false)]
    [InlineData("{hex}", false)]
    [InlineData("{guid}", false)]
    [InlineData("{:guid}", true)]
    [InlineData("{:int}", true)]
    [InlineData("{:u32}", true)]
    [InlineData("{:u64}", true)]
    [InlineData("{:int32}", true)]
    [InlineData("{:int64}", true)]
    [InlineData("{:alpha}", true)]
    [InlineData("{:alpha-lower}", true)]
    [InlineData("{:alpha-upper}", true)]
    [InlineData("{:alphanumeric}", true)]
    [InlineData("{:i32}", true)]
    [InlineData("{:i64}", true)]
    [InlineData("{:(literal)}", true)]
    [InlineData("{:starts-with-i(hi)}", true)]
    [InlineData("{:starts-with(hi)}", true)]
    [InlineData("{:ends-with-i(hi)}", true)]
    [InlineData("{:ends-with(hi)}", true)]
    public void TestSimpleMatchExpressionParsing(string exp, bool ok)
    {
        var context = ExpressionParsingOptions.ParseComplete(exp.AsMemory(), out var remainingExpr);
        var result = MatchExpression.TryParse(context, remainingExpr, out var matchExpression, out var error);

        if (ok)
        {
            Assert.True(result, $"Expression '{exp}' should parse successfully, but got error: {error}");
            Assert.NotNull(matchExpression);
        }
        else
        {
            Assert.False(result, $"Expression '{exp}' should not parse successfully, but it did.");
            Assert.Null(matchExpression);
            Assert.NotNull(error);
        }
    }
    
    //Test MultiValueMatcher parsing

    [Theory]
    [InlineData("{path}?key={value}", true)]
    [InlineData("{path}?key={value}&key2={value2}[require-accept-webp]", true)]
    [InlineData("{path}?key={value}&key2={value2}[import-accept-header]", true)]
    [InlineData("{path}?key={value}&key2={value2}[require-accept-avif]", true)]
    [InlineData("{path}?key={value}&key2={value2}[require-accept-jxl]", true)]
    [InlineData("{path}?key={value}&key2={value2}[raw]", true)]
    public void TestMultiValueMatcherParsing(string exp, bool ok)
    {
        if (ok != MultiValueMatcher.TryParse(exp.AsMemory(), out var result, out var error))
        {
            if (ok)
            {
                Assert.Null(error);
                Assert.NotNull(result);
                Assert.True(result.UnusedFlags == null || result.UnusedFlags.Flags.Count == 0);
            }
            if (!ok) Assert.Fail($"Expression {exp} parsed, but should have failed");
        }
    }
    
    
    private static readonly MatchingContext DefaultMatchingContext = MatchingContext.Default;

    private static readonly Lazy<Imazen.Routing.Parsing.ImazenRoutingParser> ParserHost = new(() => new ImazenRoutingParser());
    private static readonly Lazy<Parser<ImazenRoutingToken, IAstNode>> AstParser = new(() =>
    {
        var builder = new ParserBuilder<ImazenRoutingToken, IAstNode>();
        var buildResult = builder.BuildParser(ParserHost.Value, ParserType.EBNF_LL_RECURSIVE_DESCENT, "root");
        if (buildResult.IsError)
        {
            var allBuildErrors = string.Join("; ", buildResult.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"AST Parser build failed: {allBuildErrors}");
        }
        return buildResult.Result;
    });
    
    private (bool Success, Dictionary<string, string>? Captures, string? Error) EvaluateWithAst(
        string expressionString,
        string inputString,
        MatchingContext matchingContext)
    {
        var flagParseSuccess = ExpressionFlags.TryParseFromEnd(expressionString.AsMemory(),
            out var expressionWithoutFlags, out var astFlagStrings, out var flagError);
        if (!flagParseSuccess)
        {
            return (false, null, $"Flag Parser error: {flagError}");
        }
        
        var astParsingOptions = new Imazen.Routing.Matching.ParsingOptions();
        
        var astParser = AstParser.Value;
        
        // 3. Parse the expression string (without flags) to get the AST
        // var astParseResult = UnifiedExpressionParser.TryParse(expressionWithoutFlags.ToString(), ParseMode.Matching);
        var astParseResult = astParser.Parse(expressionWithoutFlags.ToString());

        if (astParseResult.IsError || (astParseResult.Errors != null && astParseResult.Errors.Any()) || astParseResult.Result == null)
        {
            // Combine all parse errors for a more complete diagnostic message
            string allParseErrors = string.Join("; ", astParseResult.Errors?.Select(e => e.ErrorMessage) ?? new List<string>());
            if (string.IsNullOrEmpty(allParseErrors) && astParseResult.IsError) allParseErrors = "Unknown parsing error."; // Fallback if Errors list is empty but IsError is true
            return (false, null, $"AST Parser error: {allParseErrors}");
        }

        var astRootNode = astParseResult.Result as Expression;
        if (astRootNode == null)
        {
            return (false, null, "AST parsing succeeded but result is not an Expression node.");
        }

        var astEvaluator = new MatcherAstEvaluator(astRootNode, matchingContext, astParsingOptions);
        
        var astMatchResult = astEvaluator.Match(inputString.AsMemory());

        Dictionary<string, string>? astCaptures = null;
        if (astMatchResult.Captures != null)
        {
            astCaptures = astMatchResult.Captures.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
        }

        return (astMatchResult.Success, astCaptures, astMatchResult.Error);
    }
    

}