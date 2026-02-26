using Imazen.Caching;
using Imazen.Routing.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Imageflow.Server;

/// <summary>
/// Extension methods for registering CacheCascade as the caching engine for Imageflow.Server.
///
/// Usage:
/// <code>
/// services.AddImageflowCacheCascade(cascade =>
/// {
///     cascade.Config = new CascadeConfig
///     {
///         Providers = ["memory", "disk", "s3-cache"]
///     };
///     // Register providers after building
/// });
/// </code>
///
/// Or with a pre-built ICacheEngine:
/// <code>
/// services.AddSingleton&lt;ICacheEngine&gt;(myCacheCascade);
/// </code>
///
/// When ICacheEngine is registered, ImageServer automatically uses CascadeCachePipeline
/// instead of the legacy CacheEngine for the caching layer.
/// </summary>
public static class CacheCascadeExtensions
{
    /// <summary>
    /// Register a CacheCascade as the ICacheEngine for Imageflow.Server.
    /// The configure callback receives a CacheCascadeBuilder to set up config and providers.
    /// </summary>
    public static IServiceCollection AddImageflowCacheCascade(
        this IServiceCollection services,
        Action<CacheCascadeBuilder> configure)
    {
        var builder = new CacheCascadeBuilder();
        configure(builder);

        services.TryAddSingleton<ICacheEngine>(sp =>
        {
            var cascade = new CacheCascade(builder.Config);
            foreach (var registration in builder.ProviderRegistrations)
            {
                registration(cascade, sp);
            }
            return cascade;
        });

        return services;
    }

    /// <summary>
    /// Register a legacy IStreamCache adapter backed by ICacheEngine.
    /// Only needed if you have code that depends on the old IStreamCache interface.
    /// </summary>
    public static IServiceCollection AddCascadeStreamCacheAdapter(this IServiceCollection services)
    {
        services.TryAddSingleton<Imazen.Common.Extensibility.StreamCache.IStreamCache>(sp =>
        {
            var engine = sp.GetRequiredService<ICacheEngine>();
            return new CascadeStreamCacheAdapter(engine);
        });
        return services;
    }
}

/// <summary>
/// Builder for configuring CacheCascade registration.
/// </summary>
public class CacheCascadeBuilder
{
    /// <summary>
    /// The cascade configuration. Must be set before building.
    /// </summary>
    public CascadeConfig Config { get; set; } = new CascadeConfig
    {
        Providers = new List<string>()
    };

    internal List<Action<CacheCascade, IServiceProvider>> ProviderRegistrations { get; } = new();

    /// <summary>
    /// Register a provider by resolving it from DI.
    /// </summary>
    public CacheCascadeBuilder AddProvider<TProvider>() where TProvider : ICacheProvider
    {
        ProviderRegistrations.Add((cascade, sp) =>
        {
            var provider = sp.GetRequiredService<TProvider>();
            cascade.RegisterProvider(provider);
        });
        return this;
    }

    /// <summary>
    /// Register a provider using a factory.
    /// </summary>
    public CacheCascadeBuilder AddProvider(Func<IServiceProvider, ICacheProvider> factory)
    {
        ProviderRegistrations.Add((cascade, sp) =>
        {
            cascade.RegisterProvider(factory(sp));
        });
        return this;
    }

    /// <summary>
    /// Register a provider instance directly.
    /// </summary>
    public CacheCascadeBuilder AddProvider(ICacheProvider provider)
    {
        ProviderRegistrations.Add((cascade, _) =>
        {
            cascade.RegisterProvider(provider);
        });
        return this;
    }
}
