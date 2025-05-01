using System.Collections.Generic;
using Imazen.Routing.Matching;

namespace Imazen.Routing.Matching.Templating;

/// <summary>
/// Provides context for validating a template against its corresponding match expression.
/// </summary>
/// <param name="MatcherVariables">Variable definitions from the match expression.</param>
/// <param name="MatcherFlags">Flags parsed from the match expression.</param>
/// <param name="TemplateFlags">Flags parsed from the template expression.</param>
public record TemplateValidationContext(
    IReadOnlyDictionary<string, MatcherVariableInfo>? MatcherVariables,
    ExpressionFlags? MatcherFlags,
    ExpressionFlags? TemplateFlags
); 