namespace CrudKit.Core.Interfaces;

/// <summary>
/// Combined mapper: create + update + response mapping.
/// Source Generator produces this when all CRUD operations are enabled.
/// Register as ICrudMapper and it resolves as ICreateMapper, IUpdateMapper, and IResponseMapper.
/// </summary>
public interface ICrudMapper<TEntity, TCreate, TUpdate, TResponse>
    : ICreateMapper<TEntity, TCreate>,
      IUpdateMapper<TEntity, TUpdate>,
      IResponseMapper<TEntity, TResponse>
    where TEntity : class, IEntity
    where TCreate : class
    where TUpdate : class
    where TResponse : class
{
}
