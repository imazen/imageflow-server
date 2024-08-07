using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Imazen.Routing.Matching.Templating;

public record MultiTemplate(StringTemplate? PathTemplate,
IReadOnlyDictionary<string, StringTemplate>? QueryTemplates,
MultiTemplateOptions? Options, ExpressionFlags? UnusedFlags)
{
    public static MultiTemplate Parse(ReadOnlyMemory<char> expressionWithFlags)
    {
        if (!MultiTemplate.TryParse(expressionWithFlags, out var result, out var error))
        {
            throw new ArgumentException(error, nameof(expressionWithFlags));
        }

        return result!;
    }

    public static bool TryParse(ReadOnlyMemory<char> expressionWithFlags,
        [NotNullWhen(true)] out MultiTemplate? result, [NotNullWhen(false)] out string? error)
    {
        if (!ExpressionFlags.TryParseFromEnd(expressionWithFlags, out var expression, out var flags, out error))
        {
            result = null;
            return false;
        }

        var allFlags = flags ?? new List<string>();
        var context = ParsingOptions.SubtractFromFlags(allFlags);

        if (!MultiTemplate.TryParseWithSmartQuery(context, expression, out var pathTemplate, out var queryTemplates,
                out error))
        {
            result = null;
            return false;
        }
        // TODO: fix flag parsing
        result = new MultiTemplate(pathTemplate, queryTemplates, new MultiTemplateOptions(),
            new ExpressionFlags(new ReadOnlyCollection<string>(allFlags)));
        error = result.GetValidationErrors();
        if (error != null)
        {
            result = null;
            return false;
        }

        return true;
    }

    private static bool TryParseWithSmartQuery(ParsingOptions context, ReadOnlyMemory<char> expressionWithoutFlags, 
        out StringTemplate? pathTemplate, out IReadOnlyDictionary<string, StringTemplate>? queryTemplates 
        , [NotNullWhen(false)] out string? error)
    {
        throw new NotImplementedException();
    }

    public string? GetValidationErrors()
    {
        
        //TODO: wrong
        if (PathTemplate == null)
        {
            return "Path template is required";
        }

        if (QueryTemplates != null && QueryTemplates.Any(x => x.Value == null))
        {
            return "Query templates must not be null";
        }

        return null;
    }
    
    

}