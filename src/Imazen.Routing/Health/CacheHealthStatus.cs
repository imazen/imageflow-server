using System.Diagnostics;
using Imazen.Abstractions.BlobCache;
using Imazen.Abstractions.Logging;
using Imazen.Routing.Caching.Health;
using Imazen.Routing.Helpers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Imazen.Routing.Health;

internal class CacheHealthStatus : IHostedService, IDisposable
{
    // We want to track the history of health checks so we can plan for recovery
    // We want to track the current health status so we can make decisions about routing
    // If we have failures happening a lot, we should trigger a health check even if it's in good health.
    private IBlobCache Cache { get; }
    public bool SupportsHealthCheck { get; init; }
    private IBlobCacheHealthDetails? CurrentHealthDetails { get; set; }
    
    private CacheHealthMetrics Metrics { get; init;  }
    private static TimeSpan Timeout => TimeSpan.FromMinutes(3);

    // TODO: NonOverlappingAsyncRunner is unreliable, remove or fix.
    private readonly INonOverlappingRunner<IBlobCacheHealthDetails>? healthCheckTask;
    
    private IReLogger Logger { get;  }

    /// <summary>
    /// Determine if the result (whether a lastCheckException or a CurrentHealthDetails or a null CurrentHealthDetails) is stale.
    /// </summary>
    /// <returns></returns>
    private bool IsStale()
    {
        return Metrics.IsStale();
    }
    
    /// <summary>
    /// Triggers a health check. This could eventually be expanded to specify the category of problem.(fetch/put/search/etc)
    /// </summary>
    public void ReportBehavior(bool successful, BehaviorTask task, TimeSpan duration = default)
    {
        Metrics.ReportBehavior(successful, task, duration);
        if (IsStale()) healthCheckTask?.FireAndForget();
    }
    
    public CacheHealthStatus(IBlobCache cache, IReLogger logger)
    {
        Cache = cache;
        Logger = logger.WithSubcategory($"CacheHealth({cache.UniqueName})");
        SupportsHealthCheck = cache.InitialCacheCapabilities.SupportsHealthCheck;
        Metrics = new CacheHealthMetrics(Cache.InitialCacheCapabilities);
        if (!SupportsHealthCheck) return;
        healthCheckTask = new NonOverlappingCachedRunner<IBlobCacheHealthDetails>(
            async (ct) =>
            {
                
                var sw = Stopwatch.StartNew();
                try
                {
                    var result = await Cache.CacheHealthCheck(ct);
                    sw.Stop();
                    var priorHealth = CurrentHealthDetails;
                    CurrentHealthDetails = result;
                    Metrics.ReportHealthCheck(result, priorHealth, sw.Elapsed);
                    if (result.AtFullHealth)
                    {
                        if (priorHealth is { AtFullHealth: false })
                        {
                            Logger.WithRetain.LogInformation("Cache {CacheName} is back at full health: {HealthDetails}",
                                Cache.UniqueName, result.GetReport());
                        }
                    }
                    else
                    {
                        if (priorHealth is { AtFullHealth: true })
                        {
                            Logger.WithRetain.LogError("Cache {CacheName} went from full health down to: {HealthDetails}",
                                Cache.UniqueName, result.GetReport());
                        }
                        if (Metrics.ConsecutiveUnhealthyChecks > 0)
                        {
                            Logger.WithRetain.LogWarning(
                                "Cache {CacheName} is still not at full health after 2 checks: {HealthDetails}",
                                Cache.UniqueName, result.GetReport());
                        }
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Metrics.ReportFailedHealthCheck(ex, CurrentHealthDetails, sw.Elapsed);
                    Logger.WithRetain.LogError(ex, "Error checking health of cache {CacheName} (Failure #{CrashCount})",
                        Cache.UniqueName, Metrics.ConsecutiveCheckCrashes);
                    throw;
                }

            }, Timeout, TimeSpan.FromSeconds(5));
    }

    

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return healthCheckTask?.StartAsync(cancellationToken) ?? Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return healthCheckTask?.StopAsync(cancellationToken) ?? Task.CompletedTask;
    }
    
    public BlobCacheCapabilities GetEstimatedCapabilities()
    {
        if (IsStale()) healthCheckTask?.FireAndForget();
        return Metrics.EstimateCapabilities();
    }

    /// <summary>
    /// Checks the health of the blob cache. If the check crashed, the exception is rethrown (even if cached)
    /// </summary>
    /// <param name="forceFresh">True to force a fresh health check, false to used cached results.</param>
    /// <param name="timeout">The timeout for us to wait (underlying check isn't canceled)</param>
    /// <param name="cancellationToken">The cancellation token to cancel our waiting.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the blob cache health details, or null if the cache does not support health check or is cancelled.</returns>
    public ValueTask<IBlobCacheHealthDetails?> CheckHealth(bool forceFresh, TimeSpan timeout = default, CancellationToken cancellationToken = default)
    {
        if (!Cache.InitialCacheCapabilities.SupportsHealthCheck)
        {
            return Tasks.ValueResult<IBlobCacheHealthDetails?>(null);
        }
        if (forceFresh || IsStale()) return healthCheckTask!.RunAsync(timeout, cancellationToken)!;
        
        if (Metrics.LastHealthCheckException != null)
        {
            throw Metrics.LastHealthCheckException;
        }
        return new ValueTask<IBlobCacheHealthDetails?>(CurrentHealthDetails);
    }
    

    public async ValueTask<string> GetReport(bool forceFresh, CancellationToken cancellationToken)
    {
        // TODO: make better
        var health = await CheckHealth(forceFresh, default, cancellationToken);
        if (health == null)
        {
            return !Cache.InitialCacheCapabilities.SupportsHealthCheck ? 
                $"Cache '{Cache.UniqueName}' ({Cache.GetType().FullName}) does not support health checks." 
                : $"!!! ERROR Cache '{Cache.UniqueName}' ({Cache.GetType().FullName}) is not responding to health checks";
        }
        var header = $"Cache '{Cache.UniqueName}' ({Cache.GetType().FullName})";
        // Wrap header with !!! instead of === when we have a problem
        if (health.AtFullHealth)
        {
            header = $"====== OK: {header} is at full health ======";
        }
        else
        {
            header = $"!!!!!! ERROR: {header} has problems !!!!!!";
        }
        return $"\n{header}\n{health.GetReport()}\n\n";
    }

    public void Dispose()
    {
        healthCheckTask?.Dispose();
    }
}