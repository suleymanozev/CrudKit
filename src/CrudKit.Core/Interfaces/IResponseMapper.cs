namespace CrudKit.Core.Interfaces;

/// <summary>
/// Maps an entity to a response DTO for API output.
/// If registered in DI, CrudEndpointMapper uses it instead of returning raw entities.
/// </summary>
public interface IResponseMapper<TEntity, TResponse>
    where TEntity : class, IAuditableEntity
    where TResponse : class
{
    TResponse Map(TEntity entity);
    IQueryable<TResponse> Project(IQueryable<TEntity> query);
}
