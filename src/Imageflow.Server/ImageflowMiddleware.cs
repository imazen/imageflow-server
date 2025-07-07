using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Imazen.Common.Extensibility.ClassicDiskCache;
using Imazen.Common.Extensibility.StreamCache;
using Imazen.Common.Instrumentation;
using Imazen.Common.Instrumentation.Support.InfoAccumulators;
using Imazen.Common.Licensing;
using Imazen.Common.Storage;
using Imazen.Abstractions.BlobCache;
using Imageflow.Server.Internal;
using Imazen.Abstractions.Blobs.LegacyProviders;

using Imazen.Abstractions.Logging;
using Imazen.Routing.Health;
using Imazen.Routing.Serving;
using Microsoft.Extensions.DependencyInjection;

namespace Imageflow.Server
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ImageflowMiddleware
    {
        private readonly RequestDelegate next;
        private readonly GlobalInfoProvider globalInfoProvider;
        private readonly IImageServer<RequestStreamAdapter,ResponseStreamAdapter, HttpContext>  imageServer;
        public ImageflowMiddleware(
            RequestDelegate next,
            GlobalInfoProvider globalInfoProvider,
            IImageServer<RequestStreamAdapter,ResponseStreamAdapter, HttpContext> imageServer)
        {
            this.imageServer = imageServer;
            this.next = next;
            this.globalInfoProvider = globalInfoProvider;
        }

        private bool hasPopulatedHttpContextExample = false;
        public async Task Invoke(HttpContext context)
        {
            
            var queryWrapper = new QueryCollectionWrapper(context.Request.Query);
            // We can optimize for the path where we know we won't be handling the request
            if (!imageServer.MightHandleRequest(context.Request.Path.Value, queryWrapper, context))
            {
                await next.Invoke(context);
                return;
            }
            // If we likely will be handling it, we can allocate the shims
            var requestAdapter = new RequestStreamAdapter(context.Request);
            var responseAdapter = new ResponseStreamAdapter(context.Response);
            
            //For instrumentation
            if (!hasPopulatedHttpContextExample){
                hasPopulatedHttpContextExample = true;
                globalInfoProvider.CopyHttpContextInfo(requestAdapter);
            }
            
            if (await imageServer.TryHandleRequestAsync(requestAdapter, responseAdapter, context, context.RequestAborted))
            {
                return; // We handled it
            }
            await next.Invoke(context);
 
        }
    }
}
