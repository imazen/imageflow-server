using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using EnumFastToStringGenerated;

namespace Imazen.Routing.Matching;


public readonly record struct StringCondition
{   
    private StringCondition(StringConditionKind stringConditionKind, char? c, string? str, CharacterClass? charClass, string[]? strArray, int? int1, int? int2)
    {
        this.stringConditionKind = stringConditionKind;
        this.c = c;
        this.str = str;
        this.charClass = charClass;
        this.strArray = strArray;
        this.int1 = int1;
        this.int2 = int2;
    }
    
    internal static StringCondition ExcludeSlashes = new (StringConditionKind.CharClass, null, null, 
        new CharacterClass(true, new ReadOnlyCollection<CharacterClass.CharRange>(new CharacterClass.CharRange[]{}), 
            new ReadOnlyCollection<char>(new[] {'/','\\'})), null, null, null);
    private static StringCondition? TryCreate(out string? error, StringConditionKind stringConditionKind, char? c, string? str, CharacterClass? charClass, string[]? strArray, int? int1, int? int2)
    {
        var condition = new StringCondition(stringConditionKind, c, str, charClass, strArray, int1, int2);
        if (!condition.ValidateArgsPresent(out error))
        {
            return null;
        }
        if (!condition.IsInitialized)
        {
            error = "StringCondition Kind.Uninitialized is not allowed";
            return null;
        }
        return condition;
    }

    internal static bool IsReservedName(string name)
    {
        return TryGetKindsForConditionAlias(name, true, out _);
    }
    private static bool TryGetKindsForConditionAlias(string name, bool useIgnoreCaseVariant, [NotNullWhen(true)] out IReadOnlyCollection<StringConditionKind>? kinds)
    {
        var normalized = StringConditionKindAliases.NormalizeAliases(name);
        if (useIgnoreCaseVariant)
        {
            normalized = StringConditionKindAliases.GetIgnoreCaseVersion(normalized);
        }
        if (KindLookup.Value.TryGetValue(normalized, out var k))
        {
            kinds = k;
            return true;
        }
        kinds = null;
        return false;
    }
    internal static StringCondition? TryParse(out string? error, string name, List<ReadOnlyMemory<char>>? args, bool useIgnoreCaseVariant)
    {
        
        if (!TryGetKindsForConditionAlias(name, useIgnoreCaseVariant, out var kinds))
        {
            error = $"Unknown condition kind '{name}'";
            return null;
        }
        
        var errors = new List<string>();
        foreach (var kind in kinds)
        {
            var condition = TryCreate(kind, name, args, out error);
            if (error != null)
            {
                errors.Add($"Tried {kind} with {ArgsToStr(args)}: {error}");
            }
            if (condition == null) continue;
            if (!condition.Value.ValidateArgsPresent(out error))
            {
                throw new InvalidOperationException($"Condition was created with invalid arguments. Received: ({ArgsToStr(args)})");
            }
            return condition;
        }
        error = $"Invalid arguments for condition '{name}'. Received: ({ArgsToStr(args)}). Errors: {string.Join(", ", errors)}";
        return null;
    }
    private static string ArgsToStr(List<ReadOnlyMemory<char>>? args) => args == null ? "" : string.Join(",", args);

    private static StringCondition? TryCreate(StringConditionKind stringConditionKind, string name, List<ReadOnlyMemory<char>>? args, out string? error)
    {
        var expectedArgs = ForKind(stringConditionKind);
        if (args == null)
        {
            if (HasFlagsFast(expectedArgs, ExpectedArgs.None))
            {
                error = null;
                return new StringCondition(stringConditionKind, null, null, null, null, null, null);
            }
            error = $"Expected argument type {expectedArgs} for condition '{name}'; received none.";
            return null;
        }
        bool wantsFirstArgNumeric = HasFlagsFast(expectedArgs, ExpectedArgs.Int321) || HasFlagsFast(expectedArgs, ExpectedArgs.Int321OrInt322)
            || stringConditionKind == StringConditionKind.StartsWithNCharClass;

        bool firstOptional = HasFlagsFast(expectedArgs, ExpectedArgs.Int321OrInt322);
        var obj = TryParseArg(args[0], wantsFirstArgNumeric, firstOptional, out error);
        if (error != null)
        {
            error = $"Error parsing 1st argument: {error}";
            return null;
        }
        var twoIntArgs = HasFlagsFast(expectedArgs, ExpectedArgs.Int321OrInt322);
        var intArgAndClassArg = HasFlagsFast(expectedArgs, ExpectedArgs.Int321 | ExpectedArgs.CharClass);
        var secondArgRequired = intArgAndClassArg
                                || (twoIntArgs && obj == null);
        var secondArgWanted = secondArgRequired || twoIntArgs;
        var secondArgNumeric = twoIntArgs;
        if (args.Count != 1 && !secondArgWanted)
        {
            error = $"Expected 1 argument for condition '{name}'; received {args.Count}: {ArgsToStr(args)}.";
            return null;
        }
        if (secondArgRequired && args.Count != 2)
        {
            error = $"Expected 2 arguments for condition '{name}'; received {args.Count}: {ArgsToStr(args)}.";
            return null;
        }
        
        var obj2 = secondArgWanted && args.Count > 1 ? TryParseArg(args[1], secondArgNumeric, !secondArgRequired, out error) : null;
        if (error != null)
        {
            throw new InvalidOperationException($"Error parsing 2nd argument: {error}");
        }
        if (secondArgRequired && obj2 == null)
        {
            throw new InvalidOperationException($"Missing 2nd argument: {error}");
        }

        if (obj is double decVal)
        {
            error = $"Unexpected decimal argument for condition '{name}'; received '{args[0]}' {decVal}.";
            return null;
        }
        if (obj is int intVal)
        {
            if (twoIntArgs)
            {
                var int2 = obj2 as int?;
                error = null;
                return new StringCondition(stringConditionKind, null, null, null, null, intVal, int2);
            }else if (intArgAndClassArg)
            {
                if (obj2 is CharacterClass cc2)
                {
                    error = null;
                    return new StringCondition(stringConditionKind, null, null, cc2, null, intVal, null);
                }
                else
                {
                    error = $"Unexpected argument for condition '{name}'; received '{args[1]}'.";
                    return null;
                }
            } else if (HasFlagsFast(expectedArgs, ExpectedArgs.Int321))
            {
                error = null;
                return new StringCondition(stringConditionKind, null, null, null, null, intVal, null);
            }
            else
            {
                error = $"Unexpected int argument for condition '{name}'; received '{args[0]}'.";
                return null;
            }

        }
        if (obj is char c)
        {
            if (HasFlagsFast(expectedArgs, ExpectedArgs.Char))
            {
                error = null;
                return new StringCondition(stringConditionKind, c, null, null, null, null, null);
            }
            // try converting it to a string
            if (HasFlagsFast(expectedArgs, ExpectedArgs.String))
            {
                error = null;
                return new StringCondition(stringConditionKind, null, c.ToString(), null, null, null, null);
            }
            error = $"Unexpected char argument for condition '{name}'; received '{args[0]}'.";
            return null;
        }
        if (obj is string str)
        {
            if (HasFlagsFast(expectedArgs, ExpectedArgs.String))
            {
                error = null;
                return new StringCondition(stringConditionKind, null, str, null, null, null, null);
            }
            error = $"Unexpected string argument for condition '{name}'; received '{args[0]}'.";
            return null;
        }
        if (obj is string[] strArray)
        {
            if (HasFlagsFast(expectedArgs, ExpectedArgs.StringArray))
            {
                error = null;
                return new StringCondition(stringConditionKind, null, null, null, strArray, null, null);
            }
            error = $"Unexpected string array argument for condition '{name}'; received '{args[0]}'.";
            return null;
        }
        if (obj is CharacterClass cc)
        {
            if (HasFlagsFast(expectedArgs, ExpectedArgs.CharClass))
            {
                error = null;
                return new StringCondition(stringConditionKind, null, null, cc, null, null, null);
            }
            error = $"Unexpected char class argument for condition '{name}'; received '{args[0]}'.";
            return null;
        }

        if (twoIntArgs && obj is null)
        {
            var int2 = obj2 as int?;
            error = null;
            return new StringCondition(stringConditionKind, null, null, null, null, null, int2);
        }
        throw new NotImplementedException("Unexpected argument type: " + obj?.GetType());
    }
    
    private static string? TryParseString(ReadOnlySpan<char> arg, out string? error)
    {
        // Some characters must be escaped
        // ' " \ [ ] | , ( )
        error = null;
        return arg.ToString();
    }

    private static object? TryParseArg(ReadOnlyMemory<char> argMemory, bool tryParseNumeric, bool allowEmpty, out string? error)
    {
        var arg = argMemory.Span;
        var type = ExpressionParsingHelpers.GetArgType(arg);
        if (allowEmpty && type == ExpressionParsingHelpers.ArgType.Empty)
        {
            error = null;
            return null;
        }
        if ((type & ExpressionParsingHelpers.ArgType.CharClass) > 0)
        {
            if (!CharacterClass.TryParseInterned(argMemory,true, out var cc, out error))
            {
                return null;
            }
            return cc;
        }
        if ((type & ExpressionParsingHelpers.ArgType.Array) > 0)
        {
            // use FindCharNotEscaped
            var list = new List<string>();
            var start = 0;
            while (start < arg.Length)
            {
                var commaIndex = ExpressionParsingHelpers.FindCharNotEscaped(arg[start..], '|', '\\');
                if (commaIndex == -1)
                {
                    var lastStr = TryParseString(arg[start..], out error);
                    if (lastStr == null) return null;
                    list.Add(lastStr);
                    break;
                }

                var s = TryParseString(arg[start..(start + commaIndex)], out error);
                if (s == null) return null;
                list.Add(s);
                start += commaIndex + 1;
            }
            error = null;
            return list.ToArray();
        }

        if (tryParseNumeric & (type & ExpressionParsingHelpers.ArgType.IntegerNumeric) > 0)
        {
#if NET6_0_OR_GREATER
            if (int.TryParse(arg, out var i))
#else
            if (int.TryParse(arg.ToString(), out var i))
#endif
            {
                error = null;
                return i;
            }
        }

        if (tryParseNumeric & (type & ExpressionParsingHelpers.ArgType.DecimalNumeric) > 0)
        {
#if NET6_0_OR_GREATER
            if (double.TryParse(arg, out var d))
#else
            if (double.TryParse(arg.ToString(), out var d))
#endif
            {
                error = null;
                return d;
            }
        }

        if ((type & ExpressionParsingHelpers.ArgType.Char) > 0)
        {
            if (arg.Length != 1)
            {
                error = "Expected a single character";
                return null;
            }
            error = null;
            return arg[0];
        }
        
        if ((type & ExpressionParsingHelpers.ArgType.String) > 0)
        {
            return TryParseString(arg, out error);
        }
        throw new NotImplementedException();
    }
    
    private static readonly Lazy<Dictionary<string, IReadOnlyCollection<StringConditionKind>>> KindLookup = new Lazy<Dictionary<string, IReadOnlyCollection<StringConditionKind>>>(() =>
    {
        var dict = new Dictionary<string, IReadOnlyCollection<StringConditionKind>>();
        foreach (var kind in StringConditionKindEnumExtensions.GetValuesFast())
        {
            var key = kind.ToDisplayFast();
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<StringConditionKind>();
                dict[key] = list;
            }
            ((List<StringConditionKind>)list).Add(kind);
        }
        return dict;
    });
    
    
    public static StringCondition Uninitialized => new StringCondition(StringConditionKind.Uninitialized, null, null, null, null, null, null);
    private bool IsInitialized => stringConditionKind != StringConditionKind.Uninitialized;

    private readonly StringConditionKind stringConditionKind;
    private readonly char? c;
    private readonly string? str;
    private readonly CharacterClass? charClass;
    private readonly string[]? strArray;
    private readonly int? int1;
    private readonly int? int2;
    
   public bool Evaluate(ReadOnlySpan<char> text, MatchingContext context) => stringConditionKind switch
   {
       StringConditionKind.EnglishAlphabet => text.IsEnglishAlphabet(),
       StringConditionKind.NumbersAndEnglishAlphabet => text.IsNumbersAndEnglishAlphabet(),
       StringConditionKind.LowercaseEnglishAlphabet => text.IsLowercaseEnglishAlphabet(),
       StringConditionKind.UppercaseEnglishAlphabet => text.IsUppercaseEnglishAlphabet(),
       StringConditionKind.Hexadecimal => text.IsHexadecimal(),
       StringConditionKind.Int32 => text.IsInt32(),
       StringConditionKind.Int64 => text.IsInt64(),
       StringConditionKind.UInt32 => text.IsU32(),
       StringConditionKind.UInt64 => text.IsU64(),
       //StringConditionKind.EndsWithSupportedImageExtension => context.EndsWithSupportedImageExtension(text),
       StringConditionKind.IntegerRange => text.IsInIntegerRangeInclusive(int1, int2),
       StringConditionKind.Guid => text.IsGuid(),
       StringConditionKind.CharLength => text.LengthWithinInclusive(int1, int2),
       StringConditionKind.EqualsOrdinal => text.EqualsOrdinal(str!),
       StringConditionKind.EqualsOrdinalIgnoreCase => text.EqualsOrdinalIgnoreCase(str!),
       StringConditionKind.EqualsAnyOrdinal => text.EqualsAnyOrdinal(strArray!),
       StringConditionKind.EqualsAnyOrdinalIgnoreCase => text.EqualsAnyOrdinalIgnoreCase(strArray!),
       StringConditionKind.StartsWithChar => text.StartsWithChar(c!.Value),
       StringConditionKind.StartsWithOrdinal => text.StartsWithOrdinal(str!),
       StringConditionKind.StartsWithOrdinalIgnoreCase => text.StartsWithOrdinalIgnoreCase(str!),
       StringConditionKind.StartsWithAnyOrdinal => text.StartsWithAnyOrdinal(strArray!),
       StringConditionKind.StartsWithAnyOrdinalIgnoreCase => text.StartsWithAnyOrdinalIgnoreCase(strArray!),
       StringConditionKind.EndsWithChar => text.EndsWithChar(c!.Value),
       StringConditionKind.EndsWithOrdinal => text.EndsWithOrdinal(str!),
       StringConditionKind.EndsWithOrdinalIgnoreCase => text.EndsWithOrdinalIgnoreCase(str!),
       StringConditionKind.EndsWithAnyOrdinal => text.EndsWithAnyOrdinal(strArray!),
       StringConditionKind.EndsWithAnyOrdinalIgnoreCase => text.EndsWithAnyOrdinalIgnoreCase(strArray!),
       StringConditionKind.IncludesOrdinal => text.IncludesOrdinal(str!),
       StringConditionKind.IncludesOrdinalIgnoreCase => text.IncludesOrdinalIgnoreCase(str!),
       StringConditionKind.IncludesAnyOrdinal => text.IncludesAnyOrdinal(strArray!),
       StringConditionKind.IncludesAnyOrdinalIgnoreCase => text.IncludesAnyOrdinalIgnoreCase(strArray!),
       StringConditionKind.True => true,
       StringConditionKind.CharClass => text.IsCharClass(charClass!),
       StringConditionKind.StartsWithNCharClass => text.StartsWithNCharClass(charClass!, int1!.Value),
       StringConditionKind.StartsWithCharClass => text.StartsWithCharClass(charClass!),
       StringConditionKind.StartsWithCharClassIgnoreCase => text.StartsWithCharClass(charClass!), // TODO: doesn't ignore case, but we transform calls
       StringConditionKind.EndsWithCharClass => text.EndsWithCharClass(charClass!),
       StringConditionKind.EndsWithCharClassIgnoreCase => text.EndsWithCharClass(charClass!), // TODO: doesn't ignore case, but we transform calls
       StringConditionKind.Uninitialized => throw new InvalidOperationException("Uninitialized StringCondition was evaluated"),
       _ => throw new NotImplementedException()
   };

   private bool ValidateArgsPresent([NotNullWhen(false)] out string? error)
   {
       var expected = ForKind(stringConditionKind);
       if (HasFlagsFast(expected, ExpectedArgs.Char) != c.HasValue)
       {
           error = c.HasValue
               ? "Unexpected char parameter in StringCondition"
               : "char parameter missing in StringCondition";
           return false;
       }

       if (HasFlagsFast(expected, ExpectedArgs.String) != (str != null))
       {
           error = (str != null)
               ? "Unexpected string parameter in StringCondition"
               : "string parameter missing in StringCondition";
           return false;
       }

       if (HasFlagsFast(expected, ExpectedArgs.StringArray) != (strArray != null))
       {
           error = (strArray != null)
               ? "Unexpected string array parameter in StringCondition"
               : "string array parameter missing in StringCondition";
           return false;
       }
       if (HasFlagsFast(expected, ExpectedArgs.CharClass) != (charClass != null))
       {
           error = (charClass != null)
               ? "Unexpected char class parameter in StringCondition"
               : "char class parameter missing in StringCondition";
           return false;
       }
       
       if (HasFlagsFast(expected, ExpectedArgs.Int321OrInt322))
       {
           if (int1.HasValue || int2.HasValue)
           {
                error = null;
                return true;
           }
           error = "int parameter(s) missing in StringCondition";
           return false;
       }
       
       if (HasFlagsFast(expected, ExpectedArgs.Int321) != int1.HasValue)
       {
           error = int1.HasValue
               ? "Unexpected int parameter in StringCondition"
               : "int parameter missing in StringCondition";
           return false;
       }

       if (int2.HasValue)
       {
           error = "Unexpected 2nd int parameter in StringCondition";
           return false;
       }
       error = null;
       return true;
   }

   [Flags]
   private enum ExpectedArgs
   {
       None = 0,
       Char = 1,
       String = 2,
       StringArray = 4,
       Int321 = 8,
       Int321OrInt322 = 16 | Int321,
       CharClass = 32,
       Int32AndCharClass = Int321 | CharClass
   }
   private static bool HasFlagsFast(ExpectedArgs value, ExpectedArgs flags) => (value & flags) == flags;


   private static ExpectedArgs ForKind(StringConditionKind stringConditionKind) =>
         stringConditionKind switch
         {
              StringConditionKind.StartsWithChar => ExpectedArgs.Char,
              StringConditionKind.EndsWithChar => ExpectedArgs.Char,
              StringConditionKind.EqualsOrdinal => ExpectedArgs.String,
              StringConditionKind.EqualsOrdinalIgnoreCase => ExpectedArgs.String,
              StringConditionKind.EqualsAnyOrdinal => ExpectedArgs.StringArray,
              StringConditionKind.EqualsAnyOrdinalIgnoreCase => ExpectedArgs.StringArray,
              StringConditionKind.StartsWithOrdinal => ExpectedArgs.String,
              StringConditionKind.StartsWithOrdinalIgnoreCase => ExpectedArgs.String,
              StringConditionKind.EndsWithOrdinal => ExpectedArgs.String,
              StringConditionKind.EndsWithOrdinalIgnoreCase => ExpectedArgs.String,
              StringConditionKind.IncludesOrdinal => ExpectedArgs.String,
              StringConditionKind.IncludesOrdinalIgnoreCase => ExpectedArgs.String,
              StringConditionKind.StartsWithAnyOrdinal => ExpectedArgs.StringArray,
              StringConditionKind.StartsWithAnyOrdinalIgnoreCase => ExpectedArgs.StringArray,
              StringConditionKind.EndsWithAnyOrdinal => ExpectedArgs.StringArray,
              StringConditionKind.EndsWithAnyOrdinalIgnoreCase => ExpectedArgs.StringArray,
              StringConditionKind.IncludesAnyOrdinal => ExpectedArgs.StringArray,
              StringConditionKind.IncludesAnyOrdinalIgnoreCase => ExpectedArgs.StringArray,
              StringConditionKind.CharLength => ExpectedArgs.Int321OrInt322,
              StringConditionKind.IntegerRange => ExpectedArgs.Int321OrInt322,
              StringConditionKind.CharClass => ExpectedArgs.CharClass,
              StringConditionKind.StartsWithNCharClass => ExpectedArgs.Int32AndCharClass,
              StringConditionKind.EnglishAlphabet => ExpectedArgs.None,
              StringConditionKind.NumbersAndEnglishAlphabet => ExpectedArgs.None,
              StringConditionKind.LowercaseEnglishAlphabet => ExpectedArgs.None,
              StringConditionKind.UppercaseEnglishAlphabet => ExpectedArgs.None,
              StringConditionKind.Hexadecimal => ExpectedArgs.None,
              StringConditionKind.Int32 => ExpectedArgs.None,
              StringConditionKind.Int64 => ExpectedArgs.None,
              StringConditionKind.UInt32 => ExpectedArgs.None,
              StringConditionKind.UInt64 => ExpectedArgs.None,
              StringConditionKind.Guid => ExpectedArgs.None,
              StringConditionKind.StartsWithCharClass => ExpectedArgs.CharClass,
              StringConditionKind.StartsWithCharClassIgnoreCase => ExpectedArgs.CharClass,
              StringConditionKind.EndsWithCharClass => ExpectedArgs.CharClass,
              StringConditionKind.EndsWithCharClassIgnoreCase => ExpectedArgs.CharClass,
            //StringConditionKind.EndsWithSupportedImageExtension => ExpectedArgs.None,
              StringConditionKind.Uninitialized => ExpectedArgs.None,
              StringConditionKind.True => ExpectedArgs.None,
              _ => throw new ArgumentOutOfRangeException(nameof(stringConditionKind), stringConditionKind, null)
         };

   public bool IsMatch(ReadOnlySpan<char> varSpan, int i, int varSpanLength)
   {
       throw new NotImplementedException();
   }
   
   
   // ToString should return function call syntax 
   public override string ToString()
   {
       var name = stringConditionKind.ToDisplayFast() ?? stringConditionKind.ToStringFast();
       var sb = new StringBuilder(name);
       sb.Append('(');
       if (int1.HasValue)
       {
           sb.Append(int1);
       }
       if (int2.HasValue)
       {
           if (sb[^1] != '(')
               sb.Append(",");
           sb.Append(int2);
       }
       // TODO: escape strings properly
       else if (c.HasValue)
       {
           if (sb[^1] != '(')
               sb.Append(",");
           sb.Append(c);
       }
       else if (str != null)
       {
           if (sb[^1] != '(')
               sb.Append(",");
           sb.Append(str);
       }
       else if (strArray != null)
       {
           if (sb[^1] != '(')
               sb.Append(",");
           sb.Append(string.Join("|", strArray));
       }
       else if (charClass != null)
       {
           if (sb[^1] != '(')
               sb.Append(",");
           sb.Append(charClass);
       }
       sb.Append(')');
       return sb.ToString();
   }
}

internal static class StringConditionKindAliases
{
    internal static string NormalizeAliases(string name)
    {
        return name switch
        {
            "int" => "int32",
            "uint" => "uint32",
            "hexadecimal" => "hex",
            "integer" => "int32",
            "long" => "int64",
            "i32" => "int32",
            "u32" => "uint32",
            "u64" => "uint64",
            "i64" => "int64",
            "integer-range" => "range",
            "starts-with-only" => "starts-with-chars",
            "len" => "length",
            "eq" => "equals",
            "" => "equals",
            "starts" => "starts-with",
            "starts-chars" => "starts-with-chars",
            "starts-i" => "starts-with-i",
            "ends" => "ends-with",
            "ends-i" => "ends-with-i",
            "includes" => "contains",
            "includes-i" => "contains-i",
            "lower" => "alpha-lower",
            "upper" => "alpha-upper",
            _ => name
        };
    }
    internal static string GetIgnoreCaseVersion(string name)
    {
        return name switch
        {
            "equals" => "equals-i",
            "starts-with" => "starts-with-i",
            "ends-with" => "ends-with-i",
            "includes" => "includes-i",
            _ => name
        };
    }
}

