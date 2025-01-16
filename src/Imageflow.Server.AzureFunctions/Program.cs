using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;
using Imageflow.Server;
using Imageflow.Server.AzureFunctions;
using Microsoft.Extensions.DependencyInjection;
using Imageflow.Server.Storage.AzureBlob;
using Microsoft.Extensions.Azure;
using Imageflow.Server.Storage.RemoteReader;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddImageflowLoggingSupport();

builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.AddBlobServiceClient("UseDevelopmentStorage=true");
});

builder.Services.AddHttpClient();

builder.Services.AddImageflowAzureBlobService(
    AzureBlobServiceOptions.ICalledAddBlobServiceClient()
    .MapPrefix("/i/", "images")
    );

builder.Services.AddImageflowDomainProxyService(new DomainProxyServiceOptions()
    .AddPrefix("/api/iio/", "https://www.imazen.io/")
    .AddPrefix("/iio/", "https://www.imazen.io/")
    );

builder.Services.AddSingleton<ImageflowMiddlewareOptions>(
    new ImageflowMiddlewareOptions()
    .SetMyOpenSourceProjectUrl("https://github.com/imazen/imageflow-dotnet-server")
    );

builder.UseMiddleware<ImageflowFunctionMiddleware>();


// Application Insights isn't enabled by default. See https://aka.ms/AAt8mw4.
// builder.Services
//     .AddApplicationInsightsTelemetryWorkerService()
//     .ConfigureFunctionsApplicationInsights();

builder.Build().Run();
