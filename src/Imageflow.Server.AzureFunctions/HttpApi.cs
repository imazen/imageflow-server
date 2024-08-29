using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Imageflow.Server.AzureFunctions
{
    public class HttpApi
    {
        private readonly ILogger<HttpApi> _logger;

        public HttpApi(ILogger<HttpApi> logger)
        {
            _logger = logger;
        }

        [Function("HttpApi")]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            return new OkObjectResult("Welcome to Azure Functions!");
        }
    }
}
