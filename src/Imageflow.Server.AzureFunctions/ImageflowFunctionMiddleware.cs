
namespace Imageflow.Server.AzureFunctions;


using System.Threading.Tasks;
using Imageflow.Server.Internal;
using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Blobs.LegacyProviders;

using Imazen.Abstractions.Logging;
using Imazen.Common.Extensibility.ClassicDiskCache;
using Imazen.Common.Extensibility.StreamCache;
using Imazen.Common.Storage;
using Imazen.Routing.Health;
using Imazen.Routing.Serving;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static System.Net.Mime.MediaTypeNames;


public class ImageflowFunctionMiddleware : IFunctionsWorkerMiddleware
{
    private readonly GlobalInfoProvider globalInfoProvider;
    private readonly IImageServer<RequestStreamAdapter, ResponseStreamAdapter, HttpContext> imageServer;
    private bool hasPopulatedHttpContextExample = false;

    private IReLogger logger;
    internal ImageflowFunctionMiddleware(
            IReLoggerFactory loggerFactory,
            GlobalInfoProvider globalInfoProvider,
            IImageServer<RequestStreamAdapter,ResponseStreamAdapter, HttpContext> imageServer)
    {

        this.imageServer = imageServer;
        this.globalInfoProvider = globalInfoProvider;
        this.logger = loggerFactory.CreateReLogger("ImageflowMiddleware");
    }

    public async Task Invoke(FunctionContext funcContext, FunctionExecutionDelegate next)
    {

        var context = funcContext.GetHttpContext();
        if (context == null)
        {
            throw new InvalidOperationException("The HTTP request context could not be found.");
        }

        logger.LogInformation("ImageflowFunctionMiddleware.Invoke called");
        logger.LogInformation("context.Request.Path: {path}", context.Request.Path);
        logger.LogInformation("context.Request.PathBase: {pathBase}", context.Request.PathBase);
        logger.LogInformation("context.Request.Query: {query}", context.Request.Query);
        logger.LogInformation("context.Request.Method: {method}", context.Request.Method);
        logger.LogInformation("context.Request.Scheme: {scheme}", context.Request.Scheme);
        logger.LogInformation("context.Request.Host: {host}", context.Request.Host);
        logger.LogInformation("context.Request.Headers: {headers}", context.Request.Headers);
        logger.LogInformation("context.Request.Cookies: {cookies}", context.Request.Cookies);
        

        var queryWrapper = new QueryCollectionWrapper(context.Request.Query);
        // We can optimize for the path where we know we won't be handling the request
        if (!imageServer.MightHandleRequest(context.Request.Path.Value, queryWrapper, context))
        {
            logger.LogInformation("Imageflow MightHandleRequest returned false");
            await next.Invoke(funcContext);
            return;
        }
        // If we likely will be handling it, we can allocate the shims
        var requestAdapter = new RequestStreamAdapter(context.Request);
        var responseAdapter = new ResponseStreamAdapter(context.Response);

        //For instrumentation
        if (!hasPopulatedHttpContextExample)
        {
            hasPopulatedHttpContextExample = true;
            globalInfoProvider.CopyHttpContextInfo(requestAdapter);
        }
        

        if (await imageServer.TryHandleRequestAsync(requestAdapter, responseAdapter, context, context.RequestAborted))
        {
            logger.LogInformation("Imageflow handled request");
            return; // We handled it
        }
        logger.LogInformation("Imageflow TryHandleRequestAsync returned false");
        await next.Invoke(funcContext);

        // // This is added pre-function execution, function will have access to this information
        // // in the context.Items dictionary
        // context.Items.Add("middlewareitem", "Hello, from middleware");

        // await next(context);

        // // This happens after function execution. We can inspect the context after the function
        // // was invoked
        // if (context.Items.TryGetValue("functionitem", out object? value) && value is string message)
        // {
        //     ILogger logger = context.GetLogger<MyCustomMiddleware>();

        //     logger.LogInformation("From function: {message}", message);
        // }
    }
}
