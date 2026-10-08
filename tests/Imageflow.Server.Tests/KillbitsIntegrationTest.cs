using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Imageflow.Fluent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Imageflow.Server.Tests
{
    /// <summary>
    /// Integration tests for the three-layer killbits surface. These spin up
    /// the full middleware and run actual image jobs.
    /// </summary>
    /// <remarks>
    /// These tests require a native runtime that implements
    /// <c>v1/context/set_policy</c> + <c>v1/context/get_net_support</c>
    /// (imageflow PR #720 and later). On older builds the server's startup
    /// call to <see cref="Imageflow.Bindings.JobContext.SetPolicy"/> throws
    /// with <c>InvalidMessageEndpoint</c>, so these tests are gated behind
    /// the environment variable
    /// <c>IMAGEFLOW_TESTS_KILLBITS_NATIVE=1</c>. The caller (CI workflow /
    /// justfile) decides when to set it — the test body never silently
    /// skips itself (per imazen global test policy).
    /// </remarks>
    [Trait("Category", "Killbits")]
    [Trait("RequiresNativeCapability", "killbits")]
    public class KillbitsIntegrationTest
    {
        private static bool NativeAvailable()
        {
            var flag = Environment.GetEnvironmentVariable("IMAGEFLOW_TESTS_KILLBITS_NATIVE");
            return flag == "1" || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
        }

        [SkippableFact]
        public async Task Default_AvifEncodeRequest_Returns422()
        {
            SkipIfNoKillbitsNative();
            using var contentRoot = new TempContentRoot()
                .AddResource("images/fire.jpg", "TestFiles.fire-umbrella-small.jpg");

            var hostBuilder = new HostBuilder().ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.Configure(app =>
                {
                    app.UseImageflow(new ImageflowMiddlewareOptions()
                        .SetMapWebRoot(false)
                        .MapPath("/", Path.Combine(contentRoot.PhysicalPath, "images"))
                        .SetSecurityPolicyOptions(new SecurityPolicyOptions())); // server defaults → AVIF denied
                });
            });

            using var host = await hostBuilder.StartAsync();
            using var client = host.GetTestClient();

            using var resp = await client.GetAsync("/fire.jpg?format=avif");
            Assert.Equal((HttpStatusCode)422, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            using var parsed = JsonDocument.Parse(body);
            Assert.True(parsed.RootElement.TryGetProperty("error", out var err));
            Assert.Contains("not_available", err.GetString());
        }

        [SkippableFact]
        public async Task OptInAvif_AvifEncodeRequest_Returns200()
        {
            SkipIfNoKillbitsNative();
            using var contentRoot = new TempContentRoot()
                .AddResource("images/fire.jpg", "TestFiles.fire-umbrella-small.jpg");

            var hostBuilder = new HostBuilder().ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.Configure(app =>
                {
                    app.UseImageflow(new ImageflowMiddlewareOptions()
                        .SetMapWebRoot(false)
                        .MapPath("/", Path.Combine(contentRoot.PhysicalPath, "images"))
                        .SetSecurityPolicyOptions(new SecurityPolicyOptions
                        {
                            Formats = new FormatOptions
                            {
                                OptInEncode = new List<ImageFormat> { ImageFormat.Avif },
                            },
                        }));
                });
            });

            using var host = await hostBuilder.StartAsync();
            using var client = host.GetTestClient();

            using var resp = await client.GetAsync("/fire.jpg?format=avif");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [SkippableFact]
        public async Task InvalidPolicy_StartupFailsLoudly()
        {
            SkipIfNoKillbitsNative();
            using var contentRoot = new TempContentRoot()
                .AddResource("images/fire.jpg", "TestFiles.fire-umbrella-small.jpg");

            // Mutually-exclusive allow + deny should fail at validation time
            // (Validate() is called before the native SetPolicy round-trip, so
            // this test works even when the native lacks killbits endpoints —
            // but we still gate it behind the capability flag because a fully
            // configured server path makes the test meaningful for regression).
            var hostBuilder = new HostBuilder().ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.Configure(app =>
                {
                    app.UseImageflow(new ImageflowMiddlewareOptions()
                        .SetMapWebRoot(false)
                        .MapPath("/", Path.Combine(contentRoot.PhysicalPath, "images"))
                        .SetSecurityPolicyOptions(new SecurityPolicyOptions
                        {
                            Formats = new FormatOptions
                            {
                                AllowEncode = new List<ImageFormat> { ImageFormat.Jpeg },
                                DenyEncode = new List<ImageFormat> { ImageFormat.Webp },
                            },
                        }));
                });
            });

            await Assert.ThrowsAsync<SecurityPolicyValidationException>(async () =>
            {
                using var host = await hostBuilder.StartAsync();
                // The exception is raised inside the middleware constructor,
                // which only runs on the first request. Issue a request to
                // surface it.
                using var client = host.GetTestClient();
                using var _ = await client.GetAsync("/fire.jpg");
            });
        }

        private static void SkipIfNoKillbitsNative()
        {
            Skip.IfNot(NativeAvailable(),
                "IMAGEFLOW_TESTS_KILLBITS_NATIVE is not set; native runtime "
                + "does not implement v1/context/set_policy. Enable this flag "
                + "in CI once an imageflow runtime with PR #720 ships.");
        }
    }
}
