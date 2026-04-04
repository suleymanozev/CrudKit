namespace CrudKit.Core.Interfaces;

/// <summary>
/// Creates a new entity from a create DTO. Replaces reflection-based mapping.
/// Source Generator produces implementations automatically.
/// </summary>
public interface ICreateMapper<TEntity, TCreate>
    where TEntity : class, IEntity
    where TCreate : class
{
    TEntity FromCreateDto(TCreate dto);
}
