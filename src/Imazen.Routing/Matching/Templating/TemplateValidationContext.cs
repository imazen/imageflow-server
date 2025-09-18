using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    DualExpressionFlags Flags,
    Regex? TemplateFlagRegex,
    bool RequirePath,
    bool RequireSchemeForPaths,
    List<string>? AllowedSchemes
)
{

    public static TemplateValidationContext VarsAndMatcherFlags(IReadOnlyDictionary<string, MatcherVariableInfo>? matcherVariables, ExpressionFlags? matcherFlags)
    {
        var dualFlags = DualExpressionFlags.FromExpressionFlags(matcherFlags, ExpressionFlagOrigin.AfterMatcher);
        return new TemplateValidationContext(matcherVariables, dualFlags, null, false, false, null);
    }
    
    public static TemplateValidationContext Empty => new(null, DualExpressionFlags.Empty, null, false, false, null);
}