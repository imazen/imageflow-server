using System;
using System.Threading;
using Imazen.Routing.Health;
using Xunit;


namespace Imazen.Routing.Caching.Health.Tests
{
    public class BehaviorMetricsTests
    {
        [Fact]
        public void ReportBehavior_Success_IncrementsSuccessCounters()
        {
            var metrics = new BehaviorMetrics(MetricBasis.ProblemReports, new BehaviorTask());
            metrics.ReportBehavior(true, new BehaviorTask(), TimeSpan.FromSeconds(1));

            Assert.Equal(1, metrics.TotalSuccessReports);
            Assert.Equal(1, metrics.ConsecutiveSuccessReports);
            Assert.Equal(0, metrics.ConsecutiveFailureReports);
        }

        [Fact]
        public void ReportBehavior_Failure_IncrementsFailureCounters()
        {
            var metrics = new BehaviorMetrics(MetricBasis.ProblemReports, new BehaviorTask());
            metrics.ReportBehavior(false, new BehaviorTask(), TimeSpan.FromSeconds(1));

            Assert.Equal(1, metrics.TotalFailureReports);
            Assert.Equal(1, metrics.ConsecutiveFailureReports);
            Assert.Equal(0, metrics.ConsecutiveSuccessReports);
        }

        [Fact]
        public void ReportBehavior_Success_UpdatesUptime()
        {
            var metrics = new BehaviorMetrics(MetricBasis.ProblemReports, new BehaviorTask());
            metrics.ReportBehavior(true, new BehaviorTask(), TimeSpan.FromSeconds(1));
            Thread.Sleep(15);
            metrics.ReportBehavior(true, new BehaviorTask(), TimeSpan.FromSeconds(1));

            Assert.True(metrics.Uptime > TimeSpan.Zero);
        }

        [Fact]
        public void ReportBehavior_Failure_UpdatesDowntime()
        {
            var metrics = new BehaviorMetrics(MetricBasis.ProblemReports, new BehaviorTask());
            metrics.ReportBehavior(false, new BehaviorTask(), TimeSpan.FromSeconds(1));
            // delay 15ms
            Thread.Sleep(15);
            metrics.ReportBehavior(false, new BehaviorTask(), TimeSpan.FromSeconds(1));

            Assert.True(metrics.Downtime > TimeSpan.Zero);
        }
    }
}