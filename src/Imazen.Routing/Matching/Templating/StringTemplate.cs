using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq; // Added for Any()
using System.Text;
using Imazen.Routing.Matching; // Need this for ExpressionParsingHelpers

namespace Imazen.Routing.Matching.Templating;

/// <summary>
/// Represents a parsed template string, composed of literal and variable segments.
/// Can be used for paths, query keys, or query values.
/// </summary>
public record StringTemplate(IReadOnlyList<ITemplateSegment> Segments)
{
    /// <summary>
    /// Parses a template string into segments.
    /// </summary>
    /// <param name="template">The template string to parse.</param>
    /// <param name="validationContext">The validation context for the template.</param>
    /// <param name="result">The parsed StringTemplate if successful.</param>
    /// <param name="error">The error message if parsing fails.</param>
    /// <returns>True if parsing was successful, false otherwise.</returns>
    public static bool TryParse(
        ReadOnlySpan<char> template,
        TemplateValidationContext? validationContext,
        [NotNullWhen(true)] out StringTemplate? result,
        [NotNullWhen(false)] out string? error)
    {
        var segments = new List<ITemplateSegment>();
        int consumed = 0;
        int lastOpenBrace = -1; // Position of the last '{' encountered outside escapes

        try // Use try-catch for cleaner error handling on failures
        {
            while (consumed < template.Length)
            {
                if (lastOpenBrace == -1) // We are looking for a literal or the next '{'
                {
                    int nextOpenBrace = ExpressionParsingHelpers.FindCharNotEscaped(template.Slice(consumed), '{', '\\');

                    if (nextOpenBrace != -1) // Found '{'
                    {
                        // Add the literal before the '{'
                        int literalEnd = consumed + nextOpenBrace;
                        if (literalEnd > consumed)
                        {
                            segments.Add(CreateLiteralSegment(template.Slice(consumed, literalEnd - consumed)));
                        }
                        lastOpenBrace = literalEnd; // Remember position of '{'
                        consumed = literalEnd + 1; // Move past '{'
                        // Debug: Indicate finding '{'
                        // System.Diagnostics.Debug.WriteLine($"StringTemplate.TryParse: Found '{{' at absolute position {lastOpenBrace}. Consumed is now {consumed}.");
                    }
                    else // No more '{', the rest is a literal
                    {
                        // Before adding as a literal, check for unexpected '}'
                        int unexpectedCloseBrace = ExpressionParsingHelpers.FindCharNotEscaped(template.Slice(consumed), '}', '\\');
                        if (unexpectedCloseBrace != -1)
                        {
                             error = $"Syntax error: Unexpected '}}' at position {consumed + unexpectedCloseBrace}.";
                             result = null;
                             return false;
                        }

                        if (consumed < template.Length)
                        {
                            segments.Add(CreateLiteralSegment(template.Slice(consumed)));
                        }
                        consumed = template.Length; // Done
                    }
                }
                else // We are inside potential {...}, looking for '}'
                {
                    int nextCloseBrace = ExpressionParsingHelpers.FindCharNotEscaped(template.Slice(consumed), '}', '\\');

                    if (nextCloseBrace != -1) // Found closing '}'
                    {
                        int closeBraceAbsoluteIndex = consumed + nextCloseBrace;
                        // Variable content is between lastOpenBrace and closeBraceAbsoluteIndex
                        var variableContent = template.Slice(lastOpenBrace + 1, closeBraceAbsoluteIndex - (lastOpenBrace + 1));

                        // Debug: Indicate finding '}' and variable content
                        // System.Diagnostics.Debug.WriteLine($"StringTemplate.TryParse: Found '}}' at absolute position {closeBraceAbsoluteIndex}. Variable content: '{{variableContent.ToString()}}'");

                        if (!TryParseVariableContent(variableContent, validationContext, out var varSegment, out error))
                        {
                            // Prepend context
                            error = $"Failed to parse variable content near position {lastOpenBrace + 1}: {error}";
                            result = null;
                            return false;
                        }
                        segments.Add(varSegment);
                        consumed = closeBraceAbsoluteIndex + 1; // Move past '}'
                        lastOpenBrace = -1; // Reset state, look for literals again
                    }
                    else // No closing '}', syntax error
                    {
                        // Debug: Indicate unmatched '{'
                        // System.Diagnostics.Debug.WriteLine($"StringTemplate.TryParse: Unmatched '{{' at position {lastOpenBrace}.");
                        error = $"Syntax error: Unmatched '{{' starting at position {lastOpenBrace}.";
                        result = null;
                        return false;
                    }
                }
            } // End while

            // Final check for dangling open brace
            if (lastOpenBrace != -1)
            {
                error = $"Syntax error: Unmatched '{{' starting at position {lastOpenBrace} at end of input.";
                result = null;
                return false;
            }
        }
        catch (TemplateParseException e) // Catch specific parsing errors
        {
             error = e.Message;
             result = null;
             return false;
        }


        result = new StringTemplate(segments);
        error = null;
        return true;
    }

    // Original TryParse overload now calls the new one with null context
    public static bool TryParse(ReadOnlySpan<char> template,
        [NotNullWhen(true)] out StringTemplate? result,
        [NotNullWhen(false)] out string? error)
    {
        return TryParse(template, null, out result, out error);
    }

    // Helper to create LiteralSegment, handling escapes
    private static LiteralSegment CreateLiteralSegment(ReadOnlySpan<char> literalSpan)
    {
        // Need to unescape \\, \{, \} etc.
        var sb = new StringBuilder(literalSpan.Length);
        for (int i = 0; i < literalSpan.Length; i++)
        {
            if (literalSpan[i] == '\\' && i + 1 < literalSpan.Length)
            {
                // Append the escaped character directly
                sb.Append(literalSpan[i + 1]);
                i++; // Skip the escaped char
            }
            else
            {
                // Append normal char (handles lone backslash at end correctly)
                sb.Append(literalSpan[i]);
            }
        }
        return new LiteralSegment(sb.ToString());
    }


    // Renamed from TryParseVariableSegment to avoid conflict
    private static bool TryParseVariableContent(
        ReadOnlySpan<char> content,
        TemplateValidationContext? validationContext,
        [NotNullWhen(true)] out VariableSegment? segment,
        [NotNullWhen(false)] out string? error)
    {
        var transformations = new List<ITransformation>();
        ReadOnlySpan<char> nameSpan;
        ReadOnlySpan<char> transformsSpan = ReadOnlySpan<char>.Empty;

        // Find the first non-escaped colon
        int firstColon = ExpressionParsingHelpers.FindCharNotEscaped(content, ':', '\\');

        if (firstColon == -1)
        {
            nameSpan = content; // No transformations
        }
        else
        {
            nameSpan = content.Slice(0, firstColon);
            transformsSpan = content.Slice(firstColon + 1);
        }

        if (nameSpan.IsEmpty)
        {
            error = "Variable name cannot be empty inside {}.";
            segment = null;
            return false;
        }

        string variableName = UnescapeArgument(nameSpan); // Unescape name if needed? Probably not. Use ToString().

        // Validate name (Adapt from ExpressionParsingHelpers.ValidateSegmentName if needed)
        // For now, basic check:
        if (!IsValidTemplateVariableName(variableName)) // Assuming a helper method
        {
             error = $"Invalid variable name: '{variableName}'. Must start with letter or underscore, contain letters, numbers, underscores.";
             segment = null;
             return false;
        }

        MatcherVariableInfo? variableInfo = null;
        if (validationContext?.MatcherVariables != null && !validationContext.MatcherVariables.TryGetValue(variableName, out variableInfo))
        {
            error = $"Template uses variable '{variableName}' which is not defined by the corresponding match expression.";
            segment = null;
            return false;
        }

        if (!transformsSpan.IsEmpty)
        {
            if (!TryParseTransformations(transformsSpan, validationContext, transformations, out error))
            {
                segment = null;
                // Error context added by caller
                return false;
            }
        }

        if (variableInfo?.IsOptional == true) // Check if var is optional in matcher
        {
            bool isHandled = transformations.Any(t => t is OrTransform || t is DefaultTransform || t is OptionalMarkerTransform);
            if (!isHandled)
            {
                error = $"Template uses optional variable '{variableName}' without providing a fallback (:or_var, :default) or marking as ignorable (:optional, :?).";
                segment = null;
                return false;
            }
        }
        segment = new VariableSegment(variableName, transformations);
        error = null;
        return true;
    }

    // Basic validation - adapt if needed
    private static bool IsValidTemplateVariableName(string name)
    {
         if (string.IsNullOrEmpty(name)) return false;
         char first = name[0];
         if (!char.IsLetter(first) && first != '_') return false;
         for (int i = 1; i < name.Length; i++)
         {
             char c = name[i];
             if (!char.IsLetterOrDigit(c) && c != '_') return false;
         }
         // Could add check against reserved transform names if desired
         return true;
    }


    private static bool TryParseTransformations(
        ReadOnlySpan<char> transformsSpan,
        TemplateValidationContext? validationContext,
        List<ITransformation> transformations,
        [NotNullWhen(false)] out string? error)
    {
        int currentPos = 0;
        while (currentPos < transformsSpan.Length)
        {
            // Find next non-escaped colon
            int nextColon = ExpressionParsingHelpers.FindCharNotEscaped(transformsSpan.Slice(currentPos), ':', '\\');
            ReadOnlySpan<char> transformPart;

            if (nextColon == -1)
            {
                transformPart = transformsSpan.Slice(currentPos);
                currentPos = transformsSpan.Length; // Move to end
            }
            else
            {
                transformPart = transformsSpan.Slice(currentPos, nextColon);
                currentPos += nextColon + 1; // Move past colon
            }

            if (transformPart.IsEmpty)
            {
                error = "Empty transformation found between colons.";
                return false;
            }

            if (!TryParseSingleTransformation(transformPart, validationContext, out var transform, out error))
            {
                // error set by TryParseSingleTransformation
                return false;
            }
            transformations.Add(transform);
        }

        error = null;
        return true;
    }


