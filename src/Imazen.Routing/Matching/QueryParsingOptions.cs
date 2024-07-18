namespace Imazen.Routing.Matching;

public record QueryParsingOptions
{
    public bool KeysOrdinalIgnoreCase { get; init; } = false;
    public bool ValuesOrdinalIgnoreCase { get; init; } = false;
    public bool AllowStarLiteral { get; init; } = false;
    //public bool QueryValuesCaptureSlashes { get; init; } = false;
    public bool ProhibitExcessQueryKeys { get; init; } = false;

    
    internal ExpressionParsingOptions ToExpressionParsingOptions()
    {
        return new()
        {
            OrdinalIgnoreCase = ValuesOrdinalIgnoreCase,
            AllowStarLiteral = AllowStarLiteral,
            AllowQuestionLiteral = true,
            //CaptureSlashesByDefault = QueryValuesCaptureSlashes
        };
    }
    public static QueryParsingOptions DefaultCaseInsensitive => new()
    {
        KeysOrdinalIgnoreCase = true,
        ValuesOrdinalIgnoreCase = true,
    };
    
    public static QueryParsingOptions DefaultCaseSensitive => new()
    {
        KeysOrdinalIgnoreCase = false,
        ValuesOrdinalIgnoreCase = false,
    };

    public static QueryParsingOptions SubtractFromFlags(List<string> flags, QueryParsingOptions defaults)
    {
        if (flags.Remove("query-ignore-case"))
        {
            defaults = defaults with { KeysOrdinalIgnoreCase = true, ValuesOrdinalIgnoreCase = true };
        }

        if (flags.Remove("query-keys-ignore-case"))
        {
            defaults = defaults with { KeysOrdinalIgnoreCase = true };
        }

        if (flags.Remove("query-values-ignore-case"))
        {
            defaults = defaults with { ValuesOrdinalIgnoreCase = true };
        }

        // if (flags.Remove("query-values-capture-slashes"))
        // {
        //     defaults = defaults with { QueryValuesCaptureSlashes = true };
        // }

        if (flags.Remove("query-prohibit-excess"))
        {
            defaults = defaults with { ProhibitExcessQueryKeys = true };
        }


        if (flags.Remove("allow-star-literal"))
        {
            defaults = defaults with { AllowStarLiteral = true };
        }

        return defaults;
    }
}