using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using Imazen.Common.ExtensionMethods;
using Imazen.Common.Licensing;
using Imazen.Common.Persistence;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Imazen.Common.Tests.Licensing
{
    public class LicenseManagerTests
    {
        public LicenseManagerTests(ITestOutputHelper output) { this.output = output; }

        readonly ITestOutputHelper output;



        [Fact]
        public void Test_Caching_With_Timeout()
        {
            if (Environment.GetEnvironmentVariable("APPVEYOR") == "True") {
                return;
            }

            var clock = new OffsetClock("2017-04-25", "2017-04-25");
            var cache = new StringCacheMem();

            // Populate cache
            {
                var mgr = new LicenseManagerSingleton(ImazenPublicKeys.Test, clock, cache);
                MockHttpHelpers.MockRemoteLicense(mgr, HttpStatusCode.OK, LicenseStrings.EliteSubscriptionRemote,
                    null);

                var conf = new MockConfig(mgr, clock, new []{"R_Elite", "R4Elite"}, new List<KeyValuePair<string, string>>());
                
                conf.AddLicense(LicenseStrings.EliteSubscriptionPlaceholder);
                
                mgr.WaitForTasks();

                var result = new Computation(conf, ImazenPublicKeys.Test, mgr, mgr, clock, true);
                Assert.True(result.LicensedForRequestUrl(new Uri("http://anydomain")));

                Assert.Empty(mgr.GetIssues());
                Assert.NotNull(conf.GetLicensesPage());
            }

            // Use cache
            {
                var mgr = new LicenseManagerSingleton(ImazenPublicKeys.Test, clock, cache)
                {
                    SkipHeartbeatsIfDiskCacheIsFresh = 0
                };
                MockHttpHelpers.MockRemoteLicenseException(mgr, WebExceptionStatus.NameResolutionFailure);

                var conf = new MockConfig(mgr, clock, new []{"R_Elite", "R4Elite"}, new List<KeyValuePair<string, string>>());
                try {
                    
                    conf.AddLicense(LicenseStrings.EliteSubscriptionPlaceholder);

                    conf.FireHeartbeat();
                    mgr.WaitForTasks();

                    var result = new Computation(conf, ImazenPublicKeys.Test, mgr, mgr, clock, true);
                    Assert.True(result.LicensedForRequestUrl(new Uri("http://anydomain")));


                    Assert.NotNull(conf.GetLicensesPage());

                    Assert.Single(mgr.GetIssues());
                } catch {
                    output.WriteLine(conf.GetLicensesPage());
                    throw;
                }
            }
        }


        [Fact]
        public void Test_Caching_With_Write_Delay()
        {
            if (Environment.GetEnvironmentVariable("APPVEYOR") == "True")
            {
                return;
            }

            var clock = new OffsetClock("2017-04-25", "2017-04-25");
            var cache = new StringCacheMem();

            // Populate cache
            {
                var mgr = new LicenseManagerSingleton(ImazenPublicKeys.Test, clock, cache);
                MockHttpHelpers.MockRemoteLicense(mgr, HttpStatusCode.OK, LicenseStrings.EliteSubscriptionRemote,
                    null);

                var conf = new MockConfig(mgr, clock, new []{"R_Elite", "R4Elite"}, Enumerable.Empty<KeyValuePair<string, string>>());
                conf.AddLicense(LicenseStrings.EliteSubscriptionPlaceholder);

                Assert.Equal(1, mgr.WaitForTasks());

                var result = new Computation(conf, ImazenPublicKeys.Test, mgr, mgr, clock, true);
                Assert.True(result.LicensedForRequestUrl(new Uri("http://anydomain")));

                Assert.Empty(mgr.GetIssues());
                Assert.NotNull(conf.GetLicensesPage());
            }

            // Use cache
            {
                var mgr = new LicenseManagerSingleton(ImazenPublicKeys.Test, clock, cache);

                var conf = new MockConfig(mgr, clock, new []{"R_Elite", "R4Elite"}, new List<KeyValuePair<string, string>>());
                
                conf.AddLicense(LicenseStrings.EliteSubscriptionPlaceholder);

                conf.FireHeartbeat();
                Assert.Equal(0, mgr.WaitForTasks());

                var result = new Computation(conf, ImazenPublicKeys.Test, mgr, mgr, clock, true);
                Assert.True(result.LicensedForRequestUrl(new Uri("http://anydomain")));


                Assert.NotNull(conf.GetLicensesPage());

                Assert.Empty(mgr.GetIssues());

                MockHttpHelpers.MockRemoteLicenseException(mgr, WebExceptionStatus.NameResolutionFailure);

                while (!mgr.AllowFetching())
                {
                    conf.FireHeartbeat();
                }

                mgr.WaitForTasks();

                result = new Computation(conf, ImazenPublicKeys.Test, mgr, mgr, clock, true);
                Assert.True(result.LicensedForRequestUrl(new Uri("http://anydomain")));
                Assert.Single(mgr.GetIssues());


            }
        }


        [Fact]
        public void Test_GlobalCache()
        {
            // We don't want to test the singleton

            var uniquePrefix = "test_cache_" + Guid.NewGuid() + "__";

            var cacheInstance = new WriteThroughCache(uniquePrefix, new[] { Path.GetTempPath() });

            var c = new PersistentGlobalStringCache(uniquePrefix, new[] { Path.GetTempPath() });
            var cacheField = typeof(PersistentGlobalStringCache)
                .GetField("cache", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(cacheField);
            cacheField.SetValue(c, cacheInstance);

            try
            {
                Assert.Equal(StringCachePutResult.WriteComplete, c.TryPut("a", "b"));
                Assert.Equal(StringCachePutResult.Duplicate, c.TryPut("a", "b"));
                Assert.Equal("b", c.Get("a"));
                Assert.Null(c.Get("404"));
                Assert.Equal(StringCachePutResult.WriteComplete, c.TryPut("a", null));

            }
            catch
            {
                output.WriteLine(c.GetIssues().Delimited("\r\n"));
                throw;
            }
        }


        [Fact]
        public void Test_Offline_License_Failure()
        {
            var clock = new OffsetClock("2017-04-25", "2017-04-25");
            var mgr = new LicenseManagerSingleton(ImazenPublicKeys.Test, clock, new StringCacheMem());
            
            var conf = new MockConfig(mgr, clock, new []{"R4Creative"}, new List<KeyValuePair<string, string>>());

            
            Assert.Empty(mgr.GetIssues());
            Assert.Null(mgr.GetAllLicenses().FirstOrDefault());

            var result = new Computation(conf, ImazenPublicKeys.Test, mgr, mgr, clock, true);

            Assert.False(result.LicensedForRequestUrl(new Uri("http://acme.com")));
            conf.AddLicense(LicenseStrings.OfflineV4DomainAcmeComCreative);

            Assert.NotNull(mgr.GetAllLicenses().First());

            result = new Computation(conf, ImazenPublicKeys.Test, mgr, mgr, clock, true);
            Assert.True(result.LicensedForRequestUrl(new Uri("http://acme.com")));

            Assert.Empty(mgr.GetIssues());
            Assert.NotNull(conf.GetLicensesPage());
        }


        [Fact]
        public void Test_Offline_License_Success()
        {
            var clock = new OffsetClock("2017-04-25", "2017-04-25");
            var mgr = new LicenseManagerSingleton(ImazenPublicKeys.Test, clock, new StringCacheMem());
            
            var conf = new MockConfig(mgr, clock, new []{"R4Creative"}, new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("localhost", "acme.com")
            });
            conf.AddLicense(LicenseStrings.OfflineV4DomainAcmeComCreative);
            
            Assert.Equal(0, mgr.WaitForTasks());
            Assert.Empty(mgr.GetIssues());

            Assert.Single(mgr.GetAllLicenses());
            
            
            Assert.NotNull(mgr.GetAllLicenses().First());

            var result = new Computation(conf, ImazenPublicKeys.Test, mgr, mgr, clock, true);

            Assert.True(result.LicensedForRequestUrl(new Uri("http://acme.com")));
            Assert.True(result.LicensedForRequestUrl(new Uri("http://subdomain.acme.com")));
            Assert.True(result.LicensedForRequestUrl(new Uri("http://localhost")));
            Assert.False(result.LicensedForRequestUrl(new Uri("http://other.com")));
            Assert.Equal(0, mgr.WaitForTasks());
            Assert.Empty(mgr.GetIssues());
            Assert.NotNull(conf.GetLicensesPage());
        }


        [Fact]
        public void Test_Remote_License_Success()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))) {
                return;
            }
            var clock = new OffsetClock("2017-04-25", "2017-04-25");
            var mgr = new LicenseManagerSingleton(ImazenPublicKeys.Test, clock, new StringCacheMem());
            Uri invokedUri = null;
            var httpHandler = MockHttpHelpers.MockRemoteLicense(mgr, HttpStatusCode.OK, LicenseStrings.EliteSubscriptionRemote,
                (r, c) => { invokedUri = r.RequestUri; });
            var conf = new MockConfig(mgr, clock, new []{"R_Elite"}, new List<KeyValuePair<string, string>>());
            try {
                
                conf.AddLicense(LicenseStrings.EliteSubscriptionPlaceholder);

                Assert.Single(mgr.GetAllLicenses());
                Assert.True(mgr.GetAllLicenses().First().IsRemote);
                conf.FireHeartbeat();

                mgr.WaitForTasks();
                Assert.Empty(mgr.GetIssues());

                Mock.Verify(httpHandler);
                Assert.StartsWith(
                    "https://s3.us-west-2.amazonaws.com/licenses.imazen.net/v1/licenses/latest/",
                    invokedUri.ToString());


                Assert.NotNull(mgr.GetAllLicenses().First().FetchedLicense());

                var result = new Computation(conf, ImazenPublicKeys.Test, mgr, mgr, clock, true);

                Assert.True(result.LicensedForRequestUrl(new Uri("http://anydomain")));
                //TODO: Explore why WaitForTasks() can sometimes return 1
                mgr.WaitForTasks();
                Assert.Empty(mgr.GetIssues());
                Assert.NotNull(conf.GetLicensesPage());
            } catch {
                output.WriteLine(conf.GetLicensesPage());
                throw;
            }
        }
        //        Cache = new StringCacheEmpty()
        //    {
        //    var mgr = new LicenseManagerSingleton()
        //{
        //public void Test_Uncached_403()


        //[Fact]
        // test mixed

        // Test invalid content

        //Test network grace period
        //    };

        //    var httpHandler = MockRemoteLicense(mgr, HttpStatusCode.Forbidden, "", null);

        //    Config conf = new MockConfig();
        //    
        //    conf.Plugins.Install(new LicensedPlugin(mgr, "R4Elite"));
        //    conf.Plugins.AddLicense(LicenseStrings.EliteSubscriptionPlaceholder);

        //    var tasks = mgr.GetAsyncTasksSnapshot().ToArray();
        //    Assert.Equal(1, tasks.Count());
        //    Task.WaitAll(tasks);

        //    conf.FireHeartbeat();
        //    Mock.Verify(httpHandler);

        //    var sink = new IssueSink("LicenseManagerTest");
        //    var result = new Computation(conf, PublicKeys.Test, sink, mgr);


        //    //Assert.NotNull(mgr.GetAllLicenses().First().GetFreshRemoteLicense());
        //    Assert.True(result.LicensedForHost("any"));

        //    tasks = mgr.GetAsyncTasksSnapshot().ToArray();
        //    Assert.Equal(0, tasks.Count());
        //    Task.WaitAll(tasks);

        //}
        // Test with cache states - none, 404, valid, expired, and invalid
        // Test with timeout, 403/404, valid, and invalid response


        //var cacheMock = new Mock<IPersistentStringCache>();
        //cacheMock.Setup((c) => c.Get(It.IsAny<string>())).Returns("404").Verifiable("Cache.Get must be called");
        //cacheMock.Setup((c) => c.TryPut(It.IsAny<string>(), It.IsAny<string>())).Returns(StringCachePutResult.WriteFailed).Verifiable("Cache.TryPut must be called");

        //mgr.Cache = cacheMock.Object;
    }
}
