
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using Imazen.Abstractions.Blobs;
using Imazen.Abstractions.Blobs.LegacyProviders;
using Imazen.Abstractions.Logging;
using Imazen.Common.Instrumentation;
using Imazen.Common.Instrumentation.Support.InfoAccumulators;
using Imazen.Common.Licensing;
using Imazen.Abstractions.BlobCache;
using Imazen.Common.Storage;
using Imazen.Common.Extensibility.ClassicDiskCache;
using Imazen.Common.Extensibility.StreamCache;
using Imazen.Routing.Serving;
using Imazen.Routing.Matching.Templating;
using Imazen.Common.Issues;
using Imazen.Abstractions;
using Microsoft.Extensions.Hosting;
using Imazen.Routing.Layers;

/// <summary>
/// Looks up all known interfaces that would be registered in DI,
/// attempts to cast them to all the interface types they should also be registered under
/// and verifies they *are*
/// </summary> 
internal class DependencyRegistrationHealth{

    private static void CheckRegistered<T>(IServiceProvider serviceProvider, object instance, List<string> issues)  
     where T : class{
        if (instance is T casted)
        {
            if (serviceProvider.GetServices<T>().All(x => x != casted))
            {
                issues.Add($"{instance.GetType().FullName} was not registered as {typeof(T).FullName} despite implementing it.");
            }
        }
    }

    private static IEnumerable<object> GetServices<T>(IServiceProvider serviceProvider, List<string> issues){
        var services = serviceProvider.GetServices<T>().ToList();

        if (services.Distinct().Count() != services.Count())
        {
            var duplicates = services.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            foreach (var duplicate in duplicates)
            {
                issues.Add("The same instance of " + duplicate?.GetType().FullName + " was registered multiple times for " + typeof(T).Name);            }
        }
        return services.Select(x => (object)x!);
    }

    public static List<string> ReturnProblems(IServiceProvider serviceProvider){

        var types = new List<object>();
        var issues = new List<string>();
#pragma warning disable CS0618 // Type or member is obsolete
        types.AddRange(GetServices<IClassicDiskCache>(serviceProvider, issues));
        types.AddRange(GetServices<IStreamCache>(serviceProvider, issues));

        types.AddRange(GetServices<IBlobProvider>(serviceProvider, issues));
#pragma warning restore CS0618 // Type or member is obsolete
        types.AddRange(GetServices<IBlobWrapperProvider>(serviceProvider, issues));
        types.AddRange(GetServices<IBlobWrapperProviderZoned>(serviceProvider, issues));
        types.AddRange(GetServices<IBlobCache>(serviceProvider, issues));
        types.AddRange(GetServices<IUniqueNamed>(serviceProvider, issues));
        types.AddRange(GetServices<IBlobCacheProvider>(serviceProvider, issues));
        types.AddRange(GetServices<IHostedImageServerService>(serviceProvider, issues));
        types.AddRange(GetServices<IHostedService>(serviceProvider, issues));
        types.AddRange(GetServices<IInfoProvider>(serviceProvider, issues));
        types.AddRange(GetServices<IReLoggerFactory>(serviceProvider, issues));
        types.AddRange(GetServices<IIssueProvider>(serviceProvider, issues));
        types.AddRange(GetServices<IHasDiagnosticPageSection>(serviceProvider, issues));
        types.AddRange(GetServices<IRoutingLayer>(serviceProvider, issues));
        types.AddRange(GetServices<IRoutingEndpoint>(serviceProvider, issues));
        types.AddRange(GetServices<ILicenseChecker>(serviceProvider, issues));
        //types.AddRange(serviceProvider.GetServices<IImageServer<IHttpRequestStreamAdapter, IHttpResponseStreamAdapter, HttpContext>>().Select(x => (object)x));

        foreach (var t in types){
            CheckRegistered<IHasDiagnosticPageSection>(serviceProvider, t, issues);
            CheckRegistered<IIssueProvider>(serviceProvider, t, issues);
            CheckRegistered<IUniqueNamed>(serviceProvider, t, issues);
            CheckRegistered<IBlobCacheProvider>(serviceProvider, t, issues);
            CheckRegistered<IInfoProvider>(serviceProvider, t, issues);
            CheckRegistered<IHostedImageServerService>(serviceProvider, t, issues);
            CheckRegistered<IHostedService>(serviceProvider, t, issues);
            CheckRegistered<IRoutingLayer>(serviceProvider, t, issues);
            CheckRegistered<IRoutingEndpoint>(serviceProvider, t, issues);

        }
        return issues;
    }

    public static void ThrowOnProblems(IServiceProvider serviceProvider){
        var issues = ReturnProblems(serviceProvider);
        if (issues.Count > 0){
            throw new Exception("Dependency registration issues:\n" + string.Join("\n", issues));
        }
    }
}
