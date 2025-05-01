using System.Collections.Generic;

namespace Imazen.Routing.Matching.Templating;

/// <summary>
/// Represents a segment of a parsed string template, either literal text or a variable substitution.
/// </summary>
public interface ITemplateSegment { }

/// <summary>
/// Represents a literal part of a template string.
/// </summary>
/// <param name="Value">The literal string value.</param>
public record LiteralSegment(string Value) : ITemplateSegment;

/// <summary>
/// Represents a variable substitution part ({...}) of a template string, including its transformations.
/// </summary>
/// <param name="VariableName">The name of the variable to substitute.</param>
/// <param name="Transformations">The list of transformations to apply, in order.</param>
public record VariableSegment(string VariableName, IReadOnlyList<ITransformation> Transformations) : ITemplateSegment; 