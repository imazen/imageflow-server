using Imageflow.Fluent;
using Imageflow.Server;
using Imageflow.Server.ExampleModernAPI;
using Imazen.Abstractions.Logging;
using Imazen.Routing.Layers;
using PathMapping = Imazen.Routing.Layers.PathMapping;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
#pragma warning disable CS0618
builder.Services.ConfigureImageflowMiddleware(new ImageflowMiddlewareOptions()
    .MapPath("/images", Path.Join(builder.Environment.WebRootPath, "images"))
    .SetMyOpenSourceProjectUrl("https://github.com/imazen/imageflow-dotnet-server")
    
    .AddRoutingConfiguration((routing) =>
    {
        routing.ConfigureEndpoints((endpoints) =>
        {
            endpoints.AddLayer(new CustomMediaLayer(new PathMapper(new[]
            {
                new PathMapping("/img/", Path.Join(builder.Environment.ContentRootPath, "json"), true)
            })));
        });
    }));
#pragma warning restore CS0618
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseImageflow();


var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast")
    .WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

