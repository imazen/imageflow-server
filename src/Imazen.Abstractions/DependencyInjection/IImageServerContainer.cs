using Microsoft.Extensions.DependencyInjection;

namespace Imazen.Abstractions.DependencyInjection;

public interface IImageServerContainer : IServiceProvider
{
    void Register<TService>(Func<TService> instanceCreator) where TService : class;
    void Register<TService>(TService instance) where TService : class;
    IEnumerable<TService> Resolve<TService>();

    IServiceProvider? GetOuterProvider();

    IEnumerable<T> GetInstanceOfEverythingLocal<T>();
    bool Contains<T>();
}

// TODO: Make this AOT friendly
public class ImageServerContainer(IServiceProvider? outerProvider) : IImageServerContainer
{
    private readonly Dictionary<Type, List<object>> services = new();

    public IServiceProvider? GetOuterProvider()
    {
        return outerProvider;
    }

    public IEnumerable<T> GetInstanceOfEverythingLocal<T>()
    {
        return services.SelectMany(kvp => kvp.Value.Where(v => v is T).Cast<T>());
    }

    public bool Contains<T>()
    {
        return services.ContainsKey(typeof(T));
    }

    public void Register<TService>(Func<TService> instanceCreator) where TService : class
    {
        var instance = instanceCreator();
        if (instance == null) throw new ArgumentNullException(nameof(instanceCreator));
        if (services.ContainsKey(typeof(TService)))
        {
            services[typeof(TService)].Add(instance);
        }
        else
        {
            services[typeof(TService)] = [instance];
        }
    }
    public void RegisterAll<TService>(IEnumerable<TService> instances)
    {
        if (services.ContainsKey(typeof(TService)))
        {
            services[typeof(TService)].AddRange(instances.Cast<object>());
        }
        else
        {
            services[typeof(TService)] = instances.Cast<object>().ToList();
        }
    }
        
    public void Register<TService>(TService instance) where TService : class
    {
        if (instance == null) throw new ArgumentNullException(nameof(instance));
        if (services.ContainsKey(typeof(TService)))
        {
            services[typeof(TService)].Add(instance);
        }
        else
        {
            services[typeof(TService)] = [instance];
        }
    }

    public IEnumerable<TService> Resolve<TService>()
    {
        return services[typeof(TService)].Cast<TService>();
    }



    public object? GetService(Type serviceType)
    {
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var actualServiceType = serviceType.GetGenericArguments()[0];
            if (services.TryGetValue(actualServiceType, out var instances))
            {
                var castMethod = typeof(Enumerable).GetMethod("Cast");
                //TODO: This won't work for AOT
                var method = castMethod!.MakeGenericMethod(actualServiceType);
                return method.Invoke(null, new object[] { instances });
            }
        }
        else if (services.TryGetValue(serviceType, out var instances))
        {
            return instances.FirstOrDefault();
        }

        return null;
    }


    public void CopyFromOuter<T>()
    {
        var outer = GetOuterProvider();
        if (outer == null) return;
        var outerInstances = outer.GetService<IEnumerable<T>>();
        RegisterAll(outerInstances);
    }
}