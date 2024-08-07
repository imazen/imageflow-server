namespace Imazen.Routing.Matching.Templating;

// For both querystring values and paths
public record StringTemplate(TemplateSegment[] Segments, bool TemplateOptional)
{
    
}