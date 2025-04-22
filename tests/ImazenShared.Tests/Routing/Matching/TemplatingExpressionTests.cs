using System;
using System.Collections.Generic;
using Imazen.Routing.Matching.Templating;
using Xunit;
using System.Linq;

namespace Imazen.Tests.Routing.Matching;

public class TemplatingExpressionTests
{
    // =================== Parsing Tests ===================

    [Theory]
    // Basic Path
    [InlineData("/users/{id}", true, null)]
    [InlineData("/images/{filename}.jpg", true, null)]
    [InlineData("/literals/only", true, null)]
    // Basic Query
    [InlineData("?key={value}", true, null)]
    [InlineData("/path?key={value}", true, null)]
    [InlineData("/path?k1={v1}&k2={v2}", true, null)]
    [InlineData("?key=literal", true, null)]
    [InlineData("?{keyName}={value}", true, null)] // Templated key
    // Transformations
    [InlineData("/path/{var:lower}", true, null)]
    [InlineData("/path/{var:upper:encode}", true, null)]
    [InlineData("/path/{var:map(a,b):map(c,d):default(x)}", true, null)]
    [InlineData("/path/{var:or(fallback):lower}", true, null)]
    [InlineData("/path?var={val:optional}", true, null)]
    [InlineData("/path?var={val:?}", true, null)]
    // Escaping (Using Verbatim Strings @"")
    [InlineData(@"\{literal\}", true, null)] // Escaped braces
    [InlineData(@"/path/{var:default(a\,b)}", true, null)] // Escaped comma in default
    [InlineData(@"/path/{var:map(a\(1\),b)}", true, null)] // Escaped parens in map
    [InlineData(@"/path/{var:map(a\\b,c)}", true, null)] // Escaped backslash in map
    // Invalid Syntax
    [InlineData("/path/{var", false, "Unmatched '{'")] // Unmatched {
    [InlineData("/path/var}", false, "Unexpected '}'")] // Unmatched }
    [InlineData("/path/{var:badtransform}", false, "Unknown transformation")] // Unknown transform
    [InlineData("/path/{var:lower()}", false, "does not accept arguments")] // Transform with unexpected args
    [InlineData("/path/{var:map(a)}", false, "even number of arguments")] // Map with odd args
    [InlineData("/path/{var:or()}", false, "exactly one argument")] // Or with wrong arg count
    [InlineData("/path/{var:default}", false, "requires arguments")] // Default requires args
    [InlineData("/path/{:lower}", false, "Variable name cannot be empty")] // Empty variable name
    public void TestParsing(string template, bool shouldSucceed, string? expectedErrorSubstring)
    {
        bool success = MultiTemplate.TryParse(template.AsMemory(), out var multiTemplate, out var error);

        if (shouldSucceed)
        {
            Assert.True(success, $"Parsing should succeed for '{template}', but failed with: {error}");
            Assert.NotNull(multiTemplate);
            Assert.Null(error);
            // TODO: Could add more detailed checks on the parsed structure (segments, transforms)
        }
        else
        {
            Assert.False(success, $"Parsing should fail for '{template}', but succeeded.");
            Assert.Null(multiTemplate);
            Assert.NotNull(error);
            if (expectedErrorSubstring != null)
            {
                Assert.Contains(expectedErrorSubstring, error, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // =================== Evaluation Tests ===================

    private static Dictionary<string, string> Vars(params string[] pairs)
    {
        var dict = new Dictionary<string, string>();
        for (int i = 0; i < pairs.Length; i += 2)
        {
            dict.Add(pairs[i], pairs[i + 1]);
        }
        return dict;
    }

    [Theory]
    // Basic Path
    [InlineData("/users/{id}", "/users/123", "id", "123")]
    [InlineData("/files/{name}.{ext}", "/files/report.pdf", "name", "report", "ext", "pdf")]
    [InlineData("literal/path", "literal/path")]
    // Basic Query
    [InlineData("/search?q={term}", "/search?q=testing", "term", "testing")]
    [InlineData("/api?id={id}&ver={v}", "/api?id=abc&ver=2", "id", "abc", "v", "2")]
    [InlineData("/api?{key}={val}", "/api?user=def", "key", "user", "val", "def")]
    // Case Transforms
    [InlineData("/users/{name:lower}", "/users/alice", "name", "Alice")]
    [InlineData("/products/{code:upper}", "/products/XYZ", "code", "Xyz")]
    // Escaping in Template (Using Verbatim Strings @"")
    [InlineData(@"/file\{name\}.txt", "/file{name}.txt")] // Should output literal braces
    // Default Transform
    [InlineData("/items?page={p:default(1)}", "/items?page=1")] // Missing p
    [InlineData("/items?page={p:default(1)}", "/items?page=5", "p", "5")] // Present p
    [InlineData("/items?page={p:default(1)}", "/items?page=1", "p", "")] // Empty p
    // Or Transform
    [InlineData("/data?id={id:or(guid)}", "/data?id=abc", "id", "abc")] // id present
    [InlineData("/data?id={id:or(guid)}", "/data?id=fallback123", "guid", "fallback123")] // id missing, guid present
    [InlineData("/data?id={id:or(guid)}", "/data?id=", "id", "")] // id empty, use empty
    [InlineData("/data?id={id:or(guid)}", "/data?id=")] // Both missing
    // Map Transform
    [InlineData("/status?code={val:map(1,ok):map(2,err)}", "/status?code=ok", "val", "1")]
    [InlineData("/status?code={val:map(1,ok):map(2,err)}", "/status?code=err", "val", "2")]
    [InlineData("/status?code={val:map(1,ok):map(2,err)}", "/status?code=3", "val", "3")] // No match
    [InlineData("/status?code={val:map(1,ok):map(2,err):default(unk)}", "/status?code=unk", "val", "3")] // No match with default
    // Encode Transform
    [InlineData("/search?q={term:encode}", "/search?q=hello%20world", "term", "hello world")]
    [InlineData("/path/{term:encode}/details", "/path/a%2Fb%2Fc/details", "term", "a/b/c")]
    // Optional Query Params (?)
    [InlineData("/api?user={u:?}", "/api", "u", "")] // Empty optional -> omit pair
    [InlineData("/api?user={u:?}", "/api", "x", "y")] // Missing optional -> omit pair
    [InlineData("/api?user={u:?}", "/api?user=test", "u", "test")] // Present optional -> include pair
    [InlineData("/api?user={u:lower:?}", "/api?user=test", "u", "TEST")] // Transform before optional check
    [InlineData("/api?user={u:lower:?}", "/api", "u", "")] // Empty after transform -> omit
    [InlineData("/api?{k:?}=val", "/api", "k", "")] // Optional key -> omit pair
    [InlineData("/api?key={v:?}", "/api?key=", "v", "")] // Optional value empty -> omit pair (NOTE: check if this is desired vs key=)
    [InlineData("/api?{k:?}=val&a=b", "/api?a=b", "k", "")] // Omit first optional
    [InlineData("/api?a=b&k={v:?}", "/api?a=b", "v", "")] // Omit second optional
    [InlineData("/api?a=b&k={v:?}", "/api?a=b&k=1", "v", "1")] // Include second optional
    // Optional Query Params (:optional)
    [InlineData("/api?user={u:optional}", "/api", "u", "")] // Empty optional -> omit pair
    // Chaining
    [InlineData("/data/{id:or(guid):upper}", "/data/FALLBACK123", "guid", "fallback123")] // or -> upper
    [InlineData("/path/{val:map(a,b):default(X):upper}", "/path/B", "val", "a")] // map -> default(no) -> upper
    [InlineData("/path/{val:map(a,b):default(X):upper}", "/path/X", "val", "c")] // map(no) -> default -> upper
    // Empty Variable Handling (Not Optional)
    [InlineData("/api?user={u}", "/api?user=", "u", "")] // Empty non-optional var -> key=
    [InlineData("/api?{k}=val", "/api?=val", "k", "")] // Empty non-optional key -> =val
    public void TestEvaluation(string template, string expectedOutput, params string[] variables)
    {
        var parsedTemplate = MultiTemplate.Parse(template.AsMemory());
        var inputVars = Vars(variables);

        string result = parsedTemplate.Evaluate(inputVars);

        Assert.Equal(expectedOutput, result);
    }

    // TODO: Add tests for evaluation errors (e.g., missing required variable)
    // TODO: Add tests for validation mode once implemented
    // TODO: Add tests using QueryStringParser once available/implemented
} 