namespace Imazen.Routing.Matching;

internal static class StringConditionMatchingHelpers
{
    internal static bool IsEnglishAlphabet(this ReadOnlySpan<char> chars)
    {
        foreach (var c in chars)
        {
            switch (c)
            {
                case >= 'a' and <= 'z':
                case >= 'A' and <= 'Z':
                    continue;
                default:
                    return false;
            }
        }
        return true;
    }
    internal static bool IsNumbersAndEnglishAlphabet(this ReadOnlySpan<char> chars)
    {
        foreach (var c in chars)
        {
            switch (c)
            {
                case >= 'a' and <= 'z':
                case >= 'A' and <= 'Z':
                case >= '0' and <= '9':
                    continue;
                default:
                    return false;
            }
        }
        return true;
    }
    internal static bool IsLowercaseEnglishAlphabet(this ReadOnlySpan<char> chars)
    {
        foreach (var c in chars)
        {
            switch (c)
            {
                case >= 'a' and <= 'z':
                    continue;
                default:
                    return false;
            }
        }
        return true;
    }
    internal static bool IsUppercaseEnglishAlphabet(this ReadOnlySpan<char> chars)
    {
        foreach (var c in chars)
        {
            switch (c)
            {
                case >= 'A' and <= 'Z':
                    continue;
                default:
                    return false;
            }
        }
        return true;
    }
    internal static bool IsHexadecimal(this ReadOnlySpan<char> chars)
    {
        foreach (var c in chars)
        {
            switch (c)
            {
                case >= 'a' and <= 'f':
                case >= 'A' and <= 'F':
                case >= '0' and <= '9':
                    continue;
                default:
                    return false;
            }
        }
        return true;
    }
#if NET6_0_OR_GREATER
    internal static bool IsInt32(this ReadOnlySpan<char> chars) => int.TryParse(chars, out _);
    internal static bool IsU32(this ReadOnlySpan<char> chars) => uint.TryParse(chars, out _);
    internal static bool IsInt64(this ReadOnlySpan<char> chars) => long.TryParse(chars, out _);
    internal static bool IsU64(this ReadOnlySpan<char> chars) => ulong.TryParse(chars, out _);
    internal static bool IsInIntegerRangeInclusive(this ReadOnlySpan<char> chars, int? min, int? max)
    {
        if (!int.TryParse(chars, out var value)) return false;
        if (min.HasValue ^ max.HasValue) return value == min || value == max;
        if (min != null && value < min) return false;
        if (max != null && value > max) return false;
        return true;
    }
    internal static bool IsGuid(this ReadOnlySpan<char> chars) => Guid.TryParse(chars, out _);
#else
    internal static bool IsGuid(this ReadOnlySpan<char> chars) => Guid.TryParse(chars.ToString(), out _);
    internal static bool IsInt32(this ReadOnlySpan<char> chars) => int.TryParse(chars.ToString(), out _);
    internal static bool IsU32(this ReadOnlySpan<char> chars) => uint.TryParse(chars.ToString(), out _);
    internal static bool IsInt64(this ReadOnlySpan<char> chars) => long.TryParse(chars.ToString(), out _);
    internal static bool IsU64(this ReadOnlySpan<char> chars) => ulong.TryParse(chars.ToString(), out _);
    internal static bool IsInIntegerRangeInclusive(this ReadOnlySpan<char> chars, int? min, int? max)
    {
        if (!int.TryParse(chars.ToString(), out var value)) return false;
        if (min.HasValue ^ max.HasValue) return value == min || value == max;
        if (min != null && value < min) return false;
        if (max != null && value > max) return false;
        return true;
    }
#endif
    
    internal static bool IsOnlyCharsInclusive(this ReadOnlySpan<char> chars, string[]? allowedChars)
    {
        if (allowedChars == null) return false;
        foreach (var c in chars)
        {
            if (!allowedChars.Any(x => x.Contains(c))) return false;
        }
        return true;
    }
    internal static bool C(this ReadOnlySpan<char> chars, string[]? disallowedChars)
    {
        if (disallowedChars == null) return false;
        foreach (var c in chars)
        {
            if (disallowedChars.Any(x => x.Contains(c))) return false;
        }
        return true;
    }
    internal static bool LengthWithinInclusive(this ReadOnlySpan<char> chars, int? min, int? max)
    {
        if (min.HasValue ^ max.HasValue) return chars.Length == min || chars.Length == max;
        if (min != null && chars.Length < min) return false;
        if (max != null && chars.Length > max) return false;
        return true;
    }
    internal static bool EqualsOrdinal(this ReadOnlySpan<char> chars, string value) => chars.Equals(value.AsSpan(), StringComparison.Ordinal);
    internal static bool EqualsOrdinalIgnoreCase(this ReadOnlySpan<char> chars, string value) => chars.Equals(value.AsSpan(), StringComparison.OrdinalIgnoreCase);
    
    
    internal static bool StartsWithChar(this ReadOnlySpan<char> chars, char c) => chars.Length > 0 && chars[0] == c;
    internal static bool StartsWithOrdinal(this ReadOnlySpan<char> chars, string value) => chars.StartsWith(value.AsSpan(), StringComparison.Ordinal);
    internal static bool StartsWithOrdinalIgnoreCase(this ReadOnlySpan<char> chars, string value) => chars.StartsWith(value.AsSpan(), StringComparison.OrdinalIgnoreCase);

    internal static bool StartsWithAnyOrdinal(this ReadOnlySpan<char> chars, string[] values)
    {
        foreach (var value in values)
        {
            if (chars.StartsWith(value.AsSpan(), StringComparison.Ordinal)) return true;
        }
        return false;
    }
    internal static bool StartsWithAnyOrdinalIgnoreCase(this ReadOnlySpan<char> chars, string[] values)
    {
        foreach (var value in values)
        {
            if (chars.StartsWith(value.AsSpan(), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
    
    internal static bool EqualsAnyOrdinal(this ReadOnlySpan<char> chars, string[] values)
    {
        foreach (var value in values)
        {
            if (chars.Equals(value.AsSpan(), StringComparison.Ordinal)) return true;
        }
        return false;
    }
    internal static bool EqualsAnyOrdinalIgnoreCase(this ReadOnlySpan<char> chars, string[] values)
    {
        foreach (var value in values)
        {
            if (chars.Equals(value.AsSpan(), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
    
    internal static bool EndsWithChar(this ReadOnlySpan<char> chars, char c) => chars.Length > 0 && chars[^1] == c;
    internal static bool EndsWithOrdinal(this ReadOnlySpan<char> chars, string value) => chars.EndsWith(value.AsSpan(), StringComparison.Ordinal);
    internal static bool EndsWithOrdinalIgnoreCase(this ReadOnlySpan<char> chars, string value) => chars.EndsWith(value.AsSpan(), StringComparison.OrdinalIgnoreCase);
    internal static bool EndsWithAnyOrdinal(this ReadOnlySpan<char> chars, string[] values)
    {
        foreach (var value in values)
        {
            if (chars.EndsWith(value.AsSpan(), StringComparison.Ordinal)) return true;
        }
        return false;
    }
    internal static bool EndsWithAnyOrdinalIgnoreCase(this ReadOnlySpan<char> chars, string[] values)
    {
        foreach (var value in values)
        {
            if (chars.EndsWith(value.AsSpan(), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
    internal static bool IncludesOrdinal(this ReadOnlySpan<char> chars, string value) => chars.IndexOf(value.AsSpan(), StringComparison.Ordinal) != -1;
    internal static bool IncludesOrdinalIgnoreCase(this ReadOnlySpan<char> chars, string value) => chars.IndexOf(value.AsSpan(), StringComparison.OrdinalIgnoreCase) != -1;
    internal static bool IncludesAnyOrdinal(this ReadOnlySpan<char> chars, string[] values)
    {
        foreach (var value in values)
        {
            if (chars.IndexOf(value.AsSpan(), StringComparison.Ordinal) != -1) return true;
        }
        return false;
    }
    internal static bool IncludesAnyOrdinalIgnoreCase(this ReadOnlySpan<char> chars, string[] values)
    {
        foreach (var value in values)
        {
            if (chars.IndexOf(value.AsSpan(), StringComparison.OrdinalIgnoreCase) != -1) return true;
        }
        return false;
    }
    internal static bool IsCharClass(this ReadOnlySpan<char> chars, CharacterClass charClass)
    {
        foreach (var c in chars)
        {
            if (!charClass.Contains(c)) return false;
        }
        return true;
    }
    internal static bool StartsWithNCharClass(this ReadOnlySpan<char> chars, CharacterClass charClass, int n)
    {
        if (chars.Length < n) return false;
        for (int i = 0; i < n; i++)
        {
            if (!charClass.Contains(chars[i])) return false;
        }
        return true;
    }
    internal static bool StartsWithCharClass(this ReadOnlySpan<char> chars, CharacterClass charClass)
    {
        return chars.Length != 0 && charClass.Contains(chars[0]);
    }
    internal static bool EndsWithCharClass(this ReadOnlySpan<char> chars, CharacterClass charClass)
    {
        return chars.Length != 0 && charClass.Contains(chars[^1]);
    }
    internal static bool EndsWithSupportedImageExtension(this MatchingContext context, ReadOnlySpan<char> chars)
    {
        foreach (var ext in context.SupportedImageExtensions)
        {
            if (chars.EndsWith(ext.AsSpan(), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
    
}