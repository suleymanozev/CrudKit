using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;

namespace CrudKit.EntityFrameworkCore.Repository;

/// <summary>Generic CRUD contract for EF Core entities.</summary>
public interface IRepo<T> where T : class, IEntity
{
    Task<T> FindById(Guid id, CancellationToken ct = default);
    Task<T?> FindByIdOrDefault(Guid id, CancellationToken ct = default);
    Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default);
    Task<List<T>> FindByField(string fieldName, object value, CancellationToken ct = default);
    Task<T> Create(object createDto, CancellationToken ct = default);
    Task<T> Update(Guid id, object updateDto, CancellationToken ct = default);
    Task Delete(Guid id, CancellationToken ct = default);
    Task Restore(Guid id, CancellationToken ct = default);

    /// <summary>Permanently deletes a soft-deleted entity. Only works on ISoftDeletable entities where DeletedAt is not null.</summary>
    Task HardDelete(Guid id, CancellationToken ct = default);
    Task<bool> Exists(Guid id, CancellationToken ct = default);
    Task<long> Count(CancellationToken ct = default);

    /// <summary>Bulk update entities matching the given filters.</summary>
    Task<int> BulkUpdate(Dictionary<string, FilterOp> filters, Dictionary<string, object?> values, CancellationToken ct = default);

    /// <summary>Bulk delete entities matching the given filters. Soft-deletes if ISoftDeletable.</summary>
    Task<int> BulkDelete(Dictionary<string, FilterOp> filters, CancellationToken ct = default);

    /// <summary>Count entities matching the given filters.</summary>
    Task<long> BulkCount(Dictionary<string, FilterOp> filters, CancellationToken ct = default);
}
