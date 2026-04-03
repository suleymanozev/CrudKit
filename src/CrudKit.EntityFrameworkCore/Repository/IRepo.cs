using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;

namespace CrudKit.EntityFrameworkCore.Repository;

/// <summary>Generic CRUD contract for EF Core entities.</summary>
public interface IRepo<T> where T : class, IEntity
{
    Task<T> FindById(string id, CancellationToken ct = default);
    Task<T?> FindByIdOrDefault(string id, CancellationToken ct = default);
    Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default);
    Task<List<T>> FindByField(string fieldName, object value, CancellationToken ct = default);
    Task<T> Create(object createDto, CancellationToken ct = default);
    Task<T> Update(string id, object updateDto, CancellationToken ct = default);
    Task Delete(string id, CancellationToken ct = default);
    Task Restore(string id, CancellationToken ct = default);
    Task<bool> Exists(string id, CancellationToken ct = default);
    Task<long> Count(CancellationToken ct = default);
}
