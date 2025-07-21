using Imazen.Routing.Matching;
using Xunit;
using Imazen.Routing.RoutingExpressions;
using System;

namespace Imazen.Tests.Routing.Matching;

public class RoutingExpressionParserTests
{
    public RoutingExpressionParserTests(ITestOutputHelper output)
    {
        Output = output;
    }
    public ITestOutputHelper Output { get; }
    [Theory]
    [InlineData("{path} => /result/{path}", false, "You must specify [v1] at the end")]
    [InlineData("{path} => /result/{path} [v1]", true, null)]
    [InlineData("{path} => /result/{path} [provider=my_provider][v1]", true, null)]

        [InlineData("no_separator", false, "missing the required ' => ' separator")]
    [InlineData(" => /template", false, "Match expression cannot be empty")]
    [InlineData("/match => ", false, "Template expression cannot be empty")]
    [InlineData("{a} => {a}", false, "You must specify [v1] at the end")]
    [InlineData("{a} => {a} [v2]", false, "migrate your routing expressions from syntax version 2 to version 1")]
    [InlineData("{a} => {a} [v1][invalid]", false, "Invalid flag 'invalid'")]
    public void TestValidExpressions(string expression, bool shouldSucceed, string? errorSubstring)
    {
        var parsingOptions = RoutingParsingOptions.AnySchemeAnyFlagRequirePath;
        var success = RoutingExpressionParser.TryParse(parsingOptions, expression, out var result, out var error);
        if (shouldSucceed && !success){
            Output.WriteLine($"Expression: {expression}");
            Output.WriteLine($"Error: {error}");
            Assert.Fail($"Expected success but got failure: {error}");
        }
        if (!shouldSucceed && success){
            Assert.Fail($"Expected failure but got success: {result}");
        }
        if (errorSubstring != null){
            Output.WriteLine($"Expression: {expression}");
            Output.WriteLine($"Error: {error}");
            Output.WriteLine($"Expected error substring: {errorSubstring}");
            Assert.Contains(errorSubstring, error);
        }
        Assert.Equal(shouldSucceed, success);
    }

}
