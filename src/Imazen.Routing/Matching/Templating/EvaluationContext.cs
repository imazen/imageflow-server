namespace Imazen.Routing.Matching.Templating;

/// <summary>
/// Holds state during the evaluation of a single VariableSegment's transformations.
/// </summary>
public class EvaluationContext
{
    /// <summary>
    /// Set to true by a MapTransform if it successfully matched and changed the value.
    /// </summary>
    public bool MapMatched { get; set; } = false;
} 