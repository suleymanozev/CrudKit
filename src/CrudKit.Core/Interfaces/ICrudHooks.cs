using CrudKit.Core.Context;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Entity lifecycle hooks. All methods have empty default implementations.
/// Override only the hooks you need.
/// Execution order: Validate → Before* → DB op → After*
/// </summary>
public interface ICrudHooks<T> where T : class, IAuditableEntity
{
    Task BeforeCreate(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task AfterCreate(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task BeforeUpdate(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task AfterUpdate(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task BeforeDelete(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task AfterDelete(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task BeforeRestore(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task AfterRestore(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Applies additional filters to List and FindById queries.
    /// Use for row-level security filtering.
    /// Default: returns the query unchanged.
    /// </summary>
    IQueryable<T> ApplyScope(IQueryable<T> query, CrudKit.Core.Context.AppContext ctx) => query;

    /// <summary>
    /// Customizes EF Core Include() calls for complex include scenarios (e.g., ThenInclude).
    /// Applied before [DefaultInclude] attributes.
    /// Default: returns the query unchanged.
    /// </summary>
    IQueryable<T> ApplyIncludes(IQueryable<T> query) => query;
}
