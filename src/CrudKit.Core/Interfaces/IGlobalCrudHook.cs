namespace CrudKit.Core.Interfaces;

/// <summary>
/// Global lifecycle hook that runs for ALL entities on every CRUD operation.
/// Use for cross-cutting concerns like search indexing, caching invalidation, event publishing.
/// Register via opts.UseGlobalHook&lt;T&gt;() or directly in DI as IGlobalCrudHook.
/// All methods have default empty implementations — override only what you need.
/// </summary>
public interface IGlobalCrudHook
{
    Task BeforeCreate(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task AfterCreate(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task BeforeUpdate(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called before an update is committed. Receives both the updated entity and its previous state.
    /// Default implementation delegates to the 2-parameter overload (ignoring existingEntity).
    /// </summary>
    Task BeforeUpdate(object entity, object? existingEntity, CrudKit.Core.Context.AppContext ctx)
        => BeforeUpdate(entity, ctx);

    Task AfterUpdate(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called after an update is committed. Receives both the updated entity and its previous state.
    /// Default implementation delegates to the 2-parameter overload (ignoring existingEntity).
    /// </summary>
    Task AfterUpdate(object entity, object? existingEntity, CrudKit.Core.Context.AppContext ctx)
        => AfterUpdate(entity, ctx);
    Task BeforeDelete(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task AfterDelete(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
}
