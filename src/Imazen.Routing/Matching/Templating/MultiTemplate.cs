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
    ExpressionFlags? Flags // Added Flags property
    )
{
    public static MultiTemplate Parse(ReadOnlyMemory<char> expression)
    {
        if (!TryParse(expression, null, out var result, out var error))
        {
            throw new ArgumentException(error, nameof(expression));
        }
        return result!;
    }

    public static bool TryParse(
        ReadOnlyMemory<char> expressionWithFlags,
        TemplateValidationContext? validationContext,
        [NotNullWhen(true)] out MultiTemplate? result,
        [NotNullWhen(false)] out string? error)
    {
        if (!ExpressionFlags.TryParseFromEnd(expressionWithFlags, out var expressionWithoutFlags, out var flagsList, out error))
        {
            result = null;
            return false;
        }
        var templateFlags = flagsList.Count > 0 ? new ExpressionFlags(new System.Collections.ObjectModel.ReadOnlyCollection<string>(flagsList)) : null;

        TemplateValidationContext? currentValidationContext = null;
        if (validationContext != null)
        {
            currentValidationContext = validationContext with { TemplateFlags = templateFlags };
        }

        ReadOnlyMemory<char> pathPart;
        ReadOnlyMemory<char> queryPart;

        int queryStartIndex = ExpressionParsingHelpers.FindCharNotEscaped(expressionWithoutFlags.Span, '?', '\\');

        if (queryStartIndex != -1)
        {
            pathPart = expressionWithoutFlags.Slice(0, queryStartIndex);
            queryPart = expressionWithoutFlags.Slice(queryStartIndex + 1);
        }
        else
        {
            pathPart = expressionWithoutFlags;
            queryPart = ReadOnlyMemory<char>.Empty;
        }

        StringTemplate? pathTemplate = null;
        if (!pathPart.IsEmpty)
        {
            if (!StringTemplate.TryParse(pathPart.Span, currentValidationContext, out pathTemplate, out error))
            {
                 error = $"Failed to parse path template part: {error}";
                result = null;
                return false;
            }
        }

        List<(StringTemplate Key, StringTemplate Value)>? queryTemplates = null;
        if (!queryPart.IsEmpty)
        {
            queryTemplates = new List<(StringTemplate Key, StringTemplate Value)>();
            int currentPos = 0;
            var querySpan = queryPart.Span;

            while (currentPos < querySpan.Length)
            {
                int nextAmpersand = ExpressionParsingHelpers.FindCharNotEscaped(querySpan.Slice(currentPos), '&', '\\');
                int endOfPair = (nextAmpersand == -1) ? querySpan.Length : currentPos + nextAmpersand;
                var pairSpan = querySpan.Slice(currentPos, endOfPair - currentPos);

                int equalsIndex = ExpressionParsingHelpers.FindCharNotEscaped(pairSpan, '=', '\\');
                ReadOnlySpan<char> keySpan = (equalsIndex == -1) ? pairSpan : pairSpan.Slice(0, equalsIndex);
                ReadOnlySpan<char> valueSpan = (equalsIndex == -1) ? ReadOnlySpan<char>.Empty : pairSpan.Slice(equalsIndex + 1);

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

        result = new MultiTemplate(pathTemplate, queryTemplates, null, templateFlags);
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

    public string Evaluate(IDictionary<string, string> variables)
    {
        var pathResult = PathTemplate?.Evaluate(variables, out _) ?? "";

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
        return pathResult + sb.ToString();
    }

    public static bool TryParse(ReadOnlyMemory<char> expressionWithFlags,
        [NotNullWhen(true)] out MultiTemplate? result,
        [NotNullWhen(false)] out string? error)
    {
        return TryParse(expressionWithFlags, null, out result, out error);
    }
}

