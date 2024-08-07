namespace Imazen.Routing.Matching.Templating;

public record struct TemplateSegment
{
    public TemplateSegmentKind Kind { get; init; }
    
    public string? Literal { get; init; }
    
    public TemplateExpression? Expression { get; init; }
    
    public TemplateSegment(string literal)
    {
        Kind = TemplateSegmentKind.Literal;
        Literal = literal;
    }
    public TemplateSegment(TemplateExpression expression)
    {
        Kind = TemplateSegmentKind.Expression;
        Expression = expression;
    }
}