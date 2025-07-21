using System;
using System.Diagnostics.CodeAnalysis;
using Imazen.Routing.Matching;
using Imazen.Routing.Matching.Templating;
using System.Text.RegularExpressions;
using System.Text;

namespace Imazen.Routing.RoutingExpressions;

public record struct ParsedRoutingExpression(MultiValueMatcher Matcher, MultiTemplate Template, 
ProviderInfo? ProviderInfo, string OriginalExpression, string? PathTemplateLiteralStart, Uri? TemplateLiteralStartUri){
    public override readonly string ToString(){
        return OriginalExpression;
    }
}

public record struct ProviderInfo(string? ProviderName,string? FixedScheme);

public record RoutingParsingOptions(List<string>? AllowedSchemes, bool RequireScheme, bool RequirePath,
List<string>? AllowedFlags, List<Regex>? AllowedFlagRegexes){
    public static RoutingParsingOptions AnySchemeAnyFlagOptionalPath => new(null, false, false, null, null);
    public static RoutingParsingOptions AnySchemeAnyFlagRequirePath => new(null, false, true, null, null);
}

public static partial class RoutingExpressionParser
{
    private const string Separator = " => ";

    private const int MinSyntaxVersion = 1;
    private const int MaxSyntaxVersion = 1;

    private record UnparsedPair(string MatchExpression, string TemplateExpression, string OriginalExpression);
    



    public static bool TryParse(RoutingParsingOptions options, string fullExpression, [NotNullWhen(true)] out ParsedRoutingExpression? pair, [NotNullWhen(false)] out string? error)
    {
        pair = null;
        var separatorIndex = fullExpression.IndexOf(Separator);

        if (separatorIndex < 0)
        {
            error = $"Routing expression is missing the required ' => ' separator (with spaces).";
            return false;
        }

        var matchExpression = fullExpression.Substring(0, separatorIndex);
        if (string.IsNullOrWhiteSpace(matchExpression))
        {
            error = "Match expression cannot be empty.";
            return false;
        }

        var templateExpression = fullExpression.Substring(separatorIndex + Separator.Length);
        if (string.IsNullOrWhiteSpace(templateExpression))
        {
            error = "Template expression cannot be empty.";
            return false;
        }

        var unparsedPair = new UnparsedPair(matchExpression, templateExpression, fullExpression);
        return TryParseRoutingExpressionPair(options, unparsedPair, out pair, out error);
    }

#if !NETSTANDARD2_0
    // for pairs of provider=uniqueName, capture the uniqueName
    [GeneratedRegex(@"(provider=(?<provider>[a-zA-Z0-9_]+))|(?<version>v[0-9]+)")]
    private static partial Regex TemplateFlagRegex();
#else
    private static Regex _templateFlagRegex =new Regex(@"(provider=(?<provider>[a-zA-Z0-9_]+))|(v(?<version>[0-9]+))", RegexOptions.Compiled);
    private static Regex TemplateFlagRegex()
    {
        return _templateFlagRegex;
    }
#endif

    private static bool TryParseRoutingExpressionPair(RoutingParsingOptions options, UnparsedPair pair, [NotNullWhen(true)] out ParsedRoutingExpression? parsedRoutingExpression, [NotNullWhen(false)] out string? error)
    {
        // TODO: We might want to let the matcher use flags from the expression end...
        if (!MultiValueMatcher.TryParse(pair.MatchExpression, out var matcher, out string? matcherError))
        {
            parsedRoutingExpression = null;
            error = matcherError + " match expression in route " + pair.OriginalExpression;
            return false;
        }
        var matcherVariableInfo = matcher.GetMatcherVariableInfo();

        var templateFlagRegex = TemplateFlagRegex();

        var templateContext = new TemplateValidationContext(matcherVariableInfo, matcher.UnusedFlags, 
        null, templateFlagRegex, options.RequirePath, options.RequireScheme, options.AllowedSchemes);

        if (!MultiTemplate.TryParse(pair.TemplateExpression.AsMemory(), templateContext, out var template, out string? templateError))
        {
            parsedRoutingExpression = null;
            error = templateError + " template expression in route " + pair.OriginalExpression;
            return false;
        }

        if (!ParseTemplateFlags(options, template.Flags, template.Scheme, out var providerInfo, out error))
        {
            parsedRoutingExpression = null;
            return false;
        }

        // try parse the first template literal to URI
        var literalStart = template.GetTemplateLiteralStart();
        Uri.TryCreate(literalStart, UriKind.RelativeOrAbsolute, out var uri);

        parsedRoutingExpression = new ParsedRoutingExpression(matcher, template, providerInfo, pair.OriginalExpression, literalStart, uri);
        error = null;
        return true;
    }

    private static bool ParseTemplateFlags(RoutingParsingOptions options, ExpressionFlags? flags, string? scheme, out ProviderInfo? providerInfo, [NotNullWhen(false)] out string? error)
    {
        providerInfo = null;
        if (flags == null || flags.Flags.Count == 0)
        {
            error = $"You must specify [v{MaxSyntaxVersion}] at the end of your routing expression to indicate what syntax version you are using.";
            return false;
        }
        int? version = null;
        // use regex again
        foreach (var flag in flags.Flags)
        {
            var regex = TemplateFlagRegex();
            var match = regex.Match(flag);
            if (match.Success)
            {
                if (match.Groups["provider"].Success)
                {
                    providerInfo = new ProviderInfo(match.Groups["provider"].Value, scheme);
                }
                else if (match.Groups["version"].Success)
                {
                    version = int.Parse(match.Groups["version"].Value.TrimStart('v'));
                }
            }
            else if (options.AllowedFlags?.Contains(flag) == true)
            {
                // allowed flag
            }
            else if (options.AllowedFlagRegexes?.Any(x => x.IsMatch(flag)) == true)
            {
                // allowed flag
            }
            else
            {
                var sb = new StringBuilder();
                sb.Append("Invalid flag '").Append(flag).Append("'. The following are required: v").Append(MaxSyntaxVersion).Append(", and the following are allowed: ");
                if (options.AllowedFlags != null)
                {
                    sb.Append(string.Join(", ", options.AllowedFlags));
                }
                if (options.AllowedFlagRegexes != null)
                {
                    sb.Append(string.Join(", ", options.AllowedFlagRegexes.Select(x => x.ToString())));
                }
                sb.Append(", or a provider specification in the form [provider=name]");
                error = sb.ToString();
                return false;
            }
        }
        if (version.HasValue && (version.Value < MinSyntaxVersion || version.Value > MaxSyntaxVersion))
        {
            error = $"Please see the upgrade guide to migrate your routing expressions from syntax version {version.Value} to version {MaxSyntaxVersion}";
            return false;
        }
        if (!version.HasValue)
        {
            error = $"You must specify [v{MaxSyntaxVersion}] at the end of your routing expression to indicate what syntax version you are using.";
            return false;
        }
        error = null;
        return true;
    }
}


