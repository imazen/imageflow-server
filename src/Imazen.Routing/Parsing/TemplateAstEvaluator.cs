using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Imazen.Routing.Matching.Templating; // Need original records and EvaluationContext
using sly.lexer; // Added for Token<>

namespace Imazen.Routing.Parsing;

/// <summary>
/// Evaluates a parsed AST from a Templating expression using provided variables.
/// </summary>
public class TemplateAstEvaluator
{
    private readonly Expression _astRoot;
    private readonly IDictionary<string, string> _variables;
    // private readonly TemplateValidationContext? _validationContext; // For future validation

    public TemplateAstEvaluator(Expression astRoot, IDictionary<string, string> variables /*, TemplateValidationContext? validationContext = null */)
    {
        _astRoot = astRoot;
        _variables = variables;
        // _validationContext = validationContext;
    }

    public string Evaluate()
    {
        // TODO: Handle flags from _astRoot.Flags if they influence evaluation

        var pathResult = EvaluatePath();
        var queryResult = EvaluateQuery();

        return pathResult + queryResult;
    }

    private string EvaluatePath()
    {
        if (_astRoot.Path is not PathExpression pathExpr) return "";

        var sb = new StringBuilder();
        bool dummy = false; 
        EvaluateSegments(pathExpr.Segments, sb, ref dummy);
        return sb.ToString();
    }

    private string EvaluateQuery()
    {
        if (_astRoot.Query == null || _astRoot.Query.Count == 0) return "";

        var sb = new StringBuilder();
        bool firstQueryParam = true;

        foreach (var queryPair in _astRoot.Query)
        {
            var keyBuilder = new StringBuilder();
            var valueBuilder = new StringBuilder();
            bool keyOptionalEmpty = false;
            bool valueOptionalEmpty = false;

            // Evaluate Key Segments
            EvaluateSegments(GetKeySegments(queryPair), keyBuilder, ref keyOptionalEmpty); 

            // Evaluate Value Segments
            EvaluateSegments(queryPair.ValueSegmentsNode?.Segments ?? new List<ISegment>(), valueBuilder, ref valueOptionalEmpty);

            // Skip pair if EITHER key or value was optional and evaluated to empty
            if (keyOptionalEmpty || valueOptionalEmpty)
            {
                continue;
            }

            string keyResult = keyBuilder.ToString();
            string valueResult = valueBuilder.ToString();

            sb.Append(firstQueryParam ? '?' : '&');
            firstQueryParam = false;

            sb.Append(Uri.EscapeDataString(keyResult));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(valueResult));
        }

        return sb.ToString();
    }

    // Helper to get key segments (assuming key is stored as a LiteralSegment in the QueryPair for now)
    // This might need adjustment if query keys can truly have variables via the AST.
    private IReadOnlyList<ISegment> GetKeySegments(QueryPair pair)
    {
        // For now, treat the QueryPair.Key string as a single LiteralSegment.
        // If the parser is changed to allow variable keys, this needs updating.
        return new List<ISegment> { new LiteralSegment(pair.Key.Name) }; 
    }

    private void EvaluateSegments(IReadOnlyList<ISegment> segments, StringBuilder sb, ref bool containsOptionalEmpty)
    {
         foreach (var segment in segments)
         {
            bool segmentWasOptionalEmpty = EvaluateSegment(segment, sb); 
            if (segmentWasOptionalEmpty) {
                containsOptionalEmpty = true;
                // Important: If one segment is optional-empty, we still evaluate others,
                // but the whole query pair might be skipped later.
            }
         }
    }

    private bool EvaluateSegment(ISegment segment, StringBuilder sb)
    {
        if (segment is LiteralSegment literal)
        {
            sb.Append(literal.Value);
            return false;
        }
        else if (segment is VariableSegment variable)
        {
            string? currentValue = null;
            _variables.TryGetValue(variable.Name ?? string.Empty, out currentValue);

            bool isOptional = false;
            // Use the public EvaluationContext from the Templating namespace
            var evalContext = new Imazen.Routing.Matching.Templating.EvaluationContext(); 
            ITransformation? optionalMarker = null;

            var transformations = variable.Modifiers.Select(mod => ModifierToTransformation(mod)).Where(t => t != null).ToList();

            foreach (var transform in transformations)
            {
                if (transform is OptionalMarkerTransform)
                {
                    isOptional = true;
                    optionalMarker = transform; // Keep track of the marker itself
                }
                else
                {
                    // Apply transformation
                    currentValue = transform!.Apply(currentValue, _variables, evalContext);
                }
            }

            if (isOptional && string.IsNullOrEmpty(currentValue))
            {
                return true; // Signal optional empty
            }
            else
            {
                sb.Append(currentValue);
                return false;
            }
        }
        return false;
    }

    // Maps an AST IModifier node to an original ITransformation instance
    private ITransformation? ModifierToTransformation(IModifier modifier)
    {
        if (modifier is SimpleModifier sm)
        {
            if (sm.Name == "?") return new OptionalMarkerTransform();
            // Other simple modifiers like * are likely irrelevant for templating
            return null;
        }
        else if (modifier is Modifier mwa)
        {
            // Use Tokens field, coalesce null to empty list
            var args = mwa.Arguments?.Tokens ?? [];
            var stringArgs = ExtractStringArguments(args); // Placeholder extraction
            
            var nameLower = mwa.Name.ToLowerInvariant();

            switch (nameLower)
            {
                case "lower": return new ToLowerTransform();
                case "upper": return new ToUpperTransform();
                case "encode": return new UrlEncodeTransform();
                case "optional": return new OptionalMarkerTransform();
                case "map":
                    if (stringArgs.Count >= 2 && stringArgs.Count % 2 == 0)
                    {
                        var mappings = new List<(string From, string To)>(stringArgs.Count / 2);
                        for (int i = 0; i < stringArgs.Count; i += 2)
                        {
                            mappings.Add((stringArgs[i], stringArgs[i + 1]));
                        }
                        return new MapTransform(mappings);
                    }
                    return null; 
                case "or_var":
                    if (stringArgs.Count == 1) return new OrTransform(stringArgs[0]);
                    return null; 
                case "default":
                    if (stringArgs.Count == 1) return new DefaultTransform(stringArgs[0]);
                    return null; 
                case "equals":
                    if (stringArgs.Count > 0) return new EqualsTransform(stringArgs);
                    return null; 
                case "other": 
                     if (stringArgs.Count == 1) return new MapDefaultTransform(stringArgs[0]);
                     return null; 
                default:
                    return null; 
            }
        }
        return null;
    }

    // Placeholder helper to extract simple string args - NEEDS PROPER IMPLEMENTATION
    private List<string> ExtractStringArguments(IReadOnlyList<ImazenRoutingParser.TokenViewModel> tokens)
    {
        var result = new List<string>();
        StringBuilder current = new StringBuilder();
        foreach(var t in tokens)
        {
            var token = t.RawToken;
            if (token == null) continue;
            if (token.TokenID == ImazenRoutingToken.COMMA || token.TokenID == ImazenRoutingToken.PIPE) {
                 result.Add(current.ToString());
                 current.Clear();
             } else {
                 if (token.TokenID == ImazenRoutingToken.ESCAPE_SEQUENCE && token.Value.Length > 1) {
                     current.Append(token.Value[1]);
                 } else if (token.TokenID == ImazenRoutingToken.INT) {
                     current.Append(token.IntValue);
                 } else {
                     current.Append(token.Value);
                 }
             }
        }
        result.Add(current.ToString());
        return result.Where(s => !string.IsNullOrEmpty(s)).ToList(); 
    }
} 