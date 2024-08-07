namespace Imazen.Routing.Matching.Templating;

/// <summary>
/// Can be empty/skipped (no result, but optional, so no error), have a result, or have an error.
/// </summary>
public record struct TemplateResult
{
    public bool Failed { get; init; }
    public bool HasResult { get; init; }
    public string TemplateKey { get; init; }
    public string? Error { get; init; }
    public string? Result { get; init; }
}