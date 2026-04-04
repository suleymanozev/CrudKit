namespace CrudKit.Core.Interfaces;

/// <summary>
/// Applies update DTO changes to an existing entity. Respects Optional&lt;T&gt; semantics.
/// Source Generator produces implementations automatically.
/// </summary>
public interface IUpdateMapper<TEntity, TUpdate>
    where TEntity : class, IEntity
    where TUpdate : class
{
    void ApplyUpdate(TEntity entity, TUpdate dto);
}
