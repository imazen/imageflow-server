using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Imazen.Routing.Matching;

public record ExpressionFlags(ReadOnlyCollection<string> Flags)
{
    /// <summary>
    /// Parses the flags from the end of the expression. Syntax is [flag1,flag2,flag3]
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="remainingExpression"></param>
    /// <param name="result"></param>
    /// <param name="error"></param>
    /// <returns></returns>
    public static bool TryParseFromEnd(ReadOnlyMemory<char> expression,out ReadOnlyMemory<char> remainingExpression, out List<string> result, 
        [NotNullWhen(false)]
        out string? error)
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
                flags.Add(inner.ToString());
                break;
            }
            flags.Add(inner[..commaIndex].ToString());
            inner = inner[(commaIndex + 1)..];
            innerSpan = inner.Span;
        }
        // validate only a-z-
        foreach (var flag in flags)
        {
            if (!flag.All(x => x == '-' || (x >= 'a' && x <= 'z')))
            {
                result = flags;
                error = $"Invalid flag '{flag}', only a-z- are allowed";
                return false;
            }
        }

        result = flags;
        error = null;
        return true;
    }
}