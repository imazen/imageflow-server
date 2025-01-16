using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Imageflow.Server.AzureFunctions
{
    public class UniversalHttpHandler
    {
        [Function("CatchAll")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "put", "delete", Route = "{*path}")] HttpRequestData req,
            FunctionContext executionContext)
        {
            
            // Extract request details
            var method = req.Method;
            var uri = req.Url;
            var headers = req.Headers;
            var body = await req.ReadAsStringAsync();

            // Perform arbitrary handling
            // (Use headers, method, uri, body as needed for custom logic)

            // Create and return a response
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            await response.WriteStringAsync("Request handled.");
            return response;
        }
    }
}
