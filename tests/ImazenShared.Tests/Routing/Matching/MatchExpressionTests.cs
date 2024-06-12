using Imazen.Routing.Matching;
using Xunit;
namespace Imazen.Common.Tests.Routing.Matching;

public class MatchExpressionTests
{
    private static MatchingContext CaseSensitive = new MatchingContext
    {
        OrdinalIgnoreCase = false,
        SupportedImageExtensions = [],
    };
    private static MatchingContext CaseInsensitive = new MatchingContext
    {
        OrdinalIgnoreCase = true,
        SupportedImageExtensions = [],
    };
    [Theory]
    [InlineData(true, true, "/hi")]
    [InlineData(false, true, "/Hi")]
    [InlineData(true, false, "/Hi")]
    public void TestCaseSensitivity(bool isMatch, bool caseSensitive, string path)
    {
        var c = caseSensitive ? CaseSensitive : CaseInsensitive;
        var expr = MatchExpression.Parse(c, "/hi");
        Assert.Equal(isMatch, expr.IsMatch(c, path));
    }
    
    [Theory]
    [InlineData(true, "/{name}/{country}{:(/):?}", "/hi/usa", "/hi/usa/")]
    [InlineData(true, "/{name}/{country:ends(/)}", "/hi/usa/")]
    [InlineData(true, "{int}", "-1300")]
    [InlineData(false, "{uint}", "-1300")]
    [InlineData(true, "{int:range(-1000,1000)}", "-1000", "1000")]
    [InlineData(false, "{int:range(-1000,1000)}", "-1001", "1001")]
    
    [InlineData(true, "/{name}/{country:suffix(/)}", "/hi/usa/")]
    [InlineData(true, "/{name}/{country}{:eq(/):optional}", "/hi/usa", "/hi/usa/")]
    [InlineData(true, "/{name}/{country:len(3)}", "/hi/usa")]
    [InlineData(true, "/{name}/{country:len(3)}/{state:len(2)}", "/hi/usa/CO")]
    [InlineData(false, "/{name}/{country:length(3)}", "/hi/usa2")]
    [InlineData(false, "/{name}/{country:length(3)}", "/hi/usa/")]
    [InlineData(true, "/images/{seo_string_ignored}/{sku:guid}/{image_id:integer-range(0,1111111)}{width:integer:prefix(_):optional}.{format:equals(jpg|png|gif)}"
        , "/images/seo-string/12345678-1234-1234-1234-123456789012/12678_300.jpg", "/images/seo-string/12345678-1234-1234-1234-123456789012/12678.png")]
    
    public void TestAll(bool s, string expr, params string[] inputs)
    {
        var caseSensitive = expr.Contains("(i)");
        expr = expr.Replace("(i)", "");
        var c = caseSensitive ? CaseSensitive : CaseInsensitive;
        var me = MatchExpression.Parse(c, expr);
        foreach (var path in inputs)
        {
            var matched = me.TryMatchVerbose(c, path.AsMemory(), out var result, out var error);
            if (matched && !s)
            {
                Assert.Fail($"False positive! Expression {expr} should not have matched {path}! False positive.");
            }
            if (!matched && s)
            {
                Assert.Fail($"Expression {expr} incorrectly failed to match {path} with error {error}");
            }
        }
    }
    [Theory]
    [InlineData("/{name:ends-with(y)}", "/cody", "name=cody")]
    // ints
    [InlineData("{:int}", "123", "int=123")]
    [InlineData("{:int}", "-123", "int=-123")]
    [InlineData("{:int}", "0", "int=0")]
    [InlineData("{:u64}", "123", "u64=123")]
    [InlineData("{:u64}", "0", "u64=0")]
    [InlineData("{:u64}", "-123", null)]
    [InlineData("/{name}/{country}{:(/):?}", "/hi/usa", "name=hi&country=usa")]
    [InlineData("/{name}/{country}{:(/):?}",  "/hi/usa/", "name=hi&country=usa")]
    [InlineData("/{name}/{country}{:eq(/):optional}", "/hi/usa", "name=hi&country=usa")]
    [InlineData("/{name}/{country:len(3)}", "/hi/usa", "name=hi&country=usa")]
    [InlineData("/{name}/{country:len(3)}/{state:len(2)}", "/hi/usa/CO", "name=hi&country=usa&state=CO")]
    [InlineData("{country:len(3)}{state:len(2)}", "USACO", "country=USA&state=CO")]
    [InlineData("/images/{seo_string_ignored}/{sku:guid}/{image_id:integer-range(0,1111111)}{width:integer:prefix(_):optional}.{format:equals(jpg|png|gif)}"
        , "/images/seo-string/12345678-1234-1234-1234-123456789012/12678_300.jpg", "seo_string_ignored=seo-string&sku=12345678-1234-1234-1234-123456789012&image_id=12678&width=300&format=jpg")]
    [InlineData("/images/{seo_string_ignored}/{sku:guid}/{image_id:integer-range(0,1111111)}{width:integer:prefix(_):optional}.{format:equals(jpg|png|gif)}"
        , "/images/seo-string/12345678-1234-1234-1234-123456789012/12678.jpg", "seo_string_ignored=seo-string&sku=12345678-1234-1234-1234-123456789012&image_id=12678&format=jpg")]
    [InlineData("/{dir}/{file}.{ext}", "/path/to/file.txt", "dir=path&file=to/file&ext=txt")]
    [InlineData("/{dir}/{file}.{ext}", "/path/to/nested/dir/file.txt", "dir=path&file=to/nested/dir/file&ext=txt")]
    public void TestCaptures(string expr, string input, string? expectedCaptures)
    {
        var caseSensitive = expr.Contains("(i)");
        expr = expr.Replace("(i)", "");
        var c = caseSensitive ? CaseSensitive : CaseInsensitive;
        var me = MatchExpression.Parse(c, expr);
        var path = input;
        var matched = me.TryMatchVerbose(c, path.AsMemory(), out var result, out var error);
        if (!matched && expectedCaptures != null)
        {
            Assert.Fail($"Expression {expr} incorrectly failed to match {path} with error {error}");
        }
        if (matched && expectedCaptures == null)
        {
            var captureString = result!.Value.Captures == null ? "null" : string.Join("&", result!.Value.Captures.Select(x => $"{x.Name}={x.Value}"));
            Assert.Fail($"False positive! Expression {expr} should not have matched {path} with captures {captureString}! False positive.");
        }
        var expectedPairs = Imazen.Routing.Helpers.PathHelpers.ParseQuery(expectedCaptures)!
            .ToDictionary(x => x.Key, x => x.Value.ToString());
        
        var actualPairs = result!.Value.Captures!
            .ToDictionary(x => x.Name, x => x.Value.ToString());
        
        Assert.Equal(expectedPairs, actualPairs);
        
    }
    [Theory]
    [InlineData("{name:starts-with(foo):ends-with(bar)?}", false)]
    [InlineData("{name:starts-with(foo):ends-with(bar)}", true)]
    [InlineData("{name:starts-with(foo):?}", true)]
    [InlineData("{name:prefix(foo):suffix(bar)}", true)]
    [InlineData("prefix(foo){name}suffix(bar)", true)]
    [InlineData("{name:len(5):alpha()}", true)]
    [InlineData("{name:alpha():length(5,10)}", true)]
    [InlineData("{name:len(5)}", true)]
    [InlineData("{name:equals(foo):equals(bar)}", false)]
    [InlineData("{name:equals(foo|bar)}", true)]
    [InlineData("{name:starts-with(foo)}suffix(bar)", true)] //suffix(bar) will be seen as a literal
    [InlineData("{name:starts-with(foo)}/suffix(bar)", true)]
    [InlineData("{name:starts-with(foo)}:ends-with(baz)suffix(bar)", true)]
    [InlineData("{?}", true)]
    [InlineData("{*}", true)]
    [InlineData("{:?}", true)]
    [InlineData("{name:?}", true)]
    [InlineData("{name:int32}", true)]
    [InlineData("{name:int32()}", true)]
    [InlineData("{name:starts-with(foo}:ends-with(bar)}", false)]
    [InlineData("{name:starts-with(foo):ends-with(bar)}{:alpha()}", true)]
    [InlineData("{name:starts-with(foo):ends-with(bar)}/{:alpha()}", true)]
    [InlineData("{name:starts-with(foo)}{:ends-with(bar):alpha()}", false)]
    [InlineData("{name:prefix(foo):?}", true)]
    [InlineData("{name:suffix(bar)}", true)]
    [InlineData("{name:suffix(bar):?}", true)]
    [InlineData("{name:ends-with(bar):?}", true)]
    [InlineData("{name:contains(foo)}", true)]
    [InlineData("{name:contains(foo):?}", true)]
    [InlineData("{name:contains-i(foo)}", true)]
    [InlineData("{name:contains-i(foo):?}", true)]
    [InlineData("{name:equals(foo):?}", true)]
    [InlineData("{name:equals-i(foo)}", true)]
    [InlineData("{name:equals-i(foo):?}", true)]
    [InlineData("{name:starts-with-i(foo)}", true)]
    [InlineData("{name:starts-with-i(foo):?}", true)]
    [InlineData("{name:ends-with-i(bar)}", true)]
    [InlineData("{name:ends-with-i(bar):?}", true)]
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
    [InlineData("{name:starts-with(foo|bar|baz)}", true)]
    [InlineData("{name:starts-with(foo|bar|baz):?}", true)]
    [InlineData("{name:starts-with-i(foo|bar|baz)}", true)]
    [InlineData("{name:starts-with-i(foo|bar|baz):?}", true)]
    [InlineData("{name:ends-with(foo|bar|baz)}", true)]
    [InlineData("{name:ends-with(foo|bar|baz):?}", true)]
    [InlineData("{name:ends-with-i(foo|bar|baz)}", true)]
    [InlineData("{name:ends-with-i(foo|bar|baz):?}", true)]
    [InlineData("{name:contains(foo|bar|baz)}", true)]
    [InlineData("{name:contains(foo|bar|baz):?}", true)]
    [InlineData("{name:contains-i(foo|bar|baz)}", true)]
    [InlineData("{name:contains-i(foo|bar|baz):?}", true)]
    [InlineData("{name:range(1,100)}", true)]
    [InlineData("{name:range(1,100):?}", true)]
    [InlineData("{name:image-ext-supported}", true)]
    [InlineData("{name:image-ext-supported:?}", true)]
    [InlineData("{name:allowed-chars([a-zA-Z0-9_\\-])}", true)]
    [InlineData("{name:allowed-chars([a-zA-Z0-9_\\-]):?}", true)]
    [InlineData("{name:starts-with-chars(3,[a-zA-Z])}", true)]
    [InlineData("{name:starts-with-chars(3,[a-zA-Z]):?}", true)]
    [InlineData("{name:ends-with([$%^])}", true)]
    [InlineData("{name:ends-with([$%^]):?}", true)]
    [InlineData("{name:ends-with-i([$%^])}", true)]
    [InlineData("{name:ends-with-i([$%^]):?}", true)]
    [InlineData("{name:starts-with([0-9])}", true)]
    [InlineData("{name:starts-with([0-9]):?}", true)]
    [InlineData("{name:starts-with-i([0-9])}", true)]
    [InlineData("{name:starts-with-i([0-9]):?}", true)]
    [InlineData("{name:starts-with([aeiouAEIOU])}", true)]
    [InlineData("{name:starts-with([aeiouAEIOU]):?}", true)]
    [InlineData("{name:starts-with-i([aeiouAEIOU])}", true)]
    [InlineData("{name:starts-with-i([aeiouAEIOU]):?}", true)]
    [InlineData("{name:ends-with([!@#$%^&*])}", true)]
    [InlineData("{name:ends-with([!@#$%^&*]):?}", true)]
    [InlineData("{name:ends-with-i([!@#$%^&*])}", true)]
    [InlineData("{name:ends-with-i([!@#$%^&*]):?}", true)]
    [InlineData("{name:ends-with([aeiouAEIOU])}", true)]
    [InlineData("{name:ends-with([aeiouAEIOU]):?}", true)]
    [InlineData("{name:ends-with-i([aeiouAEIOU])}", true)]
    [InlineData("{name:ends-with-i([aeiouAEIOU]):?}", true)]
    [InlineData("{name:ends-with-i([aeiouAEIOU]|[a-z]):?}", false)]
    // Ensure that they won't parse if we use any aliases or conditions as names
    [InlineData("{int}", false)]
    [InlineData("{uint}", false)]
    [InlineData("{alpha}", false)]
    [InlineData("{alphanumeric}", false)]
    [InlineData("{hex}", false)]
    [InlineData("{guid}", false)]
    public void TestMatchExpressionParsing(string expression, bool shouldParse)
    {
        var context = MatchingContext.DefaultCaseInsensitive;
        var result = MatchExpression.TryParse(context, expression, out var matchExpression, out var error);

        if (shouldParse)
        {
            Assert.True(result, $"Expression '{expression}' should parse successfully, but got error: {error}");
            Assert.NotNull(matchExpression);
        }
        else
        {
            Assert.False(result, $"Expression '{expression}' should not parse successfully, but it did.");
            Assert.Null(matchExpression);
            Assert.NotNull(error);
        }
    }
}