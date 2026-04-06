using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.EntityFrameworkCore;

/// <summary>
/// Tracks registered CrudKitDbContext types and resolves the correct context for a given entity type.
/// Enables multi-DbContext scenarios (modular monolith) where different entities belong to different contexts.
/// </summary>
public class CrudKitContextRegistry
{
    private readonly List<Type> _contextTypes = new();
    private readonly ConcurrentDictionary<Type, Type?> _entityToContext = new();

    /// <summary>Register a DbContext type so its entities can be resolved at runtime.</summary>
    public void Add<TContext>() where TContext : CrudKitDbContext
    {
        var contextType = typeof(TContext);
        if (!_contextTypes.Contains(contextType))
            _contextTypes.Add(contextType);
    }

    /// <summary>All registered context types.</summary>
    public IReadOnlyList<Type> ContextTypes => _contextTypes;

    /// <summary>
    /// Find which registered DbContext type owns a DbSet for the given entity type.
    /// Returns null if no context declares a DbSet for the entity.
    /// The result is cached per entity type for subsequent calls.
    /// </summary>
    public Type? FindContextForEntity(Type entityType)
    {
        return _entityToContext.GetOrAdd(entityType, et =>
        {
            foreach (var contextType in _contextTypes)
            {
                var hasDbSet = contextType.GetProperties()
                    .Any(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                        && p.PropertyType.GetGenericArguments()[0] == et);
                if (hasDbSet) return contextType;
            }
            return null;
        });
    }

    /// <summary>
    /// Resolve the correct CrudKitDbContext instance for entity type <typeparamref name="T"/>.
    /// Falls back to <see cref="CrudKitDbContext"/> if no specific context is found.
    /// </summary>
    public CrudKitDbContext ResolveFor<T>(IServiceProvider services) where T : class
    {
        var contextType = FindContextForEntity(typeof(T));
        if (contextType != null)
            return (CrudKitDbContext)services.GetRequiredService(contextType);
        return services.GetRequiredService<CrudKitDbContext>();
    }
}
