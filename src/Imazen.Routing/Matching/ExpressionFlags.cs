using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Imazen.Routing.Matching;

public readonly record struct ExpressionFlagParsingOptions(Regex ValidationRegex, bool TrimTrailingExpressionWhitespace,
bool AllowWhitespaceAfterBrackets,
bool TrimWhitespaceAroundFlags)
{
    public static readonly ExpressionFlagParsingOptions Permissive = new(ExpressionFlags.LatestPermissiveFlagSyntax(), true, true, true);


    public static readonly ExpressionFlagParsingOptions LowercaseDash = new(ExpressionFlags.LowercaseDash(), true, true, true);

    // WithValidationRegex
    public ExpressionFlagParsingOptions WithValidationRegex(Regex validationRegex)
    {
        return new ExpressionFlagParsingOptions(validationRegex, TrimTrailingExpressionWhitespace, AllowWhitespaceAfterBrackets, TrimWhitespaceAroundFlags);
    }
}

public enum ExpressionFlagOrigin
{
    AfterMatcher,
    AfterTemplate
}
public readonly record struct DualExpressionFlag(string Key, string? Value, ExpressionFlagOrigin Origin)
{
    public override string ToString()
    {
        if (Value == null)
        {
            return Key;
        }
        return $"{Key}={Value}";
    }
}

[Flags]
public enum ExpressionFlagStatus
{
    Unclaimed = 0,
    Exclusive = 1, // Cannot be used by both.
    Matcher = 2,
    Template = 4,
    MatcherExclusive = Exclusive | Matcher,
    TemplateExclusive = Exclusive | Template
}


public class ClaimableExpressionFlag(DualExpressionFlag flag)
{
    public override string ToString()
    {
        return $"{Flag} (Found {Flag.Origin}, claimed by {Status})";
    }
    public DualExpressionFlag Flag => flag;
    public ExpressionFlagStatus Status { get; private set; } = ExpressionFlagStatus.Unclaimed;

    private static string ClaimerNameFor(ExpressionFlagStatus status)
    {
        return (status & ExpressionFlagStatus.Matcher) != 0 ? "matcher" : "template";
    }
    public void SetStatus(ExpressionFlagStatus newStatus, bool requireClaiming)
    {
        if (!TrySetStatus(newStatus, requireClaiming, out var errorMessage))
        {
            throw new InvalidOperationException(errorMessage);
        }
    }
    public bool TrySetStatus(ExpressionFlagStatus newStatus, bool requireClaiming, out string? errorMessage)
    {

        if (newStatus == ExpressionFlagStatus.Unclaimed)
        {
            errorMessage = "Cannot set status to Unclaimed";
            return false;
        }
        if (newStatus == ExpressionFlagStatus.Exclusive)
        {
            errorMessage = "Cannot set status to Exclusive without specifying Matcher or Template";
            return false;
        }
        if (newStatus == Status)
        {
            errorMessage = null;
            return true;
        }
        if (Status == ExpressionFlagStatus.Unclaimed)
        {
            Status = newStatus;
            errorMessage = null;
            return true;
        }
        if (requireClaiming)
        {
            errorMessage = $"Flag '{Flag.Key}' cannot be claimed (with requireClaiming=true) by {ClaimerNameFor(newStatus)}, it is already claimed by {ClaimerNameFor(Status)}";
            return false;
        }
        else if ((Status & ExpressionFlagStatus.Exclusive) == 0)
        {
            errorMessage = null;
            return true;
        }
        else
        {
            errorMessage = $"Flag '{Flag.Key}' cannot be used or claimed by {ClaimerNameFor(newStatus)}, it is already exclusively claimed by {ClaimerNameFor(Status)}";
            // Exclusive, cannot override
            return false;
        }

    } 
}

public record DualExpressionFlags(ReadOnlyCollection<ClaimableExpressionFlag> Flags)
{
    public bool IsEmpty => Flags.Count == 0;

    public static DualExpressionFlags Empty => new DualExpressionFlags(new ReadOnlyCollection<ClaimableExpressionFlag>([]));

    public int UnclaimedCount => Flags.Count(f => f.Status == ExpressionFlagStatus.Unclaimed);

    public override string ToString()
    {
        if (IsEmpty)
        {
            return "[]";
        }
        return "[" + string.Join(",", Flags) + "]";
    }

    public bool Contains(string flag)
    {
        return Flags.Any(f => f.Flag.Key.Equals(flag, StringComparison.OrdinalIgnoreCase) && f.Flag.Value == null);
    }
    public bool TryGet(string flag, [NotNullWhen(true)] out ClaimableExpressionFlag? value)
    {
        value = Flags.FirstOrDefault(f => f.Flag.Key.Equals(flag, StringComparison.OrdinalIgnoreCase));
        return value != null;
    }
    public bool ClaimForMatcher(string flag)
    {
        if (TryGet(flag, out var claimableFlag))
        {
            return claimableFlag.TrySetStatus(ExpressionFlagStatus.Matcher, false, out _);
        }
        return false;
    }
    public bool ClaimForTemplate(string flag)
    {
        if (TryGet(flag, out var claimableFlag))
        {
            return claimableFlag.TrySetStatus(ExpressionFlagStatus.Template, false, out _);
        }
        return false;
    }

    public static DualExpressionFlags? Combine(DualExpressionFlags? a, DualExpressionFlags? b)
    {
        if (a == null)
        {
            return b;
        }
        if (b == null)
        {
            return a;
        }
        var newFlags = new List<ClaimableExpressionFlag>(a.Flags);
        newFlags.AddRange(b.Flags);
        return new DualExpressionFlags(new ReadOnlyCollection<ClaimableExpressionFlag>(newFlags));
    }

    public DualExpressionFlags Combine(DualExpressionFlags? other)
    {
        if (other == null || other.IsEmpty)
        {
            return this;
        }
        if (IsEmpty)
        {
            return other;
        }
        var newFlags = new List<ClaimableExpressionFlag>(Flags);
        newFlags.AddRange(other.Flags);
        return new DualExpressionFlags(new ReadOnlyCollection<ClaimableExpressionFlag>(newFlags));
    }

    public static DualExpressionFlags FromExpressionFlags(ExpressionFlags? expressionFlags, ExpressionFlagOrigin origin)
    {
        if (expressionFlags == null)
        {
            return DualExpressionFlags.Empty;
        }
        var newFlags = expressionFlags.Flags.Select(f => new ClaimableExpressionFlag(new DualExpressionFlag(f, null, origin))).ToList();
        newFlags.AddRange(expressionFlags.Pairs.Select(kv => new ClaimableExpressionFlag(new DualExpressionFlag(kv.Key, kv.Value, origin))));
        return new DualExpressionFlags(new ReadOnlyCollection<ClaimableExpressionFlag>(newFlags));
    }
    public static DualExpressionFlags FromExpressionFlags(ExpressionFlags? matcherFlags, ExpressionFlags? templateFlags)
    {
        var fromMatcher = FromExpressionFlags(matcherFlags, ExpressionFlagOrigin.AfterMatcher);
        var fromTemplate = FromExpressionFlags(templateFlags, ExpressionFlagOrigin.AfterTemplate);
        return Combine(fromMatcher, fromTemplate) ?? DualExpressionFlags.Empty;
    }


}

public partial record ExpressionFlags(ReadOnlyCollection<string> Flags, ReadOnlyCollection<KeyValuePair<string, string>> Pairs)
    {
        public bool IsEmpty => Flags.Count == 0 && Pairs.Count == 0;

        public override string ToString()
        {
            if (IsEmpty)
            {
                return string.Empty;
            }
            return "[" + string.Join(",", Flags) + string.Join(",", Pairs.Select(kv => $"{kv.Key}={kv.Value}")) + "]";
        }

        public static ExpressionFlags? Combine(ExpressionFlags? a, ExpressionFlags? b)
        {
            if (a == null)
            {
                return b;
            }
            if (b == null)
            {
                return a;
            }
            var newFlags = new List<string>(a.Flags);
            newFlags.AddRange(b.Flags);
            var newPairs = new List<KeyValuePair<string, string>>(a.Pairs);
            newPairs.AddRange(b.Pairs);
            return new ExpressionFlags(new ReadOnlyCollection<string>(newFlags), new ReadOnlyCollection<KeyValuePair<string, string>>(newPairs));
        }

        private static ReadOnlyMemory<char> TrimEnd(ReadOnlyMemory<char> text)
        {
#if NET6_0_OR_GREATER
            return text.TrimEnd();
#else
        var span = text.Span.TrimEnd();
        return text.Slice(0, span.Length);
#endif
        }


        public static bool TryParseFromEnd(ReadOnlyMemory<char> expression, out ReadOnlyMemory<char> remainingExpression, out ExpressionFlags? flags,
            [NotNullWhen(false)]
        out string? error, ExpressionFlagParsingOptions options)
        {
            flags = null;

            var success = TryParseFromEnd(expression, out remainingExpression, out var flagsList, out var pairs, out error, options);
            if (success)
            {
                flags = new ExpressionFlags(new ReadOnlyCollection<string>(flagsList ?? []), new ReadOnlyCollection<KeyValuePair<string, string>>(pairs ?? []));
                return true;
            }
            error ??= "Failed to parse flags";
            return false;
        }
        /// <summary>
        /// Parses the flags from the end of the expression. Syntax is [flag1,flag2,flag3]
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="remainingExpression"></param>
        /// <param name="result"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        private static bool TryParseFromEnd(ReadOnlyMemory<char> expression, out ReadOnlyMemory<char> remainingExpression, out List<string>? result, out List<KeyValuePair<string, string>>? resultPairs,
            [NotNullWhen(false)]
        out string? error, ExpressionFlagParsingOptions options)
        {
            List<string>? flags = null;
            List<KeyValuePair<string, string>>? pairs = null;
            if (options.TrimTrailingExpressionWhitespace)
            {
                expression = TrimEnd(expression);
            }

            var span = expression.Span;
            if (options.AllowWhitespaceAfterBrackets)
            {
                span = span.TrimEnd();
            }

            if (span.Length == 0 || span[^1] != ']')
            {
                //It's ok for there to be none
                result = null;
                resultPairs = null;
                error = null;
                remainingExpression = expression;
                return true;
            }
            var startAt = span.LastIndexOf('[');
            if (startAt == -1)
            {
                result = null;
                resultPairs = null;
                error = "Flags must be enclosed in [], only found closing ]";
                remainingExpression = expression;
                return false;
            }
            remainingExpression = options.TrimTrailingExpressionWhitespace ? TrimEnd(expression[..startAt]) : expression[..startAt];
            var inner = expression[(startAt + 1)..^1];
            var innerSpan = inner.Span;
            while (innerSpan.Length > 0)
            {
                var commaIndex = innerSpan.IndexOf(',');
                flags ??= [];

                if (commaIndex == -1)
                {
                    flags.Add(options.TrimWhitespaceAroundFlags ? inner.Span.Trim().ToString() : inner.Span.ToString());
                    break;
                }
                flags.Add(options.TrimWhitespaceAroundFlags ? inner.Span[..commaIndex].Trim().ToString() : inner.Span[..commaIndex].ToString());
                inner = inner[(commaIndex + 1)..];
                innerSpan = inner.Span;
            }
            if (flags != null)
            {
                // validate flags
                foreach (var flag in flags)
                {
                    if (!options.ValidationRegex.IsMatch(flag))
                    {
                        result = null;
                        error = $"Invalid flag '{flag}', it does not match the required format {options.ValidationRegex}.";
                        resultPairs = null;
                        return false;
                    }
                }
            }
            // Handle [flag][flag]etc, recursively
            if (!TryParseFromEnd(remainingExpression, out var remainingExpression2, out var flags2, out var pairs2, out error, options))
            {
                remainingExpression = remainingExpression2;
                if (flags2 != null)
                {
                    flags ??= [];
                    flags.AddRange(flags2);
                }
                if (pairs2 != null)
                {
                    pairs ??= [];
                    pairs.AddRange(pairs2);
                }
                result = flags;
                resultPairs = pairs;
                return false;
            }
            // We might have found no additional ones, flags2.count == 0 is the only way to know.
            remainingExpression = remainingExpression2;
            if (flags2 != null)
            {
                flags ??= [];
                flags.AddRange(flags2);
            }
            if (pairs2 != null)
            {
                pairs ??= [];
                pairs.AddRange(pairs2);
            }
            if (flags != null)
            {
                foreach (var flag in flags)
                {
                    var equalIndex = flag.IndexOf('=');
                    if (equalIndex > -1)
                    {
                        pairs ??= [];
                        pairs.Add(new KeyValuePair<string, string>(flag[..equalIndex], flag[(equalIndex + 1)..]));
                    }
                }
            }
            result = flags?.Where(f => !f.Contains('=')).ToList();
            resultPairs = pairs;
            error = null;
            return true;
        }

#if NET8_0_OR_GREATER
        [GeneratedRegex(@"^[a-zA-Z-][a-zA-Z0-9-]*$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline)]
        public static partial Regex AlphaNumericDash();
#else

    public static readonly Regex AlphaNumericDashVar =
        new(@"^[a-zA-Z-][a-zA-Z0-9-]*$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50));

    public static Regex AlphaNumericDash() => AlphaNumericDashVar;
#endif

#if NET8_0_OR_GREATER
        [GeneratedRegex(@"^[a-z-]*$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline)]
        public static partial Regex LowercaseDash();
#else

    public static readonly Regex LowercaseDashVar =
        new(@"^[a-z-]*$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50));

    public static Regex LowercaseDash() => LowercaseDashVar;
#endif

        // Allow k/v pairs as well, [a-zA-Z-][a-zA-Z0-9-]*([=][a-zA-Z0-9-]+)?
#if NET8_0_OR_GREATER
        [GeneratedRegex(@"^([a-zA-Z-_][a-zA-Z0-9-\.]*([=][a-zA-Z0-9-_\.]+)?|/)$")]
        public static partial Regex LatestPermissiveFlagSyntax();
#else

    public static readonly Regex LatestPermissiveFlagSyntaxVar =
        new(@"^([a-zA-Z-_][a-zA-Z0-9-\.]*([=][a-zA-Z0-9-_\.]+)?|/)$");

    public static Regex LatestPermissiveFlagSyntax() => LatestPermissiveFlagSyntaxVar;
#endif

    }
