using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Numbering;
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
        // Register TContext also as CrudKitDbContext so EfRepo<T> can receive it.
        services.AddScoped<CrudKitDbContext>(sp => sp.GetRequiredService<TContext>());

        // Dialect — auto-detected from TContext's provider.
        services.TryAddScoped<IDbDialect>(sp =>
        {
            var db = sp.GetRequiredService<TContext>();
            return DialectDetector.Detect(db);
        });

        // Query pipeline
        services.TryAddScoped<FilterApplier>();
        services.AddScoped(typeof(QueryBuilder<>));

        // Open generic repository: IRepo<T> → EfRepo<T>
        services.AddScoped(typeof(IRepo<>), typeof(EfRepo<>));

        // Document numbering
        services.TryAddScoped<SequenceGenerator>();

        return services;
    }
}
