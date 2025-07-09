using System;
using System.Diagnostics.CodeAnalysis;
using Imazen.Routing.Matching;
using Imazen.Routing.Matching.Templating;
using System.Text.RegularExpressions;

namespace Imazen.Routing.Matching;

public record struct ParsedRoutingExpression(MultiValueMatcher Matcher, MultiTemplate Template, ProviderInfo? ProviderInfo);

public record struct ProviderInfo(string? ProviderUniqueName);

public static partial class RoutingExpressionParser
{
    private const string Separator = " => ";

    private const int MinSyntaxVersion = 1;
    private const int MaxSyntaxVersion = 1;

    private record UnparsedPair(string MatchExpression, string TemplateExpression);
    



    public static bool TryParse(string fullExpression, [NotNullWhen(true)] out ParsedRoutingExpression? pair, [NotNullWhen(false)] out string? error)
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

        var unparsedPair = new UnparsedPair(matchExpression, templateExpression);
        return TryParseRoutingExpressionPair(unparsedPair, out var parsedPair, out error);
    }

    // for pairs of provider=uniqueName, capture the uniqueName
    [GeneratedRegex(@"(provider=(?<provider>[a-zA-Z0-9_]+))|(?<version>v[0-9]+)", RegexOptions.Compiled)]
    private static partial Regex TemplateFlagRegex();

    private static bool TryParseRoutingExpressionPair(UnparsedPair pair, [NotNullWhen(true)] out ParsedRoutingExpression? parsedRoutingExpression, [NotNullWhen(false)] out string? error)
    {
        if (!MultiValueMatcher.TryParse(pair.MatchExpression, out var matcher, out error))
        {
            parsedRoutingExpression = null;
            return false;
        }
        var matcherVariableInfo = matcher.GetMatcherVariableInfo();

        var templateFlagRegex = TemplateFlagRegex();

        var templateContext = new TemplateValidationContext(matcherVariableInfo, matcher.UnusedFlags, null, templateFlagRegex);

        if (!MultiTemplate.TryParse(pair.TemplateExpression.AsMemory(), templateContext, out var template, out error))
        {
            parsedRoutingExpression = null;
            return false;
        }

        if (!ParseProviderFlags(template.Flags, out var providerInfo, out error))
        {
            parsedRoutingExpression = null;
            return false;
        }


        parsedRoutingExpression = new ParsedRoutingExpression(matcher, template, providerInfo);
        error = null;
        return true;
    }

    private static bool ParseProviderFlags(ExpressionFlags? flags, out ProviderInfo? providerInfo, [NotNullWhen(false)] out string? error)
    {
        providerInfo = null;
        // No flags is ok, layer can validate there is only one provider that supports the scheme, and error otherwise.
        if (flags == null || flags.Flags.Count == 0)
        {
            error = null;
            return true;
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
                    providerInfo = new ProviderInfo(match.Groups["provider"].Value);
                }
                else if (match.Groups["version"].Success)
                {
                    version = int.Parse(match.Groups["version"].Value);
                }
            }
            else
            {
                error = $"Invalid flag '{flag}', template flags may only contain a provider specification in the form [provider=uniqueName]";
                return false;
            }
        }
        if (version.HasValue && (version.Value < MinSyntaxVersion || version.Value > MaxSyntaxVersion))
        {
            error = $"Please see the upgrade guide to migrate your routing expressions from syntax version {version.Value} to version {MaxSyntaxVersion}";
            return false;
        }
        else if (!version.HasValue)
        {
            error = $"You must specify [v{MaxSyntaxVersion}] at the end of your routing expression to indicate what syntax version you are using.";
            return false;
        }
        error = null;
        return true;
    }
}


