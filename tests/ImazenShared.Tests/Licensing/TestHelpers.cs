using System.Diagnostics;
using System.Net;
using Moq;
using Moq.Protected;
using Imazen.Common.Issues;
using Imazen.Common.Licensing;
using Imazen.Common.Persistence;
using System.Net.Http;

namespace Imazen.Common.Tests.Licensing
{


    class FakeClock(string date, string buildDate) : ILicenseClock
    {
        private DateTimeOffset now = DateTimeOffset.Parse(date);
        private readonly DateTimeOffset built = DateTimeOffset.Parse(buildDate);

        public void AdvanceSeconds(long seconds) { now = now.AddSeconds(seconds); }
        public DateTimeOffset GetUtcNow() => now;
        public long GetTimestampTicks() => now.Ticks;
        public long TicksPerSecond { get; } = Stopwatch.Frequency;
        public DateTimeOffset? GetBuildDate() => built;
        public DateTimeOffset? GetAssemblyWriteDate() => built;
    }

    /// <summary>
    /// Time advances normally, but starting from the given date instead of now
    /// </summary>
    internal class OffsetClock(string date, string buildDate) : ILicenseClock
    {
        private TimeSpan offset = DateTimeOffset.UtcNow - DateTimeOffset.Parse(date);
        private readonly long ticksOffset = Stopwatch.GetTimestamp() - 1;
        private readonly DateTimeOffset built = DateTimeOffset.Parse(buildDate);

        public void AdvanceSeconds(int seconds) { offset += new TimeSpan(0,0, seconds); }
        public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow - offset;
        public long GetTimestampTicks() => Stopwatch.GetTimestamp() - ticksOffset;
        public long TicksPerSecond { get; } = Stopwatch.Frequency;
        public DateTimeOffset? GetBuildDate() => built;
        public DateTimeOffset? GetAssemblyWriteDate() => built;
    }

    internal class StringCacheEmpty : IPersistentStringCache
    {
        public string? Get(string key) => null;

        public DateTime? GetWriteTimeUtc(string key) => null;

        public StringCachePutResult TryPut(string key, string value) => StringCachePutResult.WriteFailed;
    }

    


    class MockConfig : ILicenseConfig
    {
        Computation? cache;
        readonly string[] codes;

        readonly LicenseManagerSingleton mgr;
        ILicenseClock Clock { get; } = new RealClock();

        // ReSharper disable once MemberInitializerValueIgnored
        private readonly IEnumerable<KeyValuePair<string, string>> domainMappings = new List<KeyValuePair<string, string>>();
        Computation Result
        {
            get {
                if (cache?.ComputationExpires != null && cache.ComputationExpires.Value < Clock.GetUtcNow()) {
                    cache = null;
                }
                return cache ??= new Computation(this, ImazenPublicKeys.All, mgr, mgr,
                    Clock, true);
            }
        }
        
        public MockConfig(LicenseManagerSingleton mgr, ILicenseClock? clock, string[] codes, IEnumerable<KeyValuePair<string, string>> domainMappings)
        {
            this.codes = codes;
            this.mgr = mgr;
            Clock = clock ?? Clock;
            
            mgr.MonitorLicenses(this);
            mgr.MonitorHeartbeat(this);

            // Ensure our cache is appropriately invalidated
            cache = null;
            mgr.AddLicenseChangeHandler(this, (me, _) => me.cache = null);

            // And repopulated, so that errors show up.
            if (Result == null) {
                throw new ApplicationException("Failed to populate license result");
            }
            this.domainMappings = domainMappings;

        }
        public IEnumerable<KeyValuePair<string, string>> GetDomainMappings()
        {
            return domainMappings;
        }
        
        public IReadOnlyCollection<IReadOnlyCollection<string>> GetFeaturesUsed()
        {
            return new[] { codes };
        }

        private readonly List<string> licenses = new List<string>();
        public IEnumerable<string> GetLicenses()
        {
            return licenses;
        }

        public LicenseAccess LicenseScope { get; } = LicenseAccess.Local;
        public LicenseErrorAction LicenseEnforcement { get; } = LicenseErrorAction.Http402;
        public string EnforcementMethodMessage { get; } = "";
        public event LicenseConfigEvent? LicensingChange;
        public event LicenseConfigEvent? Heartbeat;
        public bool IsImageflow { get; } = false;
        public bool IsImageResizer { get; } = true;
        public string LicensePurchaseUrl { get; }  = "https://imageresizing.net/licenses";
        public string AgplCompliantMessage { get; } = "";

        public void AddLicense(string license)
        {
            licenses.Add(license);
            LicensingChange?.Invoke(this, this);
            cache = null;
        }

        public string GetLicensesPage()
        {
            return Result.ProvidePublicLicensesPage();
        }
        
        public IEnumerable<IIssue> GetIssues() => mgr.GetIssues().Concat(Result.GetIssues());


        public void FireHeartbeat()
        {
            Heartbeat?.Invoke(this,this);
        }
    }

    static class MockHttpHelpers
    {
        public static Mock<HttpMessageHandler> MockRemoteLicense(LicenseManagerSingleton mgr, HttpStatusCode code, string value,
                                                   Action<HttpRequestMessage, CancellationToken>? callback)
        {
            var handler = new Mock<HttpMessageHandler>();
            var method = handler.Protected()
                                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                                    ItExpr.IsAny<CancellationToken>())
                                .Returns(Task.Run(() => new HttpResponseMessage(code)
                                {
                                    Content = new StringContent(value, System.Text.Encoding.UTF8)
                                }));

            if (callback != null)
            {
                method.Callback(callback);
            }

            method.Verifiable("SendAsync must be called");

            mgr.SetHttpMessageHandler(handler.Object, true);
            return handler;
        }

        public static Mock<HttpMessageHandler> MockRemoteLicenseException(LicenseManagerSingleton mgr, WebExceptionStatus status)
        {
            var ex = new HttpRequestException("Mock failure", new WebException("Mock failure", status));
            var handler = new Mock<HttpMessageHandler>();
            var method = handler.Protected()
                                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                                    ItExpr.IsAny<CancellationToken>())
                                .ThrowsAsync(ex);

            method.Verifiable("SendAsync must be called");

            mgr.SetHttpMessageHandler(handler.Object, true);
            return handler;
        }
    }
}
;