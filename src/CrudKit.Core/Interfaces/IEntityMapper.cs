namespace CrudKit.Core.Interfaces;

/// <summary>
/// Maps an entity to a response DTO.
/// Implemented by the application layer — CrudKit does not provide a default.
/// </summary>
public interface IEntityMapper<TEntity, TResponse>
    where TEntity : class, IEntity
    where TResponse : class
{
    TResponse Map(TEntity entity);
    IQueryable<TResponse> Project(IQueryable<TEntity> query);
}
