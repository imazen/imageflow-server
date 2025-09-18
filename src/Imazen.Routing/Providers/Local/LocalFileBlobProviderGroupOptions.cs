using System.ComponentModel;
using System.Net.Http.Headers;
using System.Linq;
using Microsoft.Extensions.Options;
using Imazen.Routing.Matching.Templating;

namespace Imazen.Routing.Providers.Local
{
    public class LocalFileBlobProviderGroupOptions
    {

        /// <summary>
        /// An array of named provider configurations, each mapping a host and/or name to a physical path prefix.
        /// If missing, will default = "files" + "file:///" and only allow host paths. 
        /// </summary>
        public LocalFileProviderOptions[] Providers { get; set; } = new LocalFileProviderOptions[0];

        /// <summary>
        /// If true, will add a mapping for the content root path (e.g., "provider=wwwroot" and provider=content-root"
        /// both will point to the content root path. On .NET Framwork it's the Content subfolder in the app, on .NET Core it's wwwroot.
        /// </summary>
        public bool AddWwwRootMapping { get; set; } = true;


        /// <summary>
        /// If true, will allow any file path to be served, not just those under the content root path.
        /// </summary>
        public bool AllowAnyFilePath { get; set; } = true;

        public LocalFileBlobProviderGroupOptions WithMappings(LocalFileProviderOptions[] mappings)
        {
            var copy = new LocalFileBlobProviderGroupOptions {
                Providers = mappings,
                AddWwwRootMapping = this.AddWwwRootMapping,
                AllowAnyFilePath = this.AllowAnyFilePath,
            };
            return copy;
        }
        public ValidateOptionsResult ValidateOptions(IDefaultContentRootPathProvider defaultContentRootPathProvider)
        {
            return LocalFileBlobProviderGroupOptions.ValidateOptions(this, defaultContentRootPathProvider);
        }

        public LocalFileProviderOptions[] ResolveToFullSet(IDefaultContentRootPathProvider defaultContentRootPathProvider, out string? errorMessage)
        {
            return LocalFileBlobProviderGroupOptions.ResolveToFullSet(this, defaultContentRootPathProvider, out errorMessage);
        }

        public static LocalFileProviderOptions[] ResolveToFullSet(LocalFileBlobProviderGroupOptions options, 
        IDefaultContentRootPathProvider defaultContentRootPathProvider, out string? errorMessage)
        {
            errorMessage = null;
            var localEstimatedMs = 100;
            var remoteEstimatedMs = 1000;
            var providers = options.Providers.Select(p => {
                var copy = new LocalFileProviderOptions {
                    Name = p.Name ?? "files",
                    RequireScheme = p.RequireScheme ?? "file",
                    RequireHost = p.RequireHost ?? "",
                    RequireUriPathStartsWith = p.RequireUriPathStartsWith ?? "",
                    RequireLocalPathStartsWith = p.RequireLocalPathStartsWith ?? "",
                    RequireUriPathStartsWithIgnoreCase = p.RequireUriPathStartsWithIgnoreCase,
                    RequireLocalPathStartsWithIgnoreCase = p.RequireLocalPathStartsWithIgnoreCase,
                    PrefixPathsWith = p.PrefixPathsWith ?? "",
                };
                copy.EstimatedLatencyMs = copy.IsRequiredHostLocalhost() ? localEstimatedMs : remoteEstimatedMs;
                return copy;
            }).ToList();
            if (options.AddWwwRootMapping && !options.Providers.Any(p => p.Name == "wwwroot"))
            {
                providers.Add(new LocalFileProviderOptions {
                    Name = "wwwroot",
                    RequireScheme = "file",
                    RequireHost = "",
                    PrefixPathsWith = defaultContentRootPathProvider.DefaultContentRootPath,
                    EstimatedLatencyMs = localEstimatedMs
                });
            }
            if (options.AllowAnyFilePath && !options.Providers.Any(p => p.Name == "files"))
            {
                providers.Add(new LocalFileProviderOptions {
                    Name = "files",
                    RequireScheme = "file",
                    RequireHost = "",
                    PrefixPathsWith = "",
                    EstimatedLatencyMs = localEstimatedMs
                });
            }

            // check for duplicates using .ConflictsWith
            for (int i = 0; i < providers.Count; i++)
            {
                for (int j = i + 1; j < providers.Count; j++)
                {
                    if (providers[i].ConflictsWith(providers[j]))
                    {
                        errorMessage = $"Provider '{providers[i]}' conflicts with previously registered provider '{providers[j]}'";
                    }
                }
            }
            // Check IsPathFullyQualified for local paths
            foreach (var provider in providers)
            {
                if (provider.RequireLocalPathStartsWith != null &&
                    !LocalFileProviderOptions.IsLocalAbsoluteIsh(provider.RequireLocalPathStartsWith))
                {
                    errorMessage = $"Provider '{provider}' has a non-fully-qualified prefix path '{provider.RequireLocalPathStartsWith}'";
                }
            }
        
            return providers.ToArray();
        }


        public static ValidateOptionsResult ValidateOptions(LocalFileBlobProviderGroupOptions options, IDefaultContentRootPathProvider defaultContentRootPathProvider)
        {
            ResolveToFullSet(options, defaultContentRootPathProvider, out var errorMessage);
            if (errorMessage != null)
            {
                return ValidateOptionsResult.Fail(errorMessage);
            }
            return ValidateOptionsResult.Success;
        }

    }
}
