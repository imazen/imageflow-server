using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Imazen.Routing.Matching; // For ExpressionParsingHelpers and ExpressionFlags

namespace Imazen.Routing.Matching.Templating;

/// <summary>
/// Represents a parsed URL template, including path and query string components.
/// </summary>
public record MultiTemplate(StringTemplate? PathTemplate,
    IReadOnlyList<(StringTemplate KeyTemplate, StringTemplate ValueTemplate)>? QueryTemplates,
    MultiTemplateOptions? Options, // Placeholder for future options
    ExpressionFlags? Flags, // Added Flags property
    string? Scheme
    )
{
    public string? GetTemplateLiteralStart()
    {
        return PathTemplate?.GetStartLiteral();
    }
    public static MultiTemplate Parse(ReadOnlyMemory<char> expression, TemplateValidationContext? validationContext)
    {
        if (!TryParse(expression, null, validationContext, out var result, out var error))
        {
            throw new ArgumentException(error, nameof(expression));
        }
        return result!;
    }

    public static bool TryParseRemoveFlags(
        ReadOnlyMemory<char> expressionWithFlags,
        out ReadOnlyMemory<char> remainingExpression,
        out ExpressionFlags? result,
        [NotNullWhen(false)] out string? error)
    {
        if (!ExpressionFlags.TryParseFromEnd(expressionWithFlags, out remainingExpression, out result, out error,
         ExpressionFlagParsingOptions.Permissive))
        {
            result = null;
            return false;
        }
        return true;
    }
    public static bool TryParse(
        ReadOnlyMemory<char> expressionWithFlags,
        ExpressionFlags? flagsAlreadyParsed,
        TemplateValidationContext? validationContext,
        [NotNullWhen(true)] out MultiTemplate? result,
        [NotNullWhen(false)] out string? error)
    {
        if (!ExpressionFlags.TryParseFromEnd(expressionWithFlags, out var expressionWithoutFlags, out var newFlags, out error
            , ExpressionFlagParsingOptions.Permissive))
        {
            result = null;
            return false;
        }
        var templateFlags = ExpressionFlags.Combine(flagsAlreadyParsed, newFlags);

        TemplateValidationContext? currentValidationContext = null;
        if (validationContext != null)
        {
            currentValidationContext = validationContext with { TemplateFlags = templateFlags };
        }

        ReadOnlyMemory<char> pathPart;
        ReadOnlyMemory<char> queryPart;

        // TODO: this is weak
        int queryStartIndex = ExpressionParsingHelpers.FindCharNotEscapedAtDepth0(expressionWithoutFlags.Span, '?', '\\', '{', '}');

        if (queryStartIndex != -1)
        {
            pathPart = expressionWithoutFlags[..queryStartIndex];
            queryPart = expressionWithoutFlags[(queryStartIndex + 1)..];
        }
        else
        {
            pathPart = expressionWithoutFlags;
            queryPart = ReadOnlyMemory<char>.Empty;
        }

        StringTemplate? pathTemplate = null;
        string? scheme = null;
        if (!pathPart.IsEmpty)
        {
            if (!StringTemplate.TryParse(pathPart.Span, currentValidationContext, out pathTemplate, out error))
            {
                 error = $"Failed to parse path template part: {error}";
                result = null;
                return false;
            }
            // parse scheme from pathPart
            if (!ExpressionParsingHelpers.TryParseScheme(pathPart.Span, currentValidationContext?.RequireSchemeForPaths ?? false, out scheme, out var schemeError))
            {
                error = schemeError;
                result = null;
                return false;
            }
            if (scheme != null)
            {
                if (currentValidationContext?.AllowedSchemes != null && !currentValidationContext.AllowedSchemes.Contains(scheme))
                {
                    error = $"Scheme '{scheme}' is not allowed. Use one of {string.Join(", ", currentValidationContext.AllowedSchemes)}";
                    result = null;
                    return false;
                }
            }
        }else if (currentValidationContext?.RequirePath ?? false)
        {
            error = "Template must include the URI path part.";
            result = null;
            return false;
        }

        List<(StringTemplate Key, StringTemplate Value)>? queryTemplates = null;
        if (!queryPart.IsEmpty)
        {
            queryTemplates = new List<(StringTemplate Key, StringTemplate Value)>();
            int currentPos = 0;
            var querySpan = queryPart.Span;

            while (currentPos < querySpan.Length)
            {
                int nextAmpersand = ExpressionParsingHelpers.FindCharNotEscapedAtDepth0(querySpan.Slice(currentPos), '&', '\\', '{', '}');
                int endOfPair = (nextAmpersand == -1) ? querySpan.Length : currentPos + nextAmpersand;
                var pairSpan = querySpan[currentPos..endOfPair];

                int equalsIndex = ExpressionParsingHelpers.FindCharNotEscapedAtDepth0(pairSpan, '=', '\\', '{', '}');
                ReadOnlySpan<char> keySpan = (equalsIndex == -1) ? pairSpan : pairSpan[..equalsIndex];
                ReadOnlySpan<char> valueSpan = (equalsIndex == -1) ? [] : pairSpan[(equalsIndex + 1)..];

                if (!StringTemplate.TryParse(keySpan, currentValidationContext, out var keyTemplate, out error))
                {
                    result = null; error = $"Failed to parse query key template near '{keySpan.ToString()}': {error}"; return false;
                }
                if (!StringTemplate.TryParse(valueSpan, currentValidationContext, out var valueTemplate, out error))
                {
                    result = null; error = $"Failed to parse query value template near '{valueSpan.ToString()}': {error}"; return false;
                }
                queryTemplates.Add((Key: keyTemplate, Value: valueTemplate));

                if (nextAmpersand == -1) break;
                currentPos = endOfPair + 1;
            }
        }

        result = new MultiTemplate(pathTemplate, queryTemplates, null, templateFlags, scheme);
        error = result.GetValidationErrors();
        if (error != null)
        {
            result = null;
            return false;
        }

        return true;
    }

    public string? GetValidationErrors()
    {
        if (PathTemplate == null && (QueryTemplates == null || QueryTemplates.Count == 0))
        {
            // Allow empty template for now.
        }
        return null;
    }
    public bool TryEvaluatePath(IDictionary<string, string> variables, [NotNullWhen(true)] out string? pathResult, [NotNullWhen(false)] out string? error)
    {
        pathResult = PathTemplate?.Evaluate(variables, out _) ?? "";
        var pathStartLiteral = PathTemplate?.GetStartLiteral();
        if (pathStartLiteral != null && !pathResult.StartsWith(pathStartLiteral))
        {
            pathResult = null;
            error = "Evaluated path template does not start with expected literal";
            return false;
        }
        if (!TemplateSafety.IsPathSafe(pathResult.AsSpan(), PathTemplate?.GetStartLiteral(), out error))
        {
            pathResult = null;
            return false;
        }
        error = null;
        return true;
    }
    
    public bool TryEvaluateToCombinedString(IDictionary<string, string> variables, 
        [NotNullWhen(true)] out string? result, [NotNullWhen(false)] out string? error)
    {
        if (!TryEvaluatePath(variables, out var pathResult, out error))
        {
            result = null;
            return false;
        }
        var sb = new StringBuilder();
        bool firstQueryParam = true;

        if (QueryTemplates != null)
        {
            foreach (var (keyTemplate, valueTemplate) in QueryTemplates)
            {
                var keyResult = keyTemplate.Evaluate(variables, out bool keyOptionalEmpty);
                var valueResult = valueTemplate.Evaluate(variables, out bool valueOptionalEmpty);

                if (keyOptionalEmpty || valueOptionalEmpty)
                {
                    continue;
                }

                sb.Append(firstQueryParam ? '?' : '&');
                firstQueryParam = false;

                sb.Append(keyResult);
                sb.Append('=');
                sb.Append(valueResult);
            }
        }
        result = pathResult + sb.ToString();
        error = null;
        return true;
    }

    public bool TryEvaluateToPathAndQuery(IDictionary<string, string> variables, out string? pathResult, 
    out string? queryResult, out List<KeyValuePair<string?, string?>>? queryPairs, [NotNullWhen(false)] out string? error)
    {
        // TODO: maybe flag security errors for issue logging?
        queryPairs = null;
        queryResult = null;
        if (!TryEvaluatePath(variables, out pathResult, out error))
        {
            return false;
        }

        if (QueryTemplates != null)
        {
            queryPairs = new List<KeyValuePair<string?, string?>>(QueryTemplates?.Count ?? 0);
            var sb = new StringBuilder();
            bool firstQueryParam = true;
            foreach (var (keyTemplate, valueTemplate) in QueryTemplates)
            {
                var keyResult = keyTemplate.Evaluate(variables, out bool keyOptionalEmpty);
                var valueResult = valueTemplate.Evaluate(variables, out bool valueOptionalEmpty);

                if (keyOptionalEmpty || valueOptionalEmpty)
                {
                    continue;
                }

                queryPairs.Add(new KeyValuePair<string?, string?>(keyResult, valueResult));
                sb.Append(firstQueryParam ? '?' : '&');
                firstQueryParam = false;

                sb.Append(keyResult);
                sb.Append('=');
                sb.Append(valueResult);
            }
            queryResult = sb.ToString();
        }
        error = null;
        return true;
    }
    public string Evaluate(IDictionary<string, string> variables)
    {
        if (!TryEvaluateToCombinedString(variables, out var result, out var error))
        {
            throw new ArgumentException(error);
        }
        return result!;
    }

    public static bool TryParse(ReadOnlyMemory<char> expressionWithFlags,
        [NotNullWhen(true)] out MultiTemplate? result,
        [NotNullWhen(false)] out string? error)
    {
        return TryParse(expressionWithFlags,null,null, out result, out error);
    }
}

