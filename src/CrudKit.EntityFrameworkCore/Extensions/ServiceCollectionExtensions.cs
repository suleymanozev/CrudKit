using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrudKit.EntityFrameworkCore.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the CrudKit EF Core infrastructure.
    /// Call after AddDbContext&lt;TContext&gt;.
    /// </summary>
    /// <example>
    /// services.AddDbContext&lt;AppDbContext&gt;(...);
    /// services.AddCrudKitEf&lt;AppDbContext&gt;();
    /// </example>
    public static IServiceCollection AddCrudKitEf<TContext>(this IServiceCollection services)
        where TContext : CrudKitDbContext
    {
        // Context registry for multi-DbContext support (modular monolith).
        // Retrieve or create a shared instance so multiple AddCrudKitEf calls
        // populate the same registry without BuildServiceProvider anti-pattern.
        var registryDescriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(CrudKitContextRegistry));
        CrudKitContextRegistry registry;
        if (registryDescriptor?.ImplementationInstance is CrudKitContextRegistry existing)
        {
            registry = existing;
        }
        else
        {
            registry = new CrudKitContextRegistry();
            services.AddSingleton(registry);
        }
        registry.Add<TContext>();

        // Register TContext also as CrudKitDbContext and ICrudKitDbContext for fallback resolution.
        services.TryAddScoped<CrudKitDbContext>(sp => sp.GetRequiredService<TContext>());
        services.TryAddScoped<ICrudKitDbContext>(sp => sp.GetRequiredService<TContext>());

        // Dialect — auto-detected from TContext's provider.
        services.TryAddScoped<IDbDialect>(sp =>
        {
            var db = sp.GetRequiredService<TContext>();
            return DialectDetector.Detect(db);
        });

        // Query pipeline
        services.TryAddScoped<FilterApplier>();
        services.TryAdd(ServiceDescriptor.Scoped(typeof(QueryBuilder<>), typeof(QueryBuilder<>)));

        // Open generic repository: IRepo<T> → EfRepo<T>
        services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepo<>), typeof(EfRepo<>)));

        return services;
    }
}
