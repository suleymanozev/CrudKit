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
    Task AfterUpdate(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task BeforeDelete(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task AfterDelete(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
}
