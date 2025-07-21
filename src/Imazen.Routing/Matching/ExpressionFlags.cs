using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Imazen.Routing.Matching;
public readonly record struct ExpressionFlagParsingOptions(Regex ValidationRegex, bool TrimTrailingExpressionWhitespace,
bool AllowWhitespaceAfterBrackets,
bool TrimWhitespaceAroundFlags){
    public static readonly ExpressionFlagParsingOptions Permissive = new(ExpressionFlags.LatestPermissiveFlagSyntax(), true, true, true);


    public static readonly ExpressionFlagParsingOptions LowercaseDash = new(ExpressionFlags.LowercaseDash(), true, true, true);

    // WithValidationRegex
    public ExpressionFlagParsingOptions WithValidationRegex(Regex validationRegex)
    {
        return new ExpressionFlagParsingOptions(validationRegex, TrimTrailingExpressionWhitespace, AllowWhitespaceAfterBrackets, TrimWhitespaceAroundFlags);
    }
}
public partial record ExpressionFlags(ReadOnlyCollection<string> Flags)
{

    private static ReadOnlyMemory<char> TrimEnd(ReadOnlyMemory<char> text)
    {
#if NET6_0_OR_GREATER
        return text.TrimEnd();
#else
        var span = text.Span.TrimEnd();
        return text.Slice(0, span.Length);
#endif
    }
    /// <summary>
    /// Parses the flags from the end of the expression. Syntax is [flag1,flag2,flag3]
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="remainingExpression"></param>
    /// <param name="result"></param>
    /// <param name="error"></param>
    /// <returns></returns>
    public static bool TryParseFromEnd(ReadOnlyMemory<char> expression, out ReadOnlyMemory<char> remainingExpression, out List<string> result,
        [NotNullWhen(false)]
        out string? error, ExpressionFlagParsingOptions options)
    {
        var flags = new List<string>();
        if (options.TrimTrailingExpressionWhitespace)
        {
            expression = TrimEnd(expression);
        }

        var span = expression.Span;
        if (options.AllowWhitespaceAfterBrackets)
        {
            span = span.TrimEnd();
        }

        if (span.Length == 0 || span[^1] != ']')
        {
            //It's ok for there to be none
            result = flags;
            error = null;
            remainingExpression = expression;
            return true;
        }
        var startAt = span.LastIndexOf('[');
        if (startAt == -1)
        {
            result = flags;
            error = "Flags must be enclosed in [], only found closing ]";
            remainingExpression = expression;
            return false;
        }
        remainingExpression = options.TrimTrailingExpressionWhitespace ? TrimEnd(expression[..startAt]) : expression[..startAt];
        var inner = expression[(startAt + 1)..^1];
        var innerSpan = inner.Span;
        while (innerSpan.Length > 0)
        {
            var commaIndex = innerSpan.IndexOf(',');
            if (commaIndex == -1)
            {
                flags.Add(options.TrimWhitespaceAroundFlags ? inner.Span.Trim().ToString() : inner.Span.ToString());
                break;
            }
            flags.Add(options.TrimWhitespaceAroundFlags ? inner.Span[..commaIndex].Trim().ToString() : inner.Span[..commaIndex].ToString());
            inner = inner[(commaIndex + 1)..];
            innerSpan = inner.Span;
        }
        // validate flags
        foreach (var flag in flags)
        {
            if (!options.ValidationRegex.IsMatch(flag))
            {
                result = flags;
                error = $"Invalid flag '{flag}', it does not match the required format {options.ValidationRegex}.";
                return false;
            }
        }
        // Handle [flag][flag]etc, recursively
        if (!TryParseFromEnd(remainingExpression, out var remainingExpression2, out var flags2, out error, options))
        {
            remainingExpression = remainingExpression2;
            flags.AddRange(flags2);
            result = flags;
            return false;
        }
        // We might have found no additional ones, flags2.count == 0 is the only way to know.
        remainingExpression = remainingExpression2;
        flags.AddRange(flags2);
        result = flags;
        error = null;
        return true;
    }

#if NET8_0_OR_GREATER
    [GeneratedRegex(@"^[a-zA-Z-][a-zA-Z0-9-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    public static partial Regex AlphaNumericDash();
#else

    public static readonly Regex AlphaNumericDashVar =
        new(@"^[a-zA-Z-][a-zA-Z0-9-]*$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50));

    public static Regex AlphaNumericDash() => AlphaNumericDashVar;
#endif

#if NET8_0_OR_GREATER
    [GeneratedRegex(@"^[a-z-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    public static partial Regex LowercaseDash();
#else

    public static readonly Regex LowercaseDashVar =
        new(@"^[a-z-]*$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50));

    public static Regex LowercaseDash() => LowercaseDashVar;
#endif

    // Allow k/v pairs as well, [a-zA-Z-][a-zA-Z0-9-]*([=][a-zA-Z0-9-]+)?
#if NET8_0_OR_GREATER
    [GeneratedRegex(@"^[a-zA-Z-_][a-zA-Z0-9-]*([=][a-zA-Z0-9-_]+)?$")]
    public static partial Regex LatestPermissiveFlagSyntax();
#else

    public static readonly Regex LatestPermissiveFlagSyntaxVar =
        new(@"^[a-zA-Z-_][a-zA-Z0-9-]*([=][a-zA-Z0-9-_]+)?$");

    public static Regex LatestPermissiveFlagSyntax() => LatestPermissiveFlagSyntaxVar;
#endif

}
