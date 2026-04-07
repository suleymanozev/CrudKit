using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrudKit.Identity;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CrudKit EF Core infrastructure for an Identity-based DbContext.
    /// Call after AddDbContext&lt;TContext&gt;.
    /// </summary>
    /// <example>
    /// services.AddDbContext&lt;AppIdentityDbContext&gt;(...);
    /// services.AddCrudKitIdentityEf&lt;AppIdentityDbContext&gt;();
    /// </example>
    public static IServiceCollection AddCrudKitIdentityEf<TContext>(this IServiceCollection services)
        where TContext : class, ICrudKitDbContext
    {
        // Context registry for multi-DbContext support.
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
        registry.AddContext<TContext>();

        // Register TContext also as ICrudKitDbContext for EfRepo fallback resolution.
        services.TryAddScoped<ICrudKitDbContext>(sp => sp.GetRequiredService<TContext>());

        // Dialect — auto-detected from TContext's provider.
        // ICrudKitDbContext implementations are always DbContext subclasses; the cast is safe.
        services.TryAddScoped<IDbDialect>(sp =>
        {
            var db = (Microsoft.EntityFrameworkCore.DbContext)(object)sp.GetRequiredService<TContext>();
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
