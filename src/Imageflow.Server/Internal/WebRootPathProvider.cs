using Microsoft.AspNetCore.Hosting;
using Imazen.Routing.HttpAbstractions;

namespace Imageflow.Server.Internal;

public class WebRootPathProvider(IWebHostEnvironment env) : IDefaultContentRootPathProvider
{
    public string DefaultContentRootPath => env.ContentRootPath;
}
