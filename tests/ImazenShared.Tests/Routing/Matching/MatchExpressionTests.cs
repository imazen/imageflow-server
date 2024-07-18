using Imazen.Abstractions.Resulting;
using Imazen.Routing.Matching;
using Xunit;

namespace Imazen.Tests.Routing.Matching;

public class MatchExpressionTests
{
    
    [Theory]
    [InlineData(true, "/{name}/{country}{:(/):?}", "/hi/usa", "/hi/usa/")]
    [InlineData(true, "/{name}/{country:ends(/)}", "/hi/usa/")]
    [InlineData(true, "{:int}", "-1300")]
    [InlineData(false, "{:uint}", "-1300")]
    [InlineData(true, "{:int:range(-1000,1000)}", "-1000", "1000")]
    [InlineData(false, "{:int:range(-1000,1000)}", "-1001", "1001")]
    
    [InlineData(true, "/{name}/{country:suffix(/)}", "/hi/usa/")]
    [InlineData(true, "/{name}/{country}{:eq(/):optional}", "/hi/usa", "/hi/usa/")]
    [InlineData(true, "/{name}/{country:len(3)}", "/hi/usa")]
    [InlineData(true, "/{name}/{country:len(3)}/{state:len(2)}", "/hi/usa/CO")]
    [InlineData(false, "/{name}/{country:length(3)}", "/hi/usa2")]
    [InlineData(false, "/{name}/{country:length(3)}", "/hi/usa/")]
    [InlineData(true, "/images/{seo_string_ignored}/{sku:guid}/{image_id:integer-range(0,1111111)}{width:integer:prefix(_):optional}.{format:equals(jpg|png|gif)}"
        , "/images/seo-string/12345678-1234-1234-1234-123456789012/12678_300.jpg", "/images/seo-string/12345678-1234-1234-1234-123456789012/12678.png")]
    
    public void TestAll(bool m, string exp, params string[] inputs)
    {
        var c = MatchingContext.Default;
        var me = MultiValueMatcher.Parse(exp.AsMemory());
        foreach (var v in inputs)
        {
            var matched = me.Match(c, v);
            if (matched.Success && !m)
            {
                Assert.Fail($"False positive! Expression {exp} should not have matched {v}! False positive.");
            }
            if (!matched.Success && m)
            {
                Assert.Fail($"Expression {exp} incorrectly failed to match {v} with error {matched.Error}");
            }
        }
    }
    [Theory]
    [InlineData(true,"/{name:ends(y)}", "/cody", "name=cody")]
    // ints
    [InlineData(true, "{a:int}", "123", "a=123")]
    [InlineData(true, "{a:int}", "-123", "a=-123")]
    [InlineData(true, "{a:int}", "0", "a=0")]
    [InlineData(true, "{a:u64}?k={v}", "123?k=h&a=b", "a=123&v=h", "a")]
    [InlineData(true, "{a:u64}", "0", "a=0")]
    [InlineData(false, "{:u64}", "-123", null)]
    [InlineData(true, "/{name}/{country}{:eq(/):?}", "/hi/usa", "name=hi&country=usa")]
    [InlineData(true, "/{name}/{country}{:(/):?}",  "/hi/usa/", "name=hi&country=usa")]
    [InlineData(true, "/{name}/{country}{:eq(/):optional}", "/hi/usa", "name=hi&country=usa")]
    [InlineData(true, "/{name}/{country:len(3)}", "/hi/usa", "name=hi&country=usa")]
    [InlineData(true, "/{name}/{country:len(3)}/{state:len(2)}", "/hi/usa/CO", "name=hi&country=usa&state=CO")]
    [InlineData(true, "{country:len(3)}{state:len(2)}", "USACO", "country=USA&state=CO")]
    [InlineData(true, "/images/{seo_string_ignored}/{sku:guid}/{image_id:integer-range(0,1111111)}{width:integer:prefix(_):optional}.{format:equals(jpg|png|gif)}"
        , "/images/seo-string/12345678-1234-1234-1234-123456789012/12678_300.jpg", "seo_string_ignored=seo-string&sku=12345678-1234-1234-1234-123456789012&image_id=12678&width=300&format=jpg")]
    [InlineData(true, "/images/{seo_string_ignored}/{sku:guid}/{image_id:integer-range(0,1111111)}{width:integer:prefix(_):optional}.{format:equals(jpg|png|gif)}"
        , "/images/seo-string/12345678-1234-1234-1234-123456789012/12678.jpg", "seo_string_ignored=seo-string&sku=12345678-1234-1234-1234-123456789012&image_id=12678&format=jpg")]
    [InlineData(true, "/{dir}/{file}.{ext}", "/path/file.txt", "dir=path&file=file&ext=txt")]
    [InlineData(true, "/{dir}/{file:**}.{ext}", "/path/to/nested/dir/file.txt", "dir=path&file=to/nested/dir/file&ext=txt")]
    public void TestCaptures(bool m, string expr, string input, string? expectedCaptures, string? excessKeys = null)
    {
        var me = MultiValueMatcher.Parse(expr.AsMemory());
        var result = me.Match(MatchingContext.Default, input);
        
        if (!result.Success && m)
        {
            Assert.Fail($"{expr} failed to match '{input}' with error: {result.Error}");
        }
        if (result.Success && !m)
        {
            var captureString = result!.Captures == null ? "null" : string.Join("&", result!.Captures.Select(x => $"{x.Key}={x.Value}"));
            Assert.Fail($"False positive! {expr} should NOT have matched {input} with captures {captureString} and excess query keys {string.Join(",",result.ExcessQueryKeys ?? [])}");
        }
        if (expectedCaptures == null)
        {
            return;
        }

        if (result.Success)
        {
            var expectedPairs = Imazen.Routing.Helpers.PathHelpers.ParseQuery(expectedCaptures)!
                .ToDictionary(x => x.Key, x => x.Value.ToString());

            var actualPairs = result!.Captures!
                .ToDictionary(x => x.Key, x => x.Value.ToString());

            Assert.Equal(expectedPairs, actualPairs);
            
            // Check excess keys
            if (excessKeys != null)
            {
                var expectedExcessKeys = excessKeys.Split(',');
                Assert.Equal(expectedExcessKeys, result.ExcessQueryKeys);
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
    
     

}