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

namespace Imazen.Tests.Routing.Matching;

public class MatchExpressionTests
{
    private readonly ITestOutputHelper _testOutputHelper;
    public MatchExpressionTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }
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
                (true, "/{name}/{country:?:suffix(/)}{place}{:eq(/):optional}", new[] { "/hi/usa/" }), //"/hi/usa",
                (true, "/{name}/{a}{b:prefix(/):?}{:eq(/):optional}", new[] { "/hi/usa", "/hi/usa/", "/hi/usa/co", "/hi/usa/co/" }),
                (true, "/{name}/{country:len(3)}", new[] { "/hi/usa" }),
                (true, "/{name}/{country:len(3)}/{state:len(2)}", new[] { "/hi/usa/CO" }),
                (false, "/{name}/{country:length(3)}", new[] { "/hi/usa2" }),
                (false, "/{name}/{country:length(3)}", new[] { "/hi/usa/" }),
                (true, "/images/{seo_string_ignored}/{sku:guid}/{image_id:integer-range(0,1111111)}{width:integer:prefix(_):optional}.{format:equals(jpg|png|gif)}", new[] { "/images/seo-string/12345678-1234-1234-1234-123456789012/12678_300.jpg", "/images/seo-string/12345678-1234-1234-1234-123456789012/12678.png" })
            };
            foreach (var (success, exp, inputs) in theories)
            {
                data.Add("", success, exp, inputs);
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

        if (!MultiValueMatcher.TryParse(exp, out var matcher, out var error))
        {
            Assert.Fail($"Invalid expression '{exp}': {error}");
        }

        var failures = new List<string>();

        foreach (var v in inputs)
        {
            var result = matcher.Match(c, v);
            if (result.Success == expectedSuccess) continue;
            
            var message = result.Success
                ? $"False positive! Expression '{exp}' should not have matched '{v}'. Error: {result.Error}."
                : $"Incorrect failure! Expression '{exp}' failed to match '{v}'. Error: {result.Error}";
            failures.Add(message);
            _testOutputHelper.WriteLine(message);
        }
        if (failures.Count > 0)
        {
            var message = $"{failures.Count} of {inputs.Length} inputs failed to meet expectations";
            failures.Add(message);
            _testOutputHelper.WriteLine(message);
        }

        if (failures.Count > 0)
        {
            Assert.Fail(string.Join("\n", failures));
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
                // It is not intuitive that this works - place is not optional!
                (true, "/{name}/{country:?:suffix(/)}{place}{:eq(/):optional}", "/hi/usa/" , "name=hi&country=usa&place=", null),
                //While not strictly wrong - the segmentation uses suffix(/), and optionality eval is weird - this should probably be stopped
                //(true, "/{name}/{country:?:suffix(/)}{place}{:eq(/):optional}", "/hi/usa" , "name=hi&country=usa&place=", null),
                (true, "/{name}/{country:len(3)}/{state:len(2)}", "/hi/usa/CO", "name=hi&country=usa&state=CO", null),
                (true, "{country:len(3)}{state:len(2)}", "USACO", "country=USA&state=CO", null),
                (true, "/images/{seo_string_ignored}/{sku:guid}/{image_id:integer-range(0,1111111)}{width:integer:prefix(_):optional}.{format:equals(jpg|png|gif)}", "/images/seo-string/12345678-1234-1234-1234-123456789012/12678_300.jpg", "seo_string_ignored=seo-string&sku=12345678-1234-1234-1234-123456789012&image_id=12678&width=300&format=jpg", null),
                (true, "/images/{seo_string_ignored}/{sku:guid}/{image_id:integer-range(0,1111111)}{width:integer:prefix(_):optional}.{format:equals(jpg|png|gif)}", "/images/seo-string/12345678-1234-1234-1234-123456789012/12678.jpg", "seo_string_ignored=seo-string&sku=12345678-1234-1234-1234-123456789012&image_id=12678&format=jpg", null),
                (true, "/{dir}/{file}.{ext}", "/path/file.txt", "dir=path&file=file&ext=txt", null),
                (true, "/{dir}/{file:**}.{ext}", "/path/to/nested/dir/file.txt", "dir=path&file=to/nested/dir/file&ext=txt", null)
            };
            
            foreach (var (success, expr, input, captures, keys) in theories)
            {
                data.Add("", success, expr, input, captures, keys);
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
        var matcher = MultiValueMatcher.Parse(expr);
        var result = matcher.Match(DefaultMatchingContext, input);

        if (result.Success != expectedSuccess)
        {
            var message = result.Success
                ? $"False positive! Expression '{expr}' should NOT have matched '{input}'."
                : $"Incorrect failure! Expression '{expr}' failed to match '{input}'. Error: {result.Error}";
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
            _testOutputHelper.WriteLine("Expected values: " + Stringify(expectedPairs) + "\n Actual: " + Stringify(actualPairs));
            actualPairs.Should().BeEquivalentTo(expectedPairs);
        }

        if (excessKeys != null)
        {
            var expectedExcessKeys = excessKeys.Split(',');
            Assert.Equal(expectedExcessKeys, result.ExcessQueryKeys);
        }
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
}
