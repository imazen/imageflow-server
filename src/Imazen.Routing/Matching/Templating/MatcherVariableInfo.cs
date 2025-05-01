namespace Imazen.Routing.Matching.Templating; // Or Imazen.Routing.Matching? Templating seems slightly better.

/// <summary>
/// Describes a variable captured by a match expression, for template validation purposes.
/// </summary>
/// <param name="Name">The name of the captured variable.</param>
/// <param name="IsOptional">Whether the variable capture is optional in the match expression.</param>
public record MatcherVariableInfo(string Name, bool IsOptional); 