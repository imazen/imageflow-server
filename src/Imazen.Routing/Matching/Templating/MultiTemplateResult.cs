namespace Imazen.Routing.Matching.Templating;

public record struct MultiTemplateResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    
    public TemplateResult PathResult { get; init; }
    public TemplateResult[]? QueryResults { get; init; }
}
