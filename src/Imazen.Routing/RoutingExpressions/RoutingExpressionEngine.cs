using Imazen.Routing.Matching;
using Imazen.Routing.Matching.Templating;
using Imazen.Routing.Requests;

namespace Imazen.Routing.RoutingExpressions;

public record struct RoutingResult(string? PathAndQuery, ProviderInfo? ProviderInfo){
    public static readonly RoutingResult NotFound = new(null, null);

    public bool IsNotFound => PathAndQuery == null && ProviderInfo == null;
}
public record struct RoutingExpressionEngine(ParsedRoutingExpression ParsedRoutingExpression){

    private static readonly Dictionary<string, string> EmptyDictionary = [];
    
    public readonly RoutingResult Evaluate(MatchingContext matchingContext, MutableRequest request, in string? headerAsQuery = null){
        
        var result = ParsedRoutingExpression.Matcher.Match(matchingContext, request, headerAsQuery);
        
        if(result.Success){
            bool templated = ParsedRoutingExpression.Template.TryEvaluate(result.Captures ?? EmptyDictionary, out string? routedPathAndQuery, out string? error);
            if(!templated){
                return RoutingResult.NotFound;
            }
            return new RoutingResult(routedPathAndQuery, ParsedRoutingExpression.ProviderInfo);
        }
        return RoutingResult.NotFound;
    }
    
    public readonly RoutingResult Evaluate(MatchingContext matchingContext, in string pathAndQuery, in string? headerAsQuery = null){
        
        var result = ParsedRoutingExpression.Matcher.Match(matchingContext, pathAndQuery, headerAsQuery);
        
        if(result.Success){
            bool templated = ParsedRoutingExpression.Template.TryEvaluate(result.Captures ?? EmptyDictionary, out string? routedPathAndQuery, out string? error);
            if(!templated){
                return RoutingResult.NotFound;
            }
            return new RoutingResult(routedPathAndQuery, ParsedRoutingExpression.ProviderInfo);
        }
        return RoutingResult.NotFound;
    }

    public readonly override string ToString(){
        return ParsedRoutingExpression.ToString();
    }
}
