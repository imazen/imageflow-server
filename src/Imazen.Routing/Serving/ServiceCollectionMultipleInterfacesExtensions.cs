using System.Diagnostics.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Imazen.Routing.Serving;
public static class ServiceCollectionMultipleInterfacesExtensions{
    private class ClosureBit<T,U> where T : class where U : class
    {
        U? Service { get; set; }
        internal T SetService(T service)
        {
            Service = service as U;
            if (Service == null) throw new NotSupportedException($"Service '{typeof(T).Name}' ({service.GetType().Name}) cannot be cast to '{typeof(U).Name}'");
            return service;
        }

        internal U Get(IServiceProvider container)
        {
            _ = container.GetRequiredService<T>();
            _ = container.GetServices<T>();
            return Service ?? throw new NotSupportedException();
        }
    }
    public static IServiceCollection RegisterSingletonByTwoInterfaces<T,U>(this IServiceCollection services, Func<IServiceProvider, T> factory)
             where T : class where U : class{
        var closure = new ClosureBit<T,U>();
        services.AddSingleton<T>((container) =>
        {
            return closure.SetService(factory(container));
        });
        services.AddSingleton<U>(container => closure.Get(container));
        return services;
    }
    public static IServiceCollection RegisterSingletonByThreeInterfaces<T,U,V>(this IServiceCollection services, Func<IServiceProvider, T> factory) where T : class where U : class where V : class{
        var closure = new ClosureBit<T,U>();
        var closure2 = new ClosureBit<T,V>();
        services.AddSingleton<T>((container) =>
        {
            return closure2.SetService(closure.SetService(factory(container)));
        });
        services.AddSingleton<U>(container => closure.Get(container));
        services.AddSingleton<V>(container => closure2.Get(container));
        return services;
    }
    public static IServiceCollection RegisterSingletonUnderFourInterfaces<T,U,V,W>(this IServiceCollection services, Func<IServiceProvider, T> factory) where T : class where U : class where V : class where W : class{
        var closure = new ClosureBit<T,U>();
        var closure2 = new ClosureBit<T,V>();
        var closure3 = new ClosureBit<T,W>();
        services.AddSingleton<T>((container) =>
        {
            return closure3.SetService(closure2.SetService(closure.SetService(factory(container))));
        });
        services.AddSingleton<U>(container => closure.Get(container));
        services.AddSingleton<V>(container => closure2.Get(container));
        services.AddSingleton<W>(container => closure3.Get(container));
        return services;
    }
}
