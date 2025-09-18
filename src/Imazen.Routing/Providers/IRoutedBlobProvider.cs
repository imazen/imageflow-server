using Imazen.Abstractions; 
using Imazen.Abstractions.Resulting;
using Imazen.Common.Issues;
using Imazen.Routing.Matching;
using Imazen.Routing.Promises;
using Imazen.Routing.Requests;
using Imazen.Routing.RoutingExpressions;
using System.Collections.Generic;

namespace Imazen.Routing.Providers
{

    public interface IRoutedBlobProvider
    {
        IReadOnlyCollection<string> RespondsToStaticPrefixes { get; }
        IReadOnlyCollection<string> ProviderNames { get; }

        bool NeedsUri { get; }

        bool ProvidesFor(RouteProviderInfo providerInfo);
        bool RespondsTo(string? path, ICollection<KeyValuePair<string?, string?>>? query, RouteProviderInfo providerInfo);
        ValueTask<CodeResult<ICacheableBlobPromise>?> GetBlobAsync(string? path, ICollection<KeyValuePair<string?, string?>>? query, 
            Uri? uri, RouteProviderInfo providerInfo, IRequestSnapshot request, CancellationToken cancellationToken);

    }
    // Initially, planned on clean s3://example-bucket/path/to/object ... except we need to specify region or have a RT
    // endpoint. So we need to do s3://example-bucket/path/to/object?region=us-east-1
    // 
    // And more stuff keeps coming up. 
    // So, we also can offer something like custom://?arg1=value&arg2=value etc, because we have structural query stuff
    // At least, in theory. Let's do it and test it? We also might want to pass in a credential set name?

    /// <summary>
    /// By implementing this, you can (at runtime) change the set of IRoutedBlobProvider instances that are available.
    /// </summary>
    public interface IRoutedBlobProviderGroup : IUniqueNamed, IIssueProvider
    {
        IReadOnlyCollection<IRoutedBlobProvider> Providers { get; }
        // Event to push IOptionsMonitor to listen to
        event Action OnProvidersChanged;
       
    }
}
