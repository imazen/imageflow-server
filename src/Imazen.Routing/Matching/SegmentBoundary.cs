using System.Diagnostics.CodeAnalysis;

namespace Imazen.Routing.Matching;

internal readonly record struct SegmentBoundary
{
    private readonly SegmentBoundary.Flags Behavior;
    private readonly SegmentBoundary.When On;
    private readonly string? Chars;
    private readonly char Char;


    private SegmentBoundary(
        SegmentBoundary.Flags behavior,
        SegmentBoundary.When on,
        string? chars,
        char c
    )
    {
        this.Behavior = behavior;
        this.On = on;
        this.Chars = chars;
        this.Char = c;
    }
    [Flags]
    private enum SegmentBoundaryFunction
    {
        None = 0,
        Equals = 1,
        StartsWith = 2,
        IgnoreCase = 16,
        IncludeInVar = 32,
        EndingBoundary = 64,
        SegmentOptional = 128,
        FixedLength = 256
    }

    private static SegmentBoundaryFunction FromString(string name, bool useIgnoreCaseVariant, bool segmentOptional)
    {
        var fn= name switch
        {
            "equals" or "" or "eq" => SegmentBoundaryFunction.Equals | SegmentBoundaryFunction.IncludeInVar,
            "starts_with" or "starts-with" or "starts" => SegmentBoundaryFunction.StartsWith | SegmentBoundaryFunction.IncludeInVar,
            "ends_with" or "ends-with" or "ends" => SegmentBoundaryFunction.StartsWith | SegmentBoundaryFunction.IncludeInVar | SegmentBoundaryFunction.EndingBoundary,
            "prefix" => SegmentBoundaryFunction.StartsWith,
            "suffix" => SegmentBoundaryFunction.StartsWith | SegmentBoundaryFunction.EndingBoundary,
            "len" or "length" => SegmentBoundaryFunction.FixedLength | SegmentBoundaryFunction.EndingBoundary | SegmentBoundaryFunction.IncludeInVar,
            _ => SegmentBoundaryFunction.None
        };
        if (fn == SegmentBoundaryFunction.None)
        {
            return fn;
        }
        if (useIgnoreCaseVariant)
        {
            fn |= SegmentBoundaryFunction.IgnoreCase;
        }
        if (segmentOptional)
        {
            if (fn == SegmentBoundaryFunction.FixedLength)
            {
                // When a fixed length segment is optional, we don't make a end boundary for it.
                return SegmentBoundaryFunction.None;
            }
            fn |= SegmentBoundaryFunction.SegmentOptional;
        }
        return fn;
    }

    public static SegmentBoundary Literal(ReadOnlySpan<char> literal, bool ignoreCase) =>
        StringEquals(literal, ignoreCase, false);
    
    

    public static SegmentBoundary LiteralEnd = new(Flags.EndingBoundary, When.SegmentFullyMatchedByStartBoundary, null, '\0');

    public bool HasDefaultStartWhen => On == When.StartsNow;
    public static SegmentBoundary DefaultStart = new(Flags.IncludeMatchingTextInVariable, When.StartsNow, null, '\0');
    public bool HasDefaultEndWhen => On == When.InheritFromNextSegment;
    public static SegmentBoundary DefaultEnd = new(Flags.EndingBoundary, When.InheritFromNextSegment, null, '\0');
    public static SegmentBoundary EqualsEnd = new(Flags.EndingBoundary, When.SegmentFullyMatchedByStartBoundary, null, '\0');
    
    public bool IsOptional => (Behavior & Flags.SegmentOptional) == Flags.SegmentOptional;


    public bool IncludesMatchingTextInVariable =>
        (Behavior & Flags.IncludeMatchingTextInVariable) == Flags.IncludeMatchingTextInVariable;

    public bool IsEndingBoundary =>
        (Behavior & Flags.EndingBoundary) == Flags.EndingBoundary;

    public bool SupportsScanning =>
        On != When.StartsNow &&
        SupportsMatching;

    public bool SupportsMatching =>
        On != When.InheritFromNextSegment &&
        On != When.SegmentFullyMatchedByStartBoundary;

    public bool MatchesEntireSegment =>
        On == When.EqualsOrdinal || On == When.EqualsOrdinalIgnoreCase || On == When.EqualsChar;

    public string? AsCaseSensitiveLiteral =>
        this.Behavior == Flags.None ?
            On switch
            {
                When.EqualsOrdinal => Chars,
                When.EqualsChar => Char.ToString(),
                _ => null
            } : null;

    public bool IsLiteralEnd => Behavior == Flags.EndingBoundary && On == When.SegmentFullyMatchedByStartBoundary &&
                                Char == '\0' && Chars == null;
    public SegmentBoundary SetOptional(bool optional)
        => new(optional ? Flags.SegmentOptional | Behavior : Behavior ^ Flags.SegmentOptional, On, Chars, Char);


    public bool AsEndSegmentReliesOnStartSegment =>
        On == When.SegmentFullyMatchedByStartBoundary;

    public bool AsEndSegmentReliesOnSubsequentSegmentBoundary =>
        On == When.InheritFromNextSegment;


    
    public static bool TryCreate(string function, bool useIgnoreCase, bool segmentOptional, ReadOnlySpan<char> arg0,
        [NotNullWhen(true)] out SegmentBoundary? result)
    {
        var fn = FromString(function, useIgnoreCase, segmentOptional);
        if (fn == SegmentBoundaryFunction.None)
        {
            result = null;
            return false;
        }
        return TryCreate(fn, arg0, out result);
    }

    private static bool TryCreate(SegmentBoundaryFunction function, ReadOnlySpan<char> arg0, out SegmentBoundary? result)
    {
        var argType = ExpressionParsingHelpers.GetArgType(arg0);
        
        if ((argType & ExpressionParsingHelpers.ArgType.String) == 0)
        {
            result = null;
            return false;
        }

        var includeInVar = (function & SegmentBoundaryFunction.IncludeInVar) == SegmentBoundaryFunction.IncludeInVar;
        var ignoreCase = (function & SegmentBoundaryFunction.IgnoreCase) == SegmentBoundaryFunction.IgnoreCase;
        var startsWith = (function & SegmentBoundaryFunction.StartsWith) == SegmentBoundaryFunction.StartsWith;
        var equals = (function & SegmentBoundaryFunction.Equals) == SegmentBoundaryFunction.Equals;
        var segmentOptional = (function & SegmentBoundaryFunction.SegmentOptional) == SegmentBoundaryFunction.SegmentOptional;
        var endingBoundary = (function & SegmentBoundaryFunction.EndingBoundary) == SegmentBoundaryFunction.EndingBoundary;
        var segmentFixedLength = (function & SegmentBoundaryFunction.FixedLength) == SegmentBoundaryFunction.FixedLength;
        if (startsWith)
        {
            result = StartWith(arg0, ignoreCase, includeInVar, endingBoundary).SetOptional(segmentOptional);
            return true;
        }
        if (equals)
        {
            if (endingBoundary) throw new InvalidOperationException("Equals cannot be an ending boundary");
            result = StringEquals(arg0, ignoreCase, includeInVar).SetOptional(segmentOptional);
            return true;
        }
        if (segmentFixedLength)
        {
            if (segmentOptional)
            {
                // We don't support optional fixed length segments at this time.
                result = null;
                return false;
            }
            // len requires a number
            if ((argType & ExpressionParsingHelpers.ArgType.UnsignedInteger) > 0)
            {
                //parse the number into char
                var len = int.Parse(arg0.ToString());
                result = FixedLengthEnd(len);
                return true;
            }
            result = null;
            return false;
        }
        throw new InvalidOperationException("Unreachable code");
    }
        
    private static SegmentBoundary StartWith(ReadOnlySpan<char> asSpan, bool ordinalIgnoreCase, bool includeInVar,bool endingBoundary)
    {
        var flags = includeInVar ? Flags.IncludeMatchingTextInVariable : Flags.None;
        if (endingBoundary)
        {
            flags |= Flags.EndingBoundary;
        }
        var useCaseInsensitive = ordinalIgnoreCase && ExpressionParsingHelpers.HasAzOrNonAsciiLetters(asSpan);
        if (asSpan.Length == 1 &&
            !useCaseInsensitive)
        {
            return new(flags,
                When.AtChar, null, asSpan[0]);
        }

        return new(flags,
            useCaseInsensitive ? When.AtStringIgnoreCase : When.AtString, asSpan.ToString(), '\0');
    }
    
    private static SegmentBoundary StringEquals(ReadOnlySpan<char> asSpan, bool ordinalIgnoreCase, bool includeInVar)
    {
        var useCaseInsensitive = ordinalIgnoreCase && ExpressionParsingHelpers.HasAzOrNonAsciiLetters(asSpan);
        if (asSpan.Length == 1 && !useCaseInsensitive)
        {
            return new(includeInVar ? Flags.IncludeMatchingTextInVariable : Flags.None,
                When.EqualsChar, null, asSpan[0]);
        }

        return new(includeInVar ? Flags.IncludeMatchingTextInVariable : Flags.None,
            useCaseInsensitive ? When.EqualsOrdinalIgnoreCase : When.EqualsOrdinal, asSpan.ToString(), '\0');
    }

 
    private static SegmentBoundary FixedLengthEnd(int length)
    {
        if (length < 1) throw new ArgumentOutOfRangeException(nameof(length)
            , "Fixed length must be greater than 0");
        if (length > char.MaxValue) throw new ArgumentOutOfRangeException(nameof(length)
            , "Fixed length must be less than or equal to " + char.MaxValue);
        return new SegmentBoundary(Flags.IncludeMatchingTextInVariable | Flags.EndingBoundary,
            When.FixedLength
            , null, (char)length);
    }
    [Flags]
    private enum Flags : byte
    {
        None = 0,
        SegmentOptional = 1,
        IncludeMatchingTextInVariable = 4,
        EndingBoundary = 64,
    }


    private enum When : byte
    {
        /// <summary>
        /// Cannot be combined with Optional.
        /// Cannot be used for determining the end of a segment.
        /// 
        /// </summary>
        StartsNow,
        EndOfInput,
        SegmentFullyMatchedByStartBoundary,

        /// <summary>
        /// The default for ends
        /// </summary>
        InheritFromNextSegment,
        AtChar,
        AtString,
        AtStringIgnoreCase,
        EqualsOrdinal,
        EqualsChar,
        EqualsOrdinalIgnoreCase,
        FixedLength,
    }


    public bool TryMatch(ReadOnlySpan<char> text, out int start, out int end)
    {
        if (!SupportsMatching)
        {
            throw new InvalidOperationException("Cannot match a segment boundary with " + On);
        }

        start = 0;
        end = 0;
        if (On == When.EndOfInput)
        {
            return text.Length == 0;
        }

        if (On == When.StartsNow)
        {
            return true;
        }

        if (text.Length == 0) return false;
        switch (On)
        {
            case When.FixedLength:
                if (text.Length >= this.Char)
                {
                    start = 0;
                    end = this.Char;
                    return true;
                }
                return false;
            case When.AtChar or When.EqualsChar:
                if (text[0] == Char)
                {
                    start = 0;
                    end = 1;
                    return true;
                }

                return false;

            case When.AtString or When.EqualsOrdinal:
                var charSpan = Chars.AsSpan();
                if (text.StartsWith(charSpan, StringComparison.Ordinal))
                {
                    start = 0;
                    end = charSpan.Length;
                    return true;
                }

                return true;
            case When.AtStringIgnoreCase or When.EqualsOrdinalIgnoreCase:
                var charSpan2 = Chars.AsSpan();
                if (text.StartsWith(charSpan2, StringComparison.OrdinalIgnoreCase))
                {
                    start = 0;
                    end = charSpan2.Length;
                    return true;
                }

                return true;
            default:
                return false;
        }
    }

    public bool TryScan(ReadOnlySpan<char> text, out int start, out int end)
    {
        if (!SupportsScanning)
        {
            throw new InvalidOperationException("Cannot scan a segment boundary with " + On);
        }

        // Like TryMatch, but searches for the first instance of the boundary
        start = 0;
        end = 0;
        if (On == When.EndOfInput)
        {
            start = end = text.Length;
            return true;
        }

        if (text.Length == 0) return false;
        switch (On)
        {
            case When.FixedLength:
                if (text.Length >= this.Char)
                {
                    start = this.Char;
                    end = this.Char;
                    return true;
                }
                return false;
            case When.AtChar or When.EqualsChar:
                var index = text.IndexOf(Char);
                if (index == -1) return false;
                start = index;
                end = index + 1;
                return true;
            case When.AtString or When.EqualsOrdinal:
                var searchSpan = Chars.AsSpan();
                var searchIndex = text.IndexOf(searchSpan);
                if (searchIndex == -1) return false;
                start = searchIndex;
                end = searchIndex + searchSpan.Length;
                return true;
            case When.AtStringIgnoreCase or When.EqualsOrdinalIgnoreCase:
                var searchSpanIgnoreCase = Chars.AsSpan();
                var searchIndexIgnoreCase = text.IndexOf(searchSpanIgnoreCase, StringComparison.OrdinalIgnoreCase);
                if (searchIndexIgnoreCase == -1) return false;
                start = searchIndexIgnoreCase;
                end = searchIndexIgnoreCase + searchSpanIgnoreCase.Length;
                return true;
            default:
                return false;
        }


    }
    public string? MatchString => On switch
    {
        When.AtChar or When.EqualsChar => Char.ToString(),
        When.AtString or When.AtStringIgnoreCase or
            When.EqualsOrdinal or When.EqualsOrdinalIgnoreCase => Chars,
        _ => null
    };
    public override string ToString()
    {
        var isStartBoundary = Flags.EndingBoundary == (Behavior & Flags.EndingBoundary);
        var name = On switch
        {
            When.StartsNow => "now",
            When.EndOfInput => ">",
            When.SegmentFullyMatchedByStartBoundary => "noop",
            When.InheritFromNextSegment => ">",
            When.AtChar or When.AtString or When.AtStringIgnoreCase =>
                ((Behavior & Flags.IncludeMatchingTextInVariable) != 0)
                    ? (isStartBoundary ? "starts" : "ends")
                    : (isStartBoundary ? "prefix" : "suffix"),
            When.EqualsOrdinal or When.EqualsChar or When.EqualsOrdinalIgnoreCase => "eq",
            When.FixedLength => $"len",
            _ => throw new InvalidOperationException("Unreachable code")
        };
        var ignoreCase = On is When.AtStringIgnoreCase or When.EqualsOrdinalIgnoreCase ? "-i" : "";
        var optional = (Behavior & Flags.SegmentOptional) != 0 ? "?": "";
        if (On == When.FixedLength)
        {
            return $"{name}{ignoreCase}({(int)Char}){optional}";
        }
        if (Chars != null)
        {
            name = $"{name}{ignoreCase}({Chars}){optional}";
        }
        else if (Char != '\0')
        {
            name = $"{name}{ignoreCase}({Char}){optional}";
        }
        return $"{name}{optional}";
    }
}