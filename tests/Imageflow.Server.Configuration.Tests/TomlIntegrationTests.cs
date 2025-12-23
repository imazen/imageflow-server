using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Imageflow.Server.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

// We're going to setup s3 mocking, filesystem prep, azure mocking, with and without caching, then run different toml configurations with that. 
// Deriving from Imageflow.Server.Tests IntegrationTests.cs and Imageflow.Server.Host Startup.cs 
// We reference and can use TempContentRoot from Imageflow.Server.Tests

namespace Imageflow.Server.Configuration.Tests
{
    public class TomlIntegrationTests
    {
        private readonly ITestOutputHelper _output;

        public TomlIntegrationTests(ITestOutputHelper output)
        {
            this._output = output;
        }

        [Fact(Skip = "Route expression layer integration not yet complete")]
        public async Task TestFileSystemOnly()
        {
            using var contentRoot = new TempContentRoot(_output)
                .AddResource("images/fire.jpg", "TestFiles.fire-umbrella-small.jpg");

            var physicalPath = contentRoot.PhysicalPath.Replace("\\", "/");
            var toml = """
            [imageflow_server]
            config_schema = '1.0'
            [license]
            enforcement = "watermark" # or http_402_error/http_422_error, for fast failures
            key = ""
            [[routes]]
            route = '/images/{{**path}} => " + physicalPath + "/images/{{path}}'
            """;

            var context = new TomlParserContext(DeploymentEnvironment.Production, new(), _ => null, new TestFileMethods(new()));
            var result = TomlParser.Parse(toml, "test.toml", context);
            var configurator = result.GetAppConfigurator();

            var hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddSingleton<IAbstractFileMethods>(new TestFileMethods(new()));
                        configurator.ConfigureServices(services);
                    });
                    webHost.Configure(app => { configurator.ConfigureApp(app, (IWebHostEnvironment)app.ApplicationServices.GetService(typeof(IWebHostEnvironment))! ); });
                });

            using var host = await hostBuilder.StartAsync(TestContext.Current.CancellationToken);
            using var client = host.GetTestClient();

            using var response = await client.GetAsync("/images/fire.jpg?width=10", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        }
    }
}
