using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Imazen.Routing.Matching;

internal static partial class ExpressionParsingHelpers
{
    public static bool Is(this ReadOnlySpan<char> text, string value)
    {
        if (text.Length != value.Length) return false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != value[i]) return false;
        }
        return true;
    }
    
    [Flags]
    internal enum GlobChars
    {
        None = 0,
        Star = 2,
        DoubleStar = 4,
        StarOptional = Star | Optional,
        DoubleStarOptional = DoubleStar | Optional,
        Optional = 1,
    }
    internal static GlobChars GetGlobChars(ReadOnlySpan<char> text)
    {
        if (text.Length == 1)
        {
            return text[0] switch
            {
                '*' => GlobChars.Star,
                '?' => GlobChars.Optional,
                _ => GlobChars.None
            };
        }
        if (text.Length == 2)
        {
            return text[0] switch
            {
                '*' => text[1] switch
                {
                    '*' => GlobChars.DoubleStar,
                    '?' => GlobChars.StarOptional,
                    _ => GlobChars.None
                },
                _ => GlobChars.None
            };
        }
        if (text.Length == 3 && text[0] == '*' && text[1] == '*' && text[2] == '?')
        {
            return GlobChars.DoubleStarOptional;
        }
        return GlobChars.None;
    }
    
    /// <summary>
    /// Find the first character that is not escaped by escapeChar.
    /// A character is considered escaped if it is preceded by an odd number of consecutive escapeChars.
    /// </summary>
    /// <param name="str"></param>
    /// <param name="c"></param>
    /// <param name="escapeChar"></param>
    /// <returns></returns>
    internal static int FindCharNotEscaped(ReadOnlySpan<char> str,char c, char escapeChar)
    {
        var consecutiveEscapeChars = 0;
        for (var i = 0; i < str.Length; i++)
        {
            if (str[i] == escapeChar)
            {
                consecutiveEscapeChars++;
                continue;
            }
            if (str[i] == c && consecutiveEscapeChars % 2 == 0)
            {
                return i;
            }
            consecutiveEscapeChars = 0;
        }
        return -1;
    }
    
        
