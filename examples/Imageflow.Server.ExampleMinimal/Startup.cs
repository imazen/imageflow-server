using Imazen.Abstractions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Imageflow.Server.ExampleMinimal
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(builder => builder.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            }));
            services.ConfigureImageflowMiddleware(new ImageflowMiddlewareOptions()
                .SetMapWebRoot(true)
                .SetMyOpenSourceProjectUrl("https://github.com/imazen/imageflow-dotnet-server"));
        }
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseImageflow();
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/", async context =>
                {
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync("<img src=\"fire-umbrella-small.jpg?width=450\" />");
                });
            });
        }
    }
}
