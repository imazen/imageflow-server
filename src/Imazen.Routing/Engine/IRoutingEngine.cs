using Imazen.Abstractions.Logging;
using Imazen.Abstractions.Resulting;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Layers;
using Imazen.Routing.Requests;
using Microsoft.Extensions.Logging;

namespace Imazen.Routing.Engine;
public interface IRoutingEngine : IBlobRequestRouter{
    bool MightHandleRequest<TQ>(string path, TQ query) where TQ : IReadOnlyQueryWrapper;
    ValueTask<CodeResult<IRoutingEndpoint>?> Route(MutableRequest request, CancellationToken cancellationToken = default);
}