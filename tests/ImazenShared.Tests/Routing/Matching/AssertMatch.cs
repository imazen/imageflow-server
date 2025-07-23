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


// We can do better, make tests cleaner, with our own attribute system on top of MemberDataAttributeBase, providing ITheoryDataRows based on our custom attributes applied to the function.
//https://raw.githubusercontent.com/xunit/xunit/66cb80494a8bf0e5bba461a1de1b0295219bfb28/src/xunit.v3.core/MemberDataAttribute.cs
//https://raw.githubusercontent.com/xunit/xunit/66cb80494a8bf0e5bba461a1de1b0295219bfb28/src/xunit.v3.core/Attributes/MemberDataAttributeBase.cs


public static class AssertMatch
{
   /// <summary>
    /// Improved helper method that handles parsing internally and gets expression from method attribute
    /// </summary>
    public static void Captures(object caller, ITestOutputHelper output, MatchingContext matchingContext, string input, bool expectedSuccess, string? expectedCaptures = null,
       [System.Runtime.CompilerServices.CallerMemberName] string testMethod = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        // Get the match expression from the method attribute
        var method = caller.GetType().GetMethod(testMethod);
        var expressionAttr = method?.GetCustomAttribute<MatchExpressionAttribute>();
        if (expressionAttr == null)
        {
            Assert.Fail($"[{testMethod}:{lineNumber}] Method must have MatchExpression attribute");
            return;
        }

        var expression = expressionAttr.Expression;

        // Parse the expression
        if (!MultiValueMatcher.TryParse(expression, out var matcher, out var parseError))
        {
            Assert.Fail($"[{testMethod}:{lineNumber}] Failed to parse expression '{expression}': {parseError}");
            return;
        }

        // Test the match
        var result = matcher.Match(matchingContext, input);

        if (result.Success != expectedSuccess)
        {
            var message = result.Success
                ? $"[{testMethod}:{lineNumber}] False positive! Expression '{expression}' should NOT have matched '{input}'. Error: {result.Error}"
                : $"[{testMethod}:{lineNumber}] Incorrect failure! Expression '{expression}' failed to match '{input}'. Error: {result.Error}";
            output.WriteLine(message);
            Assert.Fail(message);
        }

        // Verify captures if expected
        if (expectedSuccess && expectedCaptures != null)
        {
            var expectedCapturesDict = Imazen.Routing.Helpers.PathHelpers.ParseQuery(expectedCaptures)?.ToDictionary(x => x.Key, x => x.Value.ToString());
            var actualCaptures = result.Captures?.ToDictionary(x => x.Key, x => x.Value.ToString()) ?? new Dictionary<string, string>();
            output.WriteLine($"[{testMethod}:{lineNumber}] Expected: {Stringify(expectedCapturesDict ?? new())}, Actual: {Stringify(actualCaptures)}");
            actualCaptures.Should().BeEquivalentTo(expectedCapturesDict);
        }
    }
    private static string Stringify(IDictionary<string, string> pairs) => "{" + string.Join(",", pairs.Select(x => $"{x.Key}='{x.Value}'")) + "}";


}
[AttributeUsage(AttributeTargets.Method)]
public class MatchExpressionAttribute : Attribute
{
    public string Expression { get; }
    public MatchExpressionAttribute(string expression)
    {
        Expression = expression;
    }
}
