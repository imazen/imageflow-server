using Imazen.Abstractions.Blobs;

namespace Imazen.Routing.Providers.Local
{
    public class LocalFileProviderOptions
    {
        /// <summary>
        /// The unique name for this provider instance, used for matching against 'provider' specified in the URI or routing rules.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Case-sensitive. The URI scheme that this mapping applies to. We define content-file where the path is resolved against the app content root
        /// content-file:///path. 
        /// </summary> 
        public string? RequireScheme { get; set; }

        /// <summary>
        /// Case-insensitive. The host that this mapping applies to. Can be a specific hostname (e.g., "shared-drive") or "*" to match any host.
        /// For file:///c:/path URIs, the host is empty. Use "" to match these.
        /// </summary>
        public string? RequireHost { get; set; }

        /// <summary>
        /// All paths must start with this string. Case sensitive unless RequirePathStartsWithIgnoreCase is true.
        /// </summary>
        public string? RequireUriPathStartsWith { get; set; }

        /// <summary>
        /// All paths must start with this string. Case sensitive unless RequirePathStartsWithIgnoreCase is true.
        /// </summary>
        public string? RequireLocalPathStartsWith { get; set; }

        /// <summary>
        /// If true, the path prefix check will be case-insensitive.
        /// </summary>
        public bool RequireUriPathStartsWithIgnoreCase { get; set; } = false;

        /// <summary>
        /// If true, the path prefix check will be case-insensitive.
        /// </summary>
        public bool RequireLocalPathStartsWithIgnoreCase { get; set; } = false;

        /// <summary>
        /// All paths will be prefixed with this string. Enables provider=wwwroot for example. 
        /// </summary>
        public string? PrefixPathsWith { get;  set; }

        /// <summary>
        /// If true, will force the source file to be cached. 
        /// </summary>
        public bool? ForceSourceFileCaching { get; set; }

        /// <summary>
        /// The estimated latency of this provider in milliseconds.
        /// </summary>
        public int? EstimatedLatencyMs { get; set; }

        public string RequiredSchemeOrDefault() => this.RequireScheme ?? "file";
        public bool IsRequiredHostLocalhost() => IsLocalhost(this.RequireHost);
    
        private static bool IsLocalhost(string? host)
        {
            return host == null || host == "" || host == "localhost" || host == "127.0.0.1" || host == "::1";
        }
        
        public bool MatchesScheme(string? scheme)
        {
            return this.RequireScheme == null || String.Equals(this.RequireScheme, scheme, StringComparison.Ordinal);
        }
        public bool MatchesName(string name)
        {
            return String.Equals(this.Name, name, StringComparison.OrdinalIgnoreCase);
        }

        public bool MatchesUriPathPrefix(string path)
        {
            if (this.RequireUriPathStartsWith == null) return true;
            return path.StartsWith(this.RequireUriPathStartsWith, this.RequireUriPathStartsWithIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        /// <summary>
        /// On .NET Standard 2.1 or greater, this checks if the path is fully qualified (e.g., "c:/path").
        /// On .Net 2.0, this checks if the path is rooted (e.g., "c:/path" or "/path").
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        internal static bool IsLocalAbsoluteIsh(string path)
        {
#if NETSTANDARD2_1_OR_GREATER
            return Path.IsPathFullyQualified(path);
#else
            return Path.IsPathRooted(path);
#endif
        }

        public bool MatchesLocalPathPrefix(string path)
        {
            if (RequireLocalPathStartsWith == null) return true;

            if (!IsLocalAbsoluteIsh(path)) return false;
            if (!IsLocalAbsoluteIsh(RequireLocalPathStartsWith)) return false;

            return path.StartsWith(this.RequireLocalPathStartsWith, this.RequireLocalPathStartsWithIgnoreCase ?
             StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        public bool MatchesHost(string? host)
        {
            if (this.RequireHost == "*") return true;
            if (String.Equals(this.RequireHost, host, StringComparison.OrdinalIgnoreCase)) return true;
            if (IsLocalhost(host)) return this.IsRequiredHostLocalhost();
            if (RequireHost != null && host != null && RequireHost.StartsWith("*")) return host.EndsWith(RequireHost.Substring(1), StringComparison.OrdinalIgnoreCase);
            return false;
        }
        public bool ConflictsWith(LocalFileProviderOptions other)
        {
            var hostMatches = this.MatchesHost(other.RequireHost) || other.MatchesHost(this.RequireHost);
            var schemeMatches = (this.RequireScheme ?? "file") == (other.RequireScheme ?? "file");
            return hostMatches && schemeMatches && String.Equals(this.Name, other.Name, StringComparison.OrdinalIgnoreCase);
        }

        public string? GetPhysicalPath(Uri uri)
        {
            var localPath = uri.LocalPath;
            var pathSegment = localPath;
            if (RequireLocalPathStartsWith != null)
            {
                if (!pathSegment.StartsWith(RequireLocalPathStartsWith, RequireLocalPathStartsWithIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    return null; // Should not happen if MatchesPathPrefix was called first
                }
                pathSegment = pathSegment.Substring(RequireLocalPathStartsWith.Length);
            }

            if (PrefixPathsWith == null) return null;
            
            // Combine the base path with the remaining path segment
            var combined = Path.Combine(PrefixPathsWith, pathSegment.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            // Normalize the path to prevent directory traversal issues
            var normalized = Path.GetFullPath(combined);

            // Security check: ensure the normalized path is still within the intended directory
            if (!normalized.StartsWith(Path.GetFullPath(PrefixPathsWith), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return normalized;
        }
        public override string ToString()
        {   
            //TODO;
            var schemeAndHost =$"{this.RequireScheme}://{this.RequireHost}";
            return $"{schemeAndHost}{(this.RequireUriPathStartsWith ?? "/")} => {schemeAndHost}{this.PrefixPathsWith}" 
            + this.EstimatedLatencyMs != null ? $" (EstimatedLatencyMs={this.EstimatedLatencyMs})" : ""
            + this.ForceSourceFileCaching != null ? $" (ForceSourceFileCaching={this.ForceSourceFileCaching})" : "";
        }
    }
}
