namespace CrudKit.Core.Interfaces;

/// <summary>
/// Entity'yi response DTO'suna dönüştürür.
/// Kullanıcı bu interface'i implemente eder — CrudKit sağlamaz.
/// </summary>
public interface IEntityMapper<TEntity, TResponse>
    where TEntity : class, IEntity
    where TResponse : class
{
    TResponse Map(TEntity entity);
    IQueryable<TResponse> Project(IQueryable<TEntity> query);
}
