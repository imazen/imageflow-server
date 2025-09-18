using System;
using System.Linq;
using Imazen.Abstractions.Resulting;
using Imazen.Routing.Matching;
using Xunit;
using Imazen.Routing.Matching.Templating;
using System.Collections.Generic;
using FluentAssertions;
using sly.parser.generator;
using sly.lexer;
using sly.parser;
using Microsoft.Extensions.FileSystemGlobbing;
using System.Reflection;

namespace Imazen.Tests.Routing.Matching;

public class MatchExpressionParsingTests
{
    private readonly ITestOutputHelper _testOutputHelper;
    public MatchExpressionParsingTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    private static string Stringify(IDictionary<string, string> pairs) => "{" + string.Join(",", pairs.Select(x => $"{x.Key}='{x.Value}'")) + "}";

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
    [InlineData("{name:image-ext-supported}", false)]
    [InlineData("{name:image-ext-supported:?}", false)]
    [InlineData("{name:chars([a-zA-Z0-9_\\-])}", true)]
    [InlineData("{name:chars([a-zA-Z0-9_\\-]):?}", true)]
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
            _testOutputHelper.WriteLine($"Expression '{exp}' should parse successfully, but got error: {error}");
            Assert.True(result, $"Expression '{exp}' should parse successfully, but got error: {error}");
            Assert.NotNull(matchExpression);
        }
        else
        {
            _testOutputHelper.WriteLine($"Expression '{exp}' should not parse successfully, but it did.");
            Assert.False(result, $"Expression '{exp}' should not parse successfully, but it did.");
            Assert.Null(matchExpression);
            Assert.NotNull(error);
        }
    }

    [Theory]
    // Test cases for bookended optional segments - should fail parsing
    [InlineData("/path/{optional:?}/more", "disallowed")]
    [InlineData("/files/{id:?}/download", "disallowed")]
    [InlineData("/api/v1/{version:?}/users", "disallowed")]
    [InlineData("/data/{item:?}/process", "disallowed")]
    // Valid cases that should not fail
    [InlineData("/path/{optional:?}", null)] // No following literal
    [InlineData("{optional:?}/more", null)] // Previous literal doesn't end with /
    [InlineData("/path/{optional:?}more", null)] // Following literal doesn't start with /
    [InlineData("/path/{required}/more", null)] // Not optional
    public void TestBookendedOptionalSegmentValidation(string matchExpression, string? expectedError)
    {
        var multiMatcher = MultiValueMatcher.TryParse(matchExpression.AsMemory(), out var result, out var error);
        if (multiMatcher && expectedError != null)
        {
            _testOutputHelper.WriteLine($"Expression '{matchExpression}' should not parse successfully, but it did.");
            Assert.False(multiMatcher, $"Expression '{matchExpression}' should not parse successfully, but it did.");
            Assert.Null(result);
            Assert.NotNull(error);
        }
        if (!multiMatcher && expectedError == null)
        {
            _testOutputHelper.WriteLine($"Expression '{matchExpression}' should parse successfully, but it did not.");
            Assert.True(multiMatcher, $"Expression '{matchExpression}' should parse successfully, but it did not.");
            Assert.NotNull(result);
            Assert.Null(error);
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
                Assert.True(result.AllFlags.UnclaimedCount == 0);
            }
            if (!ok) Assert.Fail($"Expression {exp} parsed, but should have failed");
        }
    }

}
