using System;
using System.Diagnostics.CodeAnalysis;
using Imazen.Routing.Matching;
using Imazen.Routing.Matching.Templating;
using System.Text.RegularExpressions;
using System.Text;

namespace Imazen.Routing.RoutingExpressions;

public record struct ParsedRoutingExpression(MultiValueMatcher Matcher, MultiTemplate Template, 
    RouteProviderInfo ProviderInfo, string OriginalExpression, string? PathTemplateLiteralStart, Uri? TemplateLiteralStartUri){
    public override readonly string ToString(){
        return OriginalExpression;
    }
    public readonly IReadOnlyCollection<string>? TemplateFlags => Template.Flags?.Flags;
}

public record struct RouteProviderInfo(string? LiteralPrefix, string? ProviderName, string? FixedScheme, 
IReadOnlyCollection<KeyValuePair<string, string>> FlagPairs, IReadOnlyCollection<string> Flags);

public record RoutingParsingOptions(
    List<string>? AllowedSchemes, 
    List<string>? AllowedTemplatePrefixes,
    bool RequireTemplatePrefix,
    bool RequireScheme, 
    bool RequirePath,
    List<string>? AllowedFlags, 
    List<Regex>? AllowedFlagRegexes){

    public static RoutingParsingOptions AnySchemeAnyFlagOptionalPath => new(null, null, false, false, false, null, null);
    public static RoutingParsingOptions AnySchemeAnyFlagRequirePath => new(null, null, false, true, false, null, null);
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

        var matchExpression = fullExpression[..separatorIndex];
        var templateExpression = fullExpression[(separatorIndex + Separator.Length)..];
        return TryParse(options, matchExpression, templateExpression, fullExpression, out pair, out error);
    }

    public static bool TryParse(RoutingParsingOptions options, string matchExpression, string templateExpression, string? combinedExpression, [NotNullWhen(true)] out ParsedRoutingExpression? pair, [NotNullWhen(false)] out string? error)
    {
        pair = null;
        if (string.IsNullOrWhiteSpace(matchExpression))
        {
            error = "Match expression cannot be empty.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(templateExpression))
        {
            error = "Template expression cannot be empty.";
            return false;
        }
        combinedExpression ??= matchExpression + Separator + templateExpression;

        var unparsedPair = new UnparsedPair(matchExpression, templateExpression, combinedExpression);
        return TryParseRoutingExpressionPair(options, unparsedPair, out pair, out error);
    }

#if !NETSTANDARD2_0
    // for pairs of provider=uniqueName, capture the uniqueName
    [GeneratedRegex(@"v[0-9]+")]
    private static partial Regex VersionNumberRegex();
#else
    private static Regex _versionNumberRegex =new Regex(@"v[0-9]+", RegexOptions.Compiled);
    private static Regex VersionNumberRegex()
    {
        return _versionNumberRegex;
    }
#endif
#if !NETSTANDARD2_0
    // for pairs of provider=uniqueName, capture the uniqueName
    [GeneratedRegex(@"[a-zA-Z0-9_]+")]
    private static partial Regex ProviderNameRegex();
#else
    private static Regex _providerNameRegex =new Regex(@"[a-zA-Z0-9_]+", RegexOptions.Compiled);
    private static Regex ProviderNameRegex()
    {
        return _providerNameRegex;
    }
#endif

#if !NETSTANDARD2_0
    // for pairs of provider=uniqueName, capture the uniqueName
    [GeneratedRegex(@"([a-zA-Z0-9_]+|([a-zA-Z0-9_]+=[a-zA-Z0-9_]+))")]
    private static partial Regex TemplateFlagRegex();
#else
    private static Regex _templateFlagRegex =new Regex(@"([a-zA-Z0-9_]+|([a-zA-Z0-9_]+=[a-zA-Z0-9_]+))", RegexOptions.Compiled);
    private static Regex TemplateFlagRegex()
    {
        return _templateFlagRegex;
    }
#endif


    private static bool TryParseRoutingExpressionPair(RoutingParsingOptions options, UnparsedPair pair, [NotNullWhen(true)] out ParsedRoutingExpression? parsedRoutingExpression, [NotNullWhen(false)] out string? error)
    {
 
        // Parse template flags first, so matcher can use them - say [ignore-case] is put last.
        if (!MultiTemplate.TryParseRemoveFlags(pair.MatchExpression.AsMemory(), out var remainingMatchExpression, out var templateFlags, out string? templateFlagsError))
        {
            parsedRoutingExpression = null;
            error = templateFlagsError + " match expression flags in route " + pair.OriginalExpression;
            return false;
        } 

        if (!MultiValueMatcher.TryParse(pair.MatchExpression.AsMemory(),null, templateFlags, out var matcher, out string? matcherError))
        {
            parsedRoutingExpression = null;
            error = matcherError + " match expression in route " + pair.OriginalExpression;
            return false;
        }
        var matcherVariableInfo = matcher.GetMatcherVariableInfo();
        var templateFlagRegex = TemplateFlagRegex();

        var templateContext = new TemplateValidationContext(matcherVariableInfo, matcher.UnusedFlags, 
        templateFlags, templateFlagRegex, options.RequirePath, options.RequireScheme, options.AllowedSchemes);


        if (!MultiTemplate.TryParse(pair.TemplateExpression.AsMemory(), templateFlags, templateContext, out var template, out string? templateError))
        {
            parsedRoutingExpression = null;
            error = templateError + " template expression in route " + pair.OriginalExpression;
            return false;
        }

        if (!ParseTemplateFlags(options, template, out var providerInfo, out error))
        {
            parsedRoutingExpression = null;
            return false;
        }


        // try parse the first template literal to URI
        var literalStart = template.GetTemplateLiteralStart();
        Uri.TryCreate(literalStart, UriKind.RelativeOrAbsolute, out var uri);

        parsedRoutingExpression = new ParsedRoutingExpression(matcher, template, providerInfo.Value, pair.OriginalExpression, literalStart, uri);
        error = null;
        return true;
    }

    private static bool ParseTemplateFlags(RoutingParsingOptions options, MultiTemplate template,
    [NotNullWhen(true)] out RouteProviderInfo? providerInfo, [NotNullWhen(false)] out string? error)
    {
        providerInfo = null;
        if (template.Flags == null || template.Flags.Flags.Count == 0)
        {
            error = $"You must specify [v{MaxSyntaxVersion}] at the end of your routing expression to indicate what syntax version you are using.";
            return false;
        }
        string? literalStart = template.GetTemplateLiteralStart();
        if (options.RequireTemplatePrefix && string.IsNullOrWhiteSpace(literalStart))
        {
            error = "Template cannot start with variable or optional segment due to validation rules.";
            return false;
        }
        if (options.AllowedTemplatePrefixes != null && !string.IsNullOrWhiteSpace(literalStart)){
            // ensure it begins with one of them
            if (!options.AllowedTemplatePrefixes.Any(x => literalStart.StartsWith(x))){
                error = "Template must begin with one of the following prefixes: " + string.Join(", ", options.AllowedTemplatePrefixes);
                return false;
            }
        }
        
        int? version = null;
        string? providerName = null;
        var allowlisting = options.AllowedFlags != null || options.AllowedFlagRegexes != null;
        // use regex again
        foreach (var flag in template.Flags.Flags)
        {
            var regex = VersionNumberRegex();
            var match = regex.Match(flag);
            if (match.Success)
            {
                if (match.Groups["version"].Success)
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
            else if (!allowlisting)
            {
                // allow any
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
        foreach (var pair in template.Flags.Pairs)
        {
            if (pair.Key == "provider")
            {
                providerName = pair.Value;
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
        providerInfo = new RouteProviderInfo(literalStart, providerName, template.Scheme, template.Flags.Pairs, template.Flags.Flags);
        error = null;
        return true;
    }
}


