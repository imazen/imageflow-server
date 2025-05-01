using System.Collections.Generic;
using System.Linq; // Added for LINQ

namespace Imazen.Routing.Matching.Templating;

/// <summary>
/// Represents a transformation to be applied to a variable's value during template evaluation.
/// </summary>
public interface ITransformation
{
    /// <summary>
    /// Applies the transformation to the current value.
    /// </summary>
    /// <param name="currentValue">The value before transformation (can be null).</param>
    /// <param name="variables">All captured variables available during evaluation.</param>
    /// <param name="context">Evaluation context for the current variable segment.</param>
    /// <returns>The transformed value (can be null).</returns>
    string? Apply(string? currentValue, IDictionary<string, string> variables, EvaluationContext context);
}

// Concrete Transformation Records (can be in separate files or here initially)

public record ToLowerTransform : ITransformation
{
    public string? Apply(string? currentValue, IDictionary<string, string> variables, EvaluationContext context) =>
        currentValue?.ToLowerInvariant();
}

public record ToUpperTransform : ITransformation
{
    public string? Apply(string? currentValue, IDictionary<string, string> variables, EvaluationContext context) =>
        currentValue?.ToUpperInvariant();
}

public record UrlEncodeTransform : ITransformation
{
    public string? Apply(string? currentValue, IDictionary<string, string> variables, EvaluationContext context) =>
        // Uri.EscapeDataString is closer to RFC3986 than WebUtility.UrlEncode (encodes space as %20)
        currentValue == null ? null : System.Uri.EscapeDataString(currentValue);
}

public record MapTransform(IReadOnlyList<(string From, string To)> Mappings) : ITransformation
{
    public string? Apply(string? currentValue, IDictionary<string, string> variables, EvaluationContext context)
    {
        if (currentValue == null) return null;
        foreach (var (from, to) in Mappings)
        {
            // Ordinal comparison is standard for internal matching
            if (string.Equals(currentValue, from, StringComparison.Ordinal))
            {
                context.MapMatched = true; // Signal that a map matched
                return to; // First match wins
            }
        }
        return currentValue; // No map matched
    }
}

public record OrTransform(string FallbackVariableName) : ITransformation
{
    public string? Apply(string? currentValue, IDictionary<string, string> variables, EvaluationContext context)
    {
        if (string.IsNullOrEmpty(currentValue))
        {
            if (variables.TryGetValue(FallbackVariableName, out var fallbackValue) && !string.IsNullOrEmpty(fallbackValue))
            {
                return fallbackValue;
            }
        }
        return currentValue;
    }
}

public record DefaultTransform(string DefaultValue) : ITransformation
{
    public string? Apply(string? currentValue, IDictionary<string, string> variables, EvaluationContext context)
    {
        // Default applies if the *current* value is null/empty. It doesn't check the original variable.
        return string.IsNullOrEmpty(currentValue) ? DefaultValue : currentValue;
    }
}

/// <summary>
/// Marker transform, doesn't change the value but flags the segment for optional handling.
/// </summary>
public record OptionalMarkerTransform : ITransformation
{
    public string? Apply(string? currentValue, IDictionary<string, string> variables, EvaluationContext context) => currentValue; // No-op
}

/// <summary>
/// Passes the value through only if it equals one of the specified allowed values (case-sensitive ordinal). Returns null otherwise.
/// </summary>
public record EqualsTransform(IReadOnlyList<string> AllowedValues) : ITransformation
{
    // Using HashSet for efficient lookup
    private readonly HashSet<string> _allowedSet = new HashSet<string>(AllowedValues, StringComparer.Ordinal);

    public string? Apply(string? currentValue, IDictionary<string, string> variables, EvaluationContext context)
    {
        if (currentValue != null && _allowedSet.Contains(currentValue))
        {
            return currentValue;
        }
        return null; // Value not allowed/equal
    }
}

/// <summary>
/// Provides a default value only if no preceding MapTransform matched.
/// </summary>
public record MapDefaultTransform(string DefaultValue) : ITransformation
{
    public string? Apply(string? currentValue, IDictionary<string, string> variables, EvaluationContext context)
    {
        if (!context.MapMatched) { return DefaultValue; }
        return currentValue;
    }
} 