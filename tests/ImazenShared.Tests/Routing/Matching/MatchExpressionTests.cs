
using Imazen.Routing.Matching;
using FluentAssertions;

namespace Imazen.Tests.Routing.Matching;

public class MatchExpressionTests
{
    private readonly ITestOutputHelper _testOutputHelper;
    public MatchExpressionTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    private ITestOutputHelper output => _testOutputHelper;
    
    [Theory]
    [InlineData(true, "/{name}/{country}{:(/):?}", new[] { "/hi/usa", "/hi/usa/" })]
    [InlineData(true, "/{name}/{country:ends(/)}", new[] { "/hi/usa/" })]
    [InlineData(true, "{:int}", new[] { "-1300" })]
    [InlineData(false, "{:uint}", new[] { "-1300" })]
    [InlineData(true, "{:int:range(-1000,1000)}", new[] { "-1000", "1000" })]
    [InlineData(false, "{:int:range(-1000,1000)}", new[] { "-1001", "1001" })]
    [InlineData(true, "/{name}/{country:suffix(/)}", new[] { "/hi/usa/" })]
    [InlineData(true, "/{name}/{country}{:eq(/):optional}", new[] { "/hi/usa", "/hi/usa/" })]
    [InlineData(true, "/{name}/{country:?:suffix(/)}{place}{:eq(/):optional}", new[] { "/hi/usa/" })]
    [InlineData(true, "/{name}/{a}{b:prefix(/):?}{:eq(/):optional}", new[] { "/hi/usa", "/hi/usa/", "/hi/usa/co", "/hi/usa/co/" })]
    [InlineData(true, "/{name}/{country:len(3)}", new[] { "/hi/usa" })]
    [InlineData(true, "/{name}/{country:len(3)}/{state:len(2)}", new[] { "/hi/usa/CO" })]
    [InlineData(false, "/{name}/{country:length(3)}", new[] { "/hi/usa2" })]
    [InlineData(false, "/{name}/{country:length(3)}", new[] { "/hi/usa/" })]
    [InlineData(true, "/images/{seo_string_ignored}/{sku:guid}/{image_id:integer-range(0,1111111)}{width:integer:prefix(_):optional}.{format:equals(jpg|png|gif)}", new[] { "/images/seo-string/12345678-1234-1234-1234-123456789012/12678_300.jpg", "/images/seo-string/12345678-1234-1234-1234-123456789012/12678.png" })]
    public void TestAll(bool ok, string exp, params string[] inputs)
    {
        var expectedSuccess = ok;
        var c = Context;

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
                (true, "/{name}/{country}[/]", "/hi/usa", "name=hi&country=usa", null),
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
        var result = matcher.Match(Context, input);

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

    
    
    private static readonly MatchingContext Context = MatchingContext.Default;



    [MatchExpression("/{a}/{b:?}")]
    [Theory]
    [InlineData("/x", false, null)]
    [InlineData("/x/", true, "a=x&b=")] //TODO: Is this weird?
    [InlineData("/x/y", true, "a=x&b=y")]
    [InlineData("/x/y/z", true, "a=x&b=y/z")]
    public void BasicOptional(string path, bool m, string? captures)
    {
        AssertMatch.Captures(this, output, Context, path, m, captures);
    }


    [MatchExpression("/{a}{b:?:prefix(/)}")]
    [Theory]
    [InlineData("/x", true, "a=x")] // Works with prefix
    [InlineData("/x/", true, "a=x&b=")]
    [InlineData("/x/y", true, "a=x&b=y")]
    [InlineData("/x/y/z", true, "a=x&b=y/z")]
    public void OptionalWithPrefix(string path, bool m, string? captures)
    {
        AssertMatch.Captures(this, output, Context, path, m, captures);
    }

    [MatchExpression("/{a}/")]
    [Theory]
    [InlineData("//", true, "a=")] //TODO, this is WEIRD
    [InlineData("/x/", true, "a=x")]
    public void RequiredButEmpty(string path, bool m, string? captures)
    {
        AssertMatch.Captures(this, output, Context, path, m, captures);
    }
}
