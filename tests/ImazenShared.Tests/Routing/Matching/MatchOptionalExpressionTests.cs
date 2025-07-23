
using Imazen.Routing.Matching;
using FluentAssertions;

namespace Imazen.Tests.Routing.Matching;

public class MatchOptionalExpressionTests
{
    private readonly ITestOutputHelper output;
    public MatchOptionalExpressionTests(ITestOutputHelper testOutputHelper)
    {
        output = testOutputHelper;
    }

    private static readonly MatchingContext Context = MatchingContext.Default;



    [MatchExpression("/{a}/{b:?}")]
    [Theory]
    [InlineData("/x/", true, "a=x&b=")]
    [InlineData("/x/y", true, "a=x&b=y")]
    [InlineData("/x/y/z", true, "a=x&b=y/z")]
    public void BasicOptional(string path, bool m, string? captures)
    {
        AssertMatch.Captures(this, output, Context, path, m, captures);
    }


    [MatchExpression("/{a}/{b:?}{c:?:prefix(/)}")]
    [Theory]
    [InlineData("/x/", true, "a=x&b=")]
    [InlineData("//", true, "a=&b=")]
    [InlineData("/x/y", true, "a=x&b=y")]
    [InlineData("///y", true, "a=&b=&c=y")]
    [InlineData("/x/y/z", true, "a=x&b=y&c=z")]
    public void ChainedOptional(string path, bool m, string? captures)
    {
        AssertMatch.Captures(this, output, Context, path, m, captures);
    }



    [MatchExpression("/{a}{b:?:prefix(/)}")]
    [Theory]
    [InlineData("/x", true, "a=x")]
    [InlineData("/x/", true, "a=x&b=")]
    [InlineData("/x/y", true, "a=x&b=y")]
    [InlineData("/x/y/z", true, "a=x&b=y/z")]
    public void OptionalWithPrefix(string path, bool m, string? captures)
    {
        AssertMatch.Captures(this, output, Context, path, m, captures);
    }

    [MatchExpression("/{a}/")]
    [Theory]
    [InlineData("//", true, "a=")]
    [InlineData("/x/", true, "a=x")]
    public void RequiredButEmpty(string path, bool m, string? captures)
    {
        AssertMatch.Captures(this, output, Context, path, m, captures);
    }

}
