namespace Imazen.Routing.Matching;

public record ParsingOptions
{
    /// <summary>
    /// If true, `?` will be allowed as a literal, and the query and path will be concatenated before being matched, and the combination must be completely captured.
    /// QueryParsingOptions will be ignored.
    /// </summary>
    public bool RawQueryAndPath { get; init; } = false;
    /// <summary>
    /// If true, the query keys will be sorted alphabetically before being concatenated and matched.
    /// </summary>
    public bool SortRawQueryValuesFirst { get; init; } = false;
    /// <summary>
    /// If true, matching will be done for any path. No path match expression is required.
    /// </summary>
    public bool IgnorePath { get; init; } = false;
    

    /// <summary>
    /// * `import-accept-header` Searches the accept header for image/webp, image/avif, and image/jxl and translates them to &accept.webp=1, &accept.avif=1, &accept.jxl=1
    /// </summary>
    public bool ImportAcceptHeader {get; init; } = false;

    /// <summary>
    /// If no query matcher is specified, query-prohibit-excess will be respected but nothing else.
    /// 
    /// </summary>
    public QueryParsingOptions QueryParsingOptions { get; init; } = new();
    
    public PathParsingOptions PathParsingOptions { get; init; } = new();

    
    public static ParsingOptions DefaultCaseInsensitive => new()
    {
        QueryParsingOptions = QueryParsingOptions.DefaultCaseInsensitive,
        PathParsingOptions = PathParsingOptions.DefaultCaseInsensitive
    };
    public static ParsingOptions DefaultCaseSensitive => new()
    {
        QueryParsingOptions = QueryParsingOptions.DefaultCaseSensitive,
        PathParsingOptions = PathParsingOptions.DefaultCaseSensitive
    };


    public static ParsingOptions SubtractFromFlags(List<string> flags)
    {
        var context = new ParsingOptions();
        if (flags.Remove("ignore-case") || flags.Remove("i"))
        {
            context = context with { QueryParsingOptions = QueryParsingOptions.DefaultCaseInsensitive, PathParsingOptions = PathParsingOptions.DefaultCaseInsensitive };
        }
        if (flags.Remove("case-sensitive"))
        {
            context = context with { QueryParsingOptions = QueryParsingOptions.DefaultCaseSensitive, PathParsingOptions = PathParsingOptions.DefaultCaseSensitive };
        }
        if (flags.Remove("raw"))
        {
            context = context with { RawQueryAndPath = true };
        }
        if (flags.Remove("sort-raw"))
        {
            context = context with { SortRawQueryValuesFirst = true, RawQueryAndPath = true };
        }
        
        if (flags.Remove("ignore-path"))
        {
            context = context with { IgnorePath = true };
        }

        if (flags.Contains("accept.format"))
        {
            context = context with { ImportAcceptHeader = true };
        }

        context = context with
        {
            QueryParsingOptions = QueryParsingOptions.SubtractFromFlags(flags, context.QueryParsingOptions)
        };
        context = context with
        {
            PathParsingOptions = PathParsingOptions.SubtractFromFlags(flags, context.PathParsingOptions)
        };
        return context;
    }

}