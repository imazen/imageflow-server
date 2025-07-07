using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Layers;
using Imazen.Routing.Requests;

namespace Imazen.Routing.Engine;

public class RoutingBuilder
{
    
    public RoutingGroupBuilder Endpoints { get; } = new("Endpoints");
    
    public RoutingGroupBuilder Media { get; } = new("Media");
    public RoutingBuilder ConfigureNewGroup(string name, Action<RoutingGroupBuilder> configure)
    {
        var group = new RoutingGroupBuilder(name);
        configure(group);
        Groups.Add(group);
        return this;
    }

    private List<RoutingGroupBuilder> Groups { get; } = new();
    
    public RoutingBuilder ConfigureMedia(Action<RoutingGroupBuilder> configure)
    {
        configure(Media);
        return this;
    }
    
    public RoutingBuilder ConfigureEndpoints(Action<RoutingGroupBuilder> configure)
    {
        configure(Endpoints);
        return this;
    }
    

    public RoutingBuilder AddMediaLayer(IRoutingLayer layer)
    {
        Media.AddLayer(layer);
        return this;
    }
    public RoutingBuilder AddEndpointLayer(IRoutingLayer layer)
    {
        Endpoints.AddLayer(layer);
        return this;
    }
    //Add predefined reusable responses
    
    /// <summary>
    /// Adds an endpoint that is not subject to the media pipeline preconditions.
    /// </summary>
    /// <param name="fastMatcher"></param>
    /// <param name="response"></param>
    /// <returns></returns>
    public RoutingBuilder AddEndpoint(IFastCond fastMatcher, IAdaptableReusableHttpResponse response)
    {
        var endpoint = new PredefinedResponseEndpoint(response);
        return AddEndpoint(fastMatcher, endpoint);
    }

    /// <summary>
    /// Adds an endpoint that is not subject to the media pipeline preconditions.
    /// </summary>
    /// <param name="fastMatcher"></param>
    /// <param name="handler"></param>
    /// <returns></returns>
    public RoutingBuilder AddEndpoint(IFastCond fastMatcher, Func<IRequestSnapshot, IAdaptableHttpResponse> handler)
    {
        var endpoint = new SyncEndpointFunc(handler);
        return AddEndpoint(fastMatcher, endpoint);
    }
    /// <summary>
    /// Adds an endpoint that is not subject to the media pipeline preconditions.
    /// </summary>
    /// <param name="fastMatcher"></param>
    /// <param name="endpoint"></param>
    /// <returns></returns>
    private RoutingBuilder AddEndpoint(IFastCond fastMatcher, IRoutingEndpoint endpoint)
    {
        Endpoints.AddLayer(new SimpleLayer(fastMatcher.ToString() ?? "(error describing route)",
            (request) => fastMatcher.Matches(request.MutablePath, request.ReadOnlyQueryWrapper) ? 
                CodeResult<IRoutingEndpoint>.Ok(endpoint) : null, fastMatcher));
        return this;
    }

    public IRoutingEngine Build(IReLogger logger)
    {
        return new RoutingEngine(Groups.Concat(new[] { Media, Endpoints}).Select(g => g.Build()).ToArray(), logger);
    }
}