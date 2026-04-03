using CrudKit.Core.Context;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Entity lifecycle hooks. All methods have empty default implementations.
/// Override only the hooks you need.
/// Execution order: Validate → Before* → DB op → After*
/// </summary>
public interface ICrudHooks<T> where T : class, IEntity
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
    /// Use for row-level security and PermScope.Own-style filtering.
    /// Default: returns the query unchanged.
    /// </summary>
    IQueryable<T> ApplyScope(IQueryable<T> query, CrudKit.Core.Context.AppContext ctx) => query;
}
