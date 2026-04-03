using CrudKit.Core.Context;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Entity lifecycle hook'ları. Tüm metodların default implementasyonu boştur.
/// Kullanıcı sadece ihtiyacı olanı override eder.
/// Hook sırası: Validate → Before* → DB op → After*
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
    /// List ve FindById sorgularına ek filtre uygular.
    /// Row-level security, PermScope.Own gibi filtreleri buraya yaz.
    /// Default: query olduğu gibi döner.
    /// </summary>
    IQueryable<T> ApplyScope(IQueryable<T> query, CrudKit.Core.Context.AppContext ctx) => query;
}