    // Updated TryParseSingleTransformation
    private static bool TryParseSingleTransformation(
        ReadOnlySpan<char> transformPart,
        TemplateValidationContext? validationContext,
        [NotNullWhen(true)] out ITransformation? transformation,
        [NotNullWhen(false)] out string? error)
    {
        int argOpenParen = ExpressionParsingHelpers.FindCharNotEscaped(transformPart, '(', '\\');
        ReadOnlySpan<char> transformNameSpan;
        List<string>? args = null;
        char argDelimiter = ','; // Default delimiter

        if (argOpenParen == -1)
        {
            transformNameSpan = transformPart;
        }
        else
        {
            transformNameSpan = transformPart.Slice(0, argOpenParen);
            int argCloseParen = -1;
            for(int i = transformPart.Length - 1; i > argOpenParen; i--) {
                 if(transformPart[i] == ')') {
                    int escapeCount = 0;
                    for(int j = i - 1; j >= 0 && transformPart[j] == '\\'; j--) { escapeCount++; }
                    if(escapeCount % 2 == 0) { argCloseParen = i; break; }
                 }
            }
            if (argCloseParen != transformPart.Length - 1) {
                error = $"Syntax error: Missing or misplaced closing parenthesis ')' for transformation arguments near '{transformPart.ToString()}'";
                transformation = null; return false;
            }
            var argsSpan = transformPart.Slice(argOpenParen + 1, argCloseParen - (argOpenParen + 1));

            // Determine delimiter based on transform name before parsing args
            string tempName = UnescapeArgument(transformNameSpan).ToLowerInvariant();
            if (tempName == "equals") {
                argDelimiter = '|';
            }

            // Parse arguments using the determined delimiter
            if (!TryParseArguments(argsSpan, argDelimiter, out args, out error)) // Pass delimiter
            {
                transformation = null; return false;
            }
        }

        string transformName = UnescapeArgument(transformNameSpan).ToLowerInvariant();

        if (args == null)
        {   // --- No Args ---
            switch (transformName)
            {
                case "lower": transformation = new ToLowerTransform(); error = null; return true;
                case "upper": transformation = new ToUpperTransform(); error = null; return true;
                case "encode": transformation = new UrlEncodeTransform(); error = null; return true;
                 case "?": case "optional": transformation = new OptionalMarkerTransform(); error = null; return true;
                default:
                    if (transformName == "map" || transformName == "or_var" || transformName == "default" || transformName == "equals" || transformName == "map_default") { error = $"Transformation '{transformName}' requires arguments in parentheses."; } else { error = $"Unknown transformation: '{transformName}'"; } transformation = null; return false;
            }
        }
        else
        {   // --- With Args ---
            switch (transformName)
            {
                 case "map": if (args.Count % 2 != 0) { error = $"'map' requires an even number of arguments. Found {args.Count}."; transformation = null; return false; } var mappings = new List<(string From, string To)>(args.Count / 2); for (int i = 0; i < args.Count; i += 2) { mappings.Add((args[i], args[i + 1])); } transformation = new MapTransform(mappings); error = null; return true;
                 case "or_var":
                 case "or": // Alias for or_var
                     if (args.Count != 1) { error = $"'or_var' requires exactly one argument. Found {args.Count}."; transformation = null; return false; }
                     string fallbackVarName = args[0];
                     if (!IsValidTemplateVariableName(fallbackVarName)) { error = $"Invalid fallback variable name '{fallbackVarName}' in 'or_var'."; transformation = null; return false; }
                     if (validationContext?.MatcherVariables != null && !validationContext.MatcherVariables.ContainsKey(fallbackVarName)) { error = $"'or_var' transform uses fallback variable '{fallbackVarName}' which is not defined by the corresponding match expression."; transformation = null; return false; }
                     transformation = new OrTransform(fallbackVarName);
                     error = null; return true;
                 case "default": if (args.Count != 1) { error = $"'default' requires exactly one argument. Found {args.Count}."; transformation = null; return false; } transformation = new DefaultTransform(args[0]); error = null; return true;
                 case "equals": // New (replaces allow)
                    if (args.Count == 0) { error = "'equals' requires at least one argument."; transformation = null; return false; }
                    // Arguments were already parsed using '|' delimiter
                    transformation = new EqualsTransform(args); error = null; return true;
                 case "allow": // New: Maps to AllowTransform (placeholder)
                     if (args.Count == 0) { error = "'allow' requires at least one argument."; transformation = null; return false; }
                     transformation = new AllowTransform(args); error = null; return true;
                 case "only": // New: Maps to OnlyTransform (placeholder)
                     if (args.Count == 0) { error = "'only' requires at least one argument."; transformation = null; return false; }
                     transformation = new OnlyTransform(args); error = null; return true;
                 case "map_default": // New: Maps to MapDefaultTransform (placeholder)
                     if (args.Count != 1) { error = "'map_default' requires exactly one argument."; transformation = null; return false; }
                     transformation = new MapDefaultTransform(args[0]); error = null; return true;

                default:
                    error = $"Unknown transformation: '{transformName}'";
                    transformation = null;
                    return false;
            }
        }
    }

    // Updated TryParseArguments signature
     private static bool TryParseArguments(
        ReadOnlySpan<char> argsSpan,
        char delimiter, // Added delimiter parameter
        out List<string> args,
        [NotNullWhen(false)] out string? error)
    {
        args = new List<string>();
        if(argsSpan.IsEmpty) { error = null; return true; }

        int start = 0;
        while (start < argsSpan.Length)
        {
            // Find next non-escaped delimiter
            var subSpan = argsSpan.Slice(start);
            var delimiterIndex = ExpressionParsingHelpers.FindCharNotEscaped(subSpan, delimiter, '\\'); // Use delimiter

            ReadOnlySpan<char> argSpan;
            if (delimiterIndex == -1)
            {
                argSpan = subSpan;
                start = argsSpan.Length;
            }
            else
            {
                argSpan = subSpan.Slice(0, delimiterIndex);
                start += delimiterIndex + 1; // Move past delimiter
            }
            args.Add(UnescapeArgument(argSpan));
        }
        error = null; return true;
    }

    // Helper to unescape \, ( ) , within arguments
    private static string UnescapeArgument(ReadOnlySpan<char> argSpan)
    {
        var sb = new StringBuilder(argSpan.Length);
        for (int i = 0; i < argSpan.Length; i++)
        {
            if (argSpan[i] == '\\' && i + 1 < argSpan.Length)
            {
                 char nextChar = argSpan[i+1];
                 // Only unescape chars special within args or backslash itself
                 if (nextChar == ',' || nextChar == '(' || nextChar == ')' || nextChar == '\\') {
                    sb.Append(nextChar);
                    i++;
                 } else {
                     // Keep backslash if it doesn't escape a special arg char
                     sb.Append('\\');
                     sb.Append(nextChar);
                     i++;
                 }
            }
            else
            {
                sb.Append(argSpan[i]);
            }
        }
        return sb.ToString();
    }


    /// <summary>
    /// Evaluates the template using the provided variables.
    /// </summary>
    /// <param name="variables">A dictionary of variable names and their values.</param>
    /// <param name="containsOptionalEmptyResult">Output parameter indicating if any VariableSegment marked as optional resulted in a null/empty value.</param>
    /// <returns>The evaluated string, or null if evaluation requires a missing required variable.</returns>
    public string? Evaluate(IDictionary<string, string> variables, out bool containsOptionalEmptyResult)
    {
        containsOptionalEmptyResult = false;
        var sb = new StringBuilder();

        foreach (var segment in Segments)
        {
            if (segment is LiteralSegment literal)
            {
                sb.Append(literal.Value);
            }
            else if (segment is VariableSegment variable)
            {
                // Create context for this variable segment evaluation
                var context = new EvaluationContext(); // New context for each variable segment

                string? currentValue = null;
                bool isOptional = false;
                variables.TryGetValue(variable.VariableName, out currentValue);

                foreach (var transform in variable.Transformations)
                {
                     if (transform is OptionalMarkerTransform)
                    {
                        isOptional = true;
                        // Optional marker doesn't change value, just flags
                    }
                    // Apply transformation with context
                    currentValue = transform.Apply(currentValue, variables, context); // Pass context
                }

                if (isOptional && string.IsNullOrEmpty(currentValue))
                {
                    containsOptionalEmptyResult = true;
                    // Omit the value
                }
                else
                {
                    sb.Append(currentValue); // Append even if null/empty if not optional
                }
            }
        }
        return sb.ToString();
    }

    // Placeholder for future validation mode
    public static bool TryParseValidated(ReadOnlySpan<char> template, object matcherVariableInfo, [NotNullWhen(true)] out StringTemplate? result, [NotNullWhen(false)] out string? error)
    {
        // This method signature is now incompatible with the validation logic.
        // Use the MultiTemplate.TryParse overload with TemplateValidationContext instead.
        // Or update this to accept TemplateValidationContext if needed for direct StringTemplate validation.
        throw new NotSupportedException("Use MultiTemplate.TryParse with TemplateValidationContext for validation.");
        // return TryParse(template, out result, out error); // Old fallback
    }

    // Custom exception for parsing errors
    private class TemplateParseException : Exception
    {
        public TemplateParseException(string message) : base(message) { }
    }
}

// Placeholder for 'allow' transformation
public class AllowTransform : ITransformation
{
    private readonly List<string> _allowedValues;
    public AllowTransform(List<string> allowedValues) => _allowedValues = allowedValues;
    public string? Apply(string? value, IDictionary<string, string> variables, EvaluationContext context)
    {
        // Placeholder: Actual logic needed
        if (value == null || !_allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
             // If value is not in allowed list, treat as missing/null
            return null;
        }
        return value;
    }
}

// Placeholder for 'only' transformation
public class OnlyTransform : ITransformation
{
    private readonly List<string> _allowedValues;
     public OnlyTransform(List<string> allowedValues) => _allowedValues = allowedValues;
    public string? Apply(string? value, IDictionary<string, string> variables, EvaluationContext context)
    {
        // Placeholder: Actual logic needed - similar to Allow but might behave differently
         if (value == null || !_allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
             // If value is not in allowed list, treat as missing/null
            return null;
        }
        return value;
    }
}