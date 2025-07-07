using Microsoft.Extensions.Hosting;

namespace Imazen.Routing.Serving;

/// <summary>
/// Implement this to be shut down when the image server shuts down
/// </summary>
public interface IHostedImageServerService : IHostedService
{

}
