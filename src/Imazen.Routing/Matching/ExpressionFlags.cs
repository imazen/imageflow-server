using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Imazen.Routing.Matching;

public partial record ExpressionFlags(ReadOnlyCollection<string> Flags)
{
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
        out string? error, Regex validationRegex)
    {
        var flags = new List<string>();
        var span = expression.Span;
        
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
        remainingExpression = expression[..startAt];
        var inner = expression[(startAt + 1)..^1];
        var innerSpan = inner.Span;
        while (innerSpan.Length > 0)
        {
            var commaIndex = innerSpan.IndexOf(',');
            if (commaIndex == -1)
            {
                flags.Add(inner.Span.Trim().ToString());
                break;
            }
            flags.Add(inner.Span[..commaIndex].Trim().ToString());
            inner = inner[(commaIndex + 1)..];
            innerSpan = inner.Span;
        }
        // validate 
        foreach (var flag in flags)
        {
            if (!validationRegex.IsMatch(flag))
            {
                result = flags;
                error = $"Invalid flag '{flag}', it does not match the required format {validationRegex}.";
                return false;
            }
        }
        // Handle [flag][flag]etc, recursively
        if (!TryParseFromEnd(remainingExpression, out var remainingExpression2, out var flags2, out error, validationRegex))
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
}
