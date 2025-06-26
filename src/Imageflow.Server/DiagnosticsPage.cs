using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Imazen.Common.Extensibility.ClassicDiskCache;
using Imazen.Common.Extensibility.StreamCache;
using Imazen.Common.Issues;
using Imazen.Common.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Imageflow.Server
{
    internal class DiagnosticsPage
    {
        private readonly IWebHostEnvironment env;
        private readonly IStreamCache streamCache;
        private readonly IClassicDiskCache diskCache;
        private readonly IList<IBlobProvider> blobProviders;
        private readonly ImageflowMiddlewareOptions options;
        internal DiagnosticsPage(ImageflowMiddlewareOptions options,IWebHostEnvironment env, ILogger<ImageflowMiddleware> logger, 
            IStreamCache streamCache,
            IClassicDiskCache diskCache, IList<IBlobProvider> blobProviders)
        {
            this.options = options;
            this.env = env;
            this.streamCache = streamCache;
            this.diskCache = diskCache;
            this.blobProviders = blobProviders;
        }

        public static bool MatchesPath(string path) => "/imageflow.debug".Equals(path, StringComparison.Ordinal) ||
                                                       "/resizer.debug".Equals(path, StringComparison.Ordinal);

        private static bool IsLocalRequest(HttpContext context) =>
            context.Connection.RemoteIpAddress.Equals(context.Connection.LocalIpAddress) || 
            IPAddress.IsLoopback(context.Connection.RemoteIpAddress);

        public async Task Invoke(HttpContext context)
        {
            var providedPassword = context.Request.Query["password"].ToString();
            var passwordMatch = !string.IsNullOrEmpty(options.DiagnosticsPassword)
                                && options.DiagnosticsPassword == providedPassword;
            
            string s;
            if (passwordMatch || 
                options.DiagnosticsAccess == AccessDiagnosticsFrom.AnyHost ||
                    (options.DiagnosticsAccess == AccessDiagnosticsFrom.LocalHost && IsLocalRequest(context))){
                s = await GeneratePage(context);
                context.Response.StatusCode = 200;
            }
            else
            {
                s =
                    "You can configure access to this page via the imageflow.toml [diagnostics] section, or in C# via ImageflowMiddlewareOptions.SetDiagnosticsPageAccess(allowLocalhost, password)\r\n\r\n";
                if (options.DiagnosticsAccess == AccessDiagnosticsFrom.LocalHost)
                {
                    s += "You can access this page from the localhost\r\n\r\n";
                }
                else
                {
                    s += "Access to this page from localhost is disabled\r\n\r\n";
                }

                if (!string.IsNullOrEmpty(options.DiagnosticsPassword))
                {
                    s += "You can access this page by adding ?password=[insert password] to the URL.\r\n\r\n";
                }
                else
                {
                    s += "You can set a password via imageflow.toml [diagnostics] allow_with_password='' or in C# with SetDiagnosticsPageAccess to access this page remotely.\r\n\r\n";
                }
                context.Response.StatusCode = 401; //Unauthorized
            }

            context.Response.Headers[HeaderNames.CacheControl] = "no-store";
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers.Add("X-Robots-Tag", "none");
            var bytes = Encoding.UTF8.GetBytes(s);
            await context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
        }

        private Task<string> GeneratePage(HttpContext context)
        {
            var s = new StringBuilder(8096);
            var now = DateTime.UtcNow.ToString(NumberFormatInfo.InvariantInfo);
            s.AppendLine($"Diagnostics for Imageflow at {context?.Request.Host.Value} generated {now} UTC");
            
            try
            {
                using var job = new Bindings.JobContext();
                var version = job.GetVersionInfo();
                s.AppendLine($"libimageflow {version.LongVersionString}");
            }
            catch (Exception e)
            {
                s.AppendLine($"Failed to get libimageflow version: {e.Message}");
            }
            
            s.AppendLine("Please remember to provide this page when contacting support.");
            var issues = diskCache?.GetIssues().ToList() ?? new List<IIssue>();
            issues.AddRange(streamCache?.GetIssues() ?? new List<IIssue>());
            s.AppendLine($"{issues.Count} issues detected:\r\n");
            foreach (var i in issues.OrderBy(i => i?.Severity))
                s.AppendLine($"{i?.Source}({i?.Severity}):\t{i?.Summary}\n\t\t\t{i?.Details?.Replace("\n", "\r\n\t\t\t")}\n");

            
            s.AppendLine(options.Licensing.Result.ProvidePublicLicensesPage());

            
            s.AppendLine("\nInstalled Plugins");

            if (streamCache != null) s.AppendLine(this.streamCache.GetType().FullName);
            if (diskCache != null) s.AppendLine(this.diskCache.GetType().FullName);
            foreach (var provider in blobProviders)
            {
                s.AppendLine(provider.GetType().FullName);
            }


            s.AppendLine("\nAccepted querystring keys:\n");
            s.AppendLine(string.Join(", ", PathHelpers.SupportedQuerystringKeys));

            s.AppendLine("\nAccepted file extensions:\n");
            s.AppendLine(string.Join(", ", PathHelpers.AcceptedImageExtensions));

            s.AppendLine("\nEnvironment information:\n");
            s.AppendLine(
                $"Running on {Environment.OSVersion} and CLR {Environment.Version} and .NET Core {GetNetCoreVersion()}");
            
            try {
                var wow64 = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432");
                var arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
                s.AppendLine("OS arch: " + arch + (string.IsNullOrEmpty(wow64)
                    ? ""
                    : " !! Warning, running as 32-bit on a 64-bit OS(" + wow64 +
                      "). This will limit ram usage !!"));
            } catch (SecurityException) {
                s.AppendLine(
                    "Failed to detect operating system architecture - security restrictions prevent reading environment variables");
            }
            //Get loaded assemblies for later use
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            
            //List loaded assemblies, and also detect plugin assemblies that are not being used.
            s.AppendLine("\nLoaded assemblies:\n");
            
            foreach (var a in assemblies) {
                var assemblyName = new AssemblyName(a.FullName);
                var line = "";
                var error = a.GetExceptionForReading<AssemblyFileVersionAttribute>();
                if (error != null) {
                    line += $"{assemblyName.Name,-40} Failed to read assembly attributes: {error.Message}";
                } else {
                    var version = $"{a.GetFileVersion()} ({assemblyName.Version})";
                    var infoVersion = $"{a.GetInformationalVersion()}";
                    line += $"{assemblyName.Name,-40} File: {version,-25} Informational: {infoVersion,-30}";
                }
                
                s.AppendLine(line);
            }

            s.AppendLine(
                "\n\nWhen fetching a remote license file (if you have one), the following information is sent via the querystring.");
            foreach (var pair in options.Licensing.Result.GetReportPairs().GetInfo()) {
                s.AppendFormat("   {0,32} {1}\n", pair.Key, pair.Value);
            }

            
            s.AppendLine(options.Licensing.Result.DisplayLastFetchUrl());
            
            return Task.FromResult(s.ToString());
        }
        
        
        private static string GetNetCoreVersion()
        {
            return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        }


    }

    internal static class AssemblyExtensions
    {
        private static T GetFirstAttribute<T>(this ICustomAttributeProvider a)
        {
            try
            {
                var attrs = a.GetCustomAttributes(typeof(T), false);
                if (attrs.Length > 0) return (T)attrs[0];
            }
            catch(FileNotFoundException) {
                //Missing dependencies
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch (Exception) { }
            return default;
        }

        public static Exception GetExceptionForReading<T>(this Assembly a)
        {
            try {
                var unused = a.GetCustomAttributes(typeof(T), false);
            } catch (Exception e) {
                return e;
            }
            return null;
        }
        
        public static string GetInformationalVersion(this Assembly a)
        {
            return GetFirstAttribute<AssemblyInformationalVersionAttribute>(a)?.InformationalVersion;
        }
        public static string GetFileVersion(this Assembly a)
        {
            return GetFirstAttribute<AssemblyFileVersionAttribute>(a)?.Version;
        }
    }
}
