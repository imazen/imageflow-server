using Imageflow.Server;

using Imazen.Abstractions.Logging;
using Imazen.Routing.Promises.Pipelines.Watermarking;
using Imazen.Routing.Engine;
using Imazen.Routing.HttpAbstractions;
using Imazen.Routing.Layers;
using Imazen.Routing.Serving;
using Imazen.Tests.Routing.Serving;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Imazen.Routing.Tests.Serving;

internal class TestHttpContext
{
}
public class ImageServerTests : ReLoggerTestBase
{
    private readonly ILicenseChecker licenseChecker;
    private ServiceCollection sharedServices;
    private readonly ITestOutputHelper _output;

    public ImageServerTests( ITestOutputHelper output) :base("ImageServerTests")
    {
        _output = output;
        licenseChecker = MockLicenseChecker.AlwaysOK();
        sharedServices = new ServiceCollection();
        sharedServices.AddSingleton<ILicenseChecker>(licenseChecker);
        sharedServices.AddSingleton<IReLoggerFactory>(loggerFactory);
        sharedServices.AddSingleton<IReLogStore>(logStore);
        sharedServices.AddSingleton<LicenseOptions>((_) => new LicenseOptions
        {
            
            ProcessWideCandidateCacheFoldersDefault = new string[]
            {
                Path.GetTempPath() // Typical env.ContentRootPath as well
            }
        });
        sharedServices.AddSingleton(new DiagnosticsPageOptions(null, DiagnosticsPageOptions.AccessDiagnosticsFrom.AnyHost));
        sharedServices.MakeReadOnly();
    }

    private ServiceCollection DuplicateServiceCollection()
    {
        var s = new ServiceCollection();
        s.Add(sharedServices);
        return s;
    }

    internal IImageServer<MockRequestAdapter, MockResponseAdapter, TestHttpContext>
        CreateImageServer(
            IRoutingEngine routingEngine,
            ImageServerOptions imageServerOptions
        )
    {
        var c= DuplicateServiceCollection();
        c.AddSingleton(imageServerOptions);
        c.TryAddSingleton<IPerformanceTracker, NullPerformanceTracker>();
        c.AddSingleton(routingEngine);
        c.AddImageServer<MockRequestAdapter, MockResponseAdapter, TestHttpContext>();


        var provider = c.BuildServiceProvider();
        try
        {
            return provider.GetRequiredService<IImageServer<MockRequestAdapter, MockResponseAdapter, TestHttpContext>>();
        }catch (Exception e)
        {
            // List service descriptors
            foreach (var descriptor in c)
            {
                _output.WriteLine(descriptor.ImplementationType?.FullName ?? descriptor.ServiceType?.FullName);

            }
            throw;
        }
    }

    [Fact]
    public void TestMightHandleRequest()
    {
        var b = new RoutingBuilder();
        b.AddEndpoint(Conditions.PathEquals("/hi"),
            SmallHttpResponse.Text(200, "HI"));
        
        var imageServer = CreateImageServer(
            b.Build(logger),
            new ImageServerOptions());
       
        
        Assert.True(imageServer.MightHandleRequest("/hi", DictionaryQueryWrapper.Empty, new TestHttpContext()));

    }

    [Fact]
    public async Task TestTryHandleRequestAsync()
    {
        var b = new RoutingBuilder();
        b.AddEndpoint(Conditions.PathEquals("/hi"),
            SmallHttpResponse.Text(200, "HI"));
        
        var imageServer = CreateImageServer(
            b.Build(logger),
            new ImageServerOptions()
        );
        var request = MockRequest.GetLocalRequest("/hi").ToAdapter();
        var response = new MockResponseAdapter();
        var context = new TestHttpContext();
        var handled = await imageServer.TryHandleRequestAsync(request, response, context, TestContext.Current.CancellationToken);
        Assert.True(handled);
        var r = await response.ToMockResponse();
        
        Assert.Equal(200, r.StatusCode);
        Assert.Equal("HI", r.DecodeBodyUtf8());
    }
}
