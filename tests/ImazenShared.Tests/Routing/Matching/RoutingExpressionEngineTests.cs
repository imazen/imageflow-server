using Imazen.Routing.Matching;
using Xunit;
using Imazen.Routing.RoutingExpressions;

namespace Imazen.Tests.Routing.Matching;

public class RoutingExpressionEngineTests
{
    public RoutingExpressionEngineTests(ITestOutputHelper output)
    {
        Output = output;
    }
    public ITestOutputHelper Output { get; }
    [Theory]
    // Basic Path Match -> Path Template
    [InlineData("/users/{id:int} => /u/{id} [v1]", "/users/123", "/u/123")]
    // Path Match with transform -> Path Template
    [InlineData("/products/{code:upper} => /items/{code:lower} [v1]", "/products/ABC", "/items/abc")]
    // Path Match -> Query Template
    [InlineData("/items/{itemId} => /api/query?identifier={itemId} [v1]", "/items/xyz789", "/api/query?identifier=xyz789")]
    // Query Match -> Path Template
    [InlineData("/path?action={act} => /do/{act} [v1]", "/path?action=create&user=1", "/do/create")]
    // Query Match -> Query Template
    [InlineData("/data?filter={f:equals(a|b)} => /results?f={f:upper}&source=original [v1]", "/data?filter=a", "/results?f=A&source=original")]
    // Optional Matcher Variable -> Optional Template Handling
    [InlineData("/search/{term:?} => /find?q={term:?:default(all)} [v1]", "/search/", "/find?q=all")]
    [InlineData("/search/{term:?} => /find?q={term:?}&go=1 [v1]", "/search/", "/find?go=1")]
    [InlineData("/search/{slug:?:suffix(/)}{term:?} => /find?q={term:or-var(slug)} [v1]", "/search/slug/", "/find?q=slug")]
    // Matcher with multiple captures -> Template using subset
    [InlineData("/img/{w:int}x{h:int}/{name}.{ext:eq(jpg)} => /thumb/{name}?width={w} [v1]", "/img/100x50/flower.jpg", "/thumb/flower?width=100")]
    [InlineData("/file/{id} => /f/{id:upper} [v1]", "/file/abc", "/f/ABC")]

    // Matcher with optional var and required var, but we forgot "suffix(/)", trying "ends(/)" first
    [InlineData("/file/{id:int:?:suffix(/)}}{path:alpha} => /{path}?id={id:?:default(none)} [v1]", "/file/testpath", null)]
    // Matcher with optional var and required var
    // Unexpected behavior since optional doesn't change the suffix chopping the path into blocks.
    // [InlineData("/file/{id:int:?:suffix(/)}{path:alpha} => /{path}?id={id:?:default(none)} [v1]", "/file/testpath", "/testpath?id=none")]
    [InlineData("/file/{id:int:?:suffix(/)}{path:alpha} => /{path}?id={id:?:default(none)} [v1]", "/file/123/testpath", "/testpath?id=123")]
    [InlineData("/file/{id:chars([0-9]):?:suffix(/)}{path:alpha} => /{path}?id={id:?:default(none)} [v1]", "/file/123/testpath", "/testpath?id=123")]
    // No match
    [InlineData("/no/match => /should/not/happen [v1]", "/other/path", null)]
    public void TestEndToEndRouting(string expression, string inputUrl, string? expectedOutput)
    {
        var parsingOptions = RoutingParsingOptions.AnySchemeAnyFlagRequirePath;
        var success = RoutingExpressionParser.TryParse(parsingOptions, expression, out var parsed, out var error);
        if (!success)
        {
            Output.WriteLine($"Parsing failed for '{expression}'");
            Output.WriteLine($"Error: {error}");
            Assert.Fail($"Parsing failed for '{expression}' with error: {error}");
        }
        else
        {
            Output.WriteLine($"Parsed: {parsed}");
            var context = MatchingContext.Default;

            var result = parsed!.Value.Matcher.Match(context, inputUrl, null);
            if (!result.Success && expectedOutput != null) Assert.Fail($"InputUrl '{inputUrl}' did not match expression '{expression}'. Error: {result.Error}");
            if (result.Success && expectedOutput == null) Assert.Fail($"InputUrl '{inputUrl}' matched expression '{expression}' but was not expected to. Captures: {string.Join(", ", result.Captures?.Select(kv => kv.Key + "=" + kv.Value) ?? Array.Empty<string>())}");
            if (!result.Success && expectedOutput == null) return; // as expected, no match
            if (!parsed!.Value.Template.TryEvaluateToCombinedString(result.Captures ?? new Dictionary<string, string>(),
                out string? pathAndQuery, out string? templateError))
            {
                Output.WriteLine($"Template evaluation failed: {templateError}");
                Assert.Fail($"Template evaluation failed: {templateError}");
            }

            Output.WriteLine($"InputUrl: {inputUrl}");


            Output.WriteLine($"Result.PathAndQuery: {pathAndQuery}");
            Output.WriteLine($"ExpectedOutput: {pathAndQuery}");

            Assert.Equal(expectedOutput,pathAndQuery);
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
        var parsingOptions = RoutingParsingOptions.AnySchemeAnyFlagRequirePath;
        var fullExpression = $"{matchExpression} => /result [v1]";
        
        var success = RoutingExpressionParser.TryParse(parsingOptions, fullExpression, out var parsed, out var error);
        
        if (expectedError != null)
        {
            // Should fail parsing
            Assert.False(success, $"Expected parsing to fail for '{matchExpression}', but it succeeded");
            Assert.NotNull(error);
            Assert.Contains(expectedError, error);
            Output.WriteLine($"Match Expression: {matchExpression}");
            Output.WriteLine($"Expected Error: {expectedError}");
            Output.WriteLine($"Actual Error: {error}");
        }
        else
        {
            // Should succeed parsing
            Assert.True(success, $"Expected parsing to succeed for '{matchExpression}', but failed with: {error}");
            Assert.NotNull(parsed);
            Assert.Null(error);
        }
    }
}
