using Imazen.Abstractions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Imageflow.Server
{
    public static class ImageflowMiddlewareExtensions
    {


        public static IServiceCollection ConfigureImageflowMiddleware(this IServiceCollection services,  ImageflowMiddlewareOptions options)

        {
            var builder = new Internal.MiddlewareOptionsServerBuilder(services,options);
            builder.RegisterServices();
            return services;
        }


        public static WebApplication UseImageflow(this WebApplication app)
        { 
            var options = app.Services.GetService<ImageflowMiddlewareOptions>();
            if (options == null)
            {
                throw new System.Exception("You must call services.ConfigureImageflowMiddleware() before calling UseImageflow");
            }

            app.UseMiddleware<ImageflowMiddleware>();
            return app;
        }

        public static IApplicationBuilder UseImageflow(this IApplicationBuilder builder)
        {
            var options = builder.ApplicationServices.GetService<ImageflowMiddlewareOptions>();
            if (options == null)
            {
                throw new System.Exception("You must call services.ConfigureImageflowMiddleware() before calling UseImageflow");
            }
            return builder.UseMiddleware<ImageflowMiddleware>();
        }


    }
}