#if NET8_0_OR_GREATER
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex ValidSegmentName();
#else
    
    private static readonly Regex ValidSegmentNameVar = 
        new(@"^[a-zA-Z_][a-zA-Z0-9_]*$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50));
    private static Regex ValidSegmentName() => ValidSegmentNameVar;
#endif

    /// <summary>
    /// We allow [a-zA-Z_][a-zA-Z0-9_]* for segment names
    /// </summary>
    /// <param name="name"></param>
    /// <param name="segmentExpression"></param>
    /// <param name="error"></param>
    /// <returns></returns>
    internal static bool ValidateSegmentName(string name, ReadOnlySpan<char> segmentExpression, [NotNullWhen(false)]out string? error)
    {
        if (name.Length == 0)
        {
            error = "Don't use empty segment names, only null or valid (internal error, callers should filter empty names)";
            return false;
        }
        if (name.Contains('*') || name.Contains('?'))
        {
            // Nullified by external logic, actually.
            error =
                $"Invalid segment expression {{{segmentExpression.ToString()}}} Conditions and modifiers such as * and ? belong after the colon. Ex: {{name:*:?}} ";
            return false;
        }
        if (!ValidSegmentName().IsMatch(name))
        {
            error = $"Invalid name '{name}' in segment expression {{{segmentExpression.ToString()}}}. Names must start with a letter or underscore, and contain only letters, numbers, or underscores";
            return false;
        }
        if (StringCondition.IsReservedName(name))
        {
            error = $"Did you forget to put : before your condition? '{name}' cannot be used as a variable name in {{{segmentExpression.ToString()}}} (for clarity), since it has a function.";
            return false;
        }
        error = null;
        return true;
    }

    internal static bool TryReadConditionName(ReadOnlySpan<char> text, 
        [NotNullWhen(true)] out int? conditionNameEnds,
        [NotNullWhen(false)] out string? error)
    {
        // A "" condition name is valid (aliased to equals)
        conditionNameEnds = text.IndexOf('(');
        if (conditionNameEnds == -1)
        {
            conditionNameEnds = text.Length;
        }
        // check for invalid characters
        for (var i = 0; i < conditionNameEnds; i++)
        {
            var c = text[i];
            if (i == 0)
            {
                if (c is >= 'a' and <= 'z')
                {
                    continue;
                }
            } else if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')
            {
                continue;
            }
            
            error = $"Invalid character '{text[i]}' (a-z)(a-z0-9-_ only) in condition name. Condition expression: '{text.ToString()}'";
            return false;
            
        }
        error = null;
        return true;
    }
    
    // parse a condition call (function call style)
    // condition_name(arg1, arg2, arg3)
    // where condition_name is [a-z_]
    // where arguments can be
    // character classes: [a-z_]
    // 'string' or "string" or 
    // numbers 123 or 123.456
    // a | delimited string array a|b|c
    // a unquoted string
    // we don't support nested function calls
    // args are comma delimited
    // The following characters must be escaped: , | ( ) ' " \ [ ]

    public static bool TryParseCondition(ReadOnlyMemory<char> text,
        [NotNullWhen(true)] 
        out ReadOnlyMemory<char>? functionName, out List<ReadOnlyMemory<char>>? args,
        [NotNullWhen(false)] out string? error)
    {
        var textSpan = text.Span;
        if (!TryReadConditionName(textSpan, out var functionNameEndsMaybe, out error))
        {
            functionName = null;
            args = null;
            return false;
        }
        int functionNameEnds = functionNameEndsMaybe.Value;

        functionName = text[..functionNameEnds];

        if (functionNameEnds == text.Length)
        {
            args = null;
            error = null;
            return true;
        }

        if (textSpan[functionNameEnds] != '(')
        {
            throw new InvalidOperationException("Unreachable code");
        }

        if (textSpan[^1] != ')')
        {
            error = $"Expected ')' at end of condition expression '{text.ToString()}'";
            functionName = null;
            args = null;
            return false;
        }

        // now parse using unescaped commas
        var argListMemory = text[(functionNameEnds + 1)..^1];
        var argListSpan = argListMemory.Span;
        if (argListSpan.Length == 0)
        {
            error = null;
            args = null;
            return true;
        }    
        // We have at least one character
        args = new List<ReadOnlyMemory<char>>();
        // Split using FindCharNotEscaped(text, ',', '\\')
        var start = 0;
        while (start < argListSpan.Length)
        {
            var subSpan = argListSpan[start..];
            var commaIndex = FindCharNotEscaped(subSpan, ',', '\\');
            if (commaIndex == -1)
            {
                args.Add(argListMemory[start..]);
                break;
            }
            args.Add(argListMemory[start..(start + commaIndex)]);
            start += commaIndex + 1;
        }
        error = null;
        return true;
    }

    [Flags]
    public enum ArgType
    {
        Empty = 0,
        CharClass = 1,
        Char = 2,
        String = 4,
        Array = 8,
        UnsignedNumeric = 16,
        DecimalNumeric = 32,
        IntegerNumeric = 64,
        UnsignedDecimal = UnsignedNumeric | DecimalNumeric,
        UnsignedInteger = UnsignedNumeric | IntegerNumeric,
    }
    
    public static ArgType GetArgType(ReadOnlySpan<char> arg)
    {
        if (arg.Length == 0) return ArgType.Empty;
        if (arg[0] == '[' && arg[^1] == ']') return ArgType.CharClass;
        if (FindCharNotEscaped(arg, '|', '\\') != -1) return ArgType.Array;
        var type = ArgType.IntegerNumeric | ArgType.DecimalNumeric | ArgType.UnsignedNumeric;
        foreach (var c in arg)
        {
            if (c is >= '0' and <= '9') continue;
            if (c == '.')
            {
                type &= ~ArgType.IntegerNumeric;
            }
            if (c == '-')
            {
                type &= ~ArgType.UnsignedNumeric;
            }
            type &= ~ArgType.DecimalNumeric;
        }
        if (arg.Length == 1) type |= ArgType.Char;
        type |= ArgType.String;
        return type;
    }


    public static bool IsCommonCaseInsensitiveChar(char c)
    {
        return c is (>= ' ' and < 'A') or (> 'Z' and < 'a') or (> 'z' and <= '~') or '\t' or '\r' or '\n';
    }
}