using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;

namespace CrudKit.EntityFrameworkCore.Repository;

/// <summary>Disposable transaction handle returned by <see cref="IRepo{T}.BeginTransactionAsync"/>.</summary>
public interface IRepoTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}

/// <summary>Generic CRUD contract for EF Core entities.</summary>
public interface IRepo<T> where T : class, IEntity
{
    // ---- Transaction ----

    /// <summary>Begins a database transaction. Commit via the returned handle; dispose to rollback.</summary>
    Task<IRepoTransaction> BeginTransactionAsync(CancellationToken ct = default);

    // ---- Query ----

    Task<T> FindById(Guid id, CancellationToken ct = default);
    Task<T?> FindByIdOrDefault(Guid id, CancellationToken ct = default);

    /// <summary>Finds a soft-deleted entity by ID, bypassing the soft-delete filter. Returns null if not found.</summary>
    Task<T?> FindDeletedById(Guid id, CancellationToken ct = default);
    Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default);
    Task<List<T>> FindByField(string fieldName, object value, CancellationToken ct = default);
    Task<bool> Exists(Guid id, CancellationToken ct = default);
    Task<long> Count(CancellationToken ct = default);

    /// <summary>Count entities matching the given filters.</summary>
    Task<long> Count(Dictionary<string, FilterOp> filters, CancellationToken ct = default);

    /// <summary>Find entities matching the given filters. Returns untracked entities.</summary>
    Task<List<T>> FindByFilter(Dictionary<string, FilterOp> filters, CancellationToken ct = default);

    // ---- Write ----

    Task<T> Create(object createDto, CancellationToken ct = default);

    /// <summary>
    /// Create with post-mapping customization. The <paramref name="configureEntity"/> callback runs after
    /// DTO→entity mapping but before SaveChanges, allowing caller to set fields not present on the DTO
    /// (e.g., foreign keys derived from route parameters).
    /// </summary>
    Task<T> Create(object createDto, Action<T> configureEntity, CancellationToken ct = default);

    /// <summary>
    /// Create from a pre-built entity instance (e.g., import). Runs hooks and SaveChanges.
    /// </summary>
    Task<T> CreateEntity(T entity, CancellationToken ct = default);

    Task<T> Update(Guid id, object updateDto, CancellationToken ct = default);
    Task Delete(Guid id, CancellationToken ct = default);
    Task Restore(Guid id, CancellationToken ct = default);

    /// <summary>Permanently deletes a soft-deleted entity. Only works on ISoftDeletable entities where DeletedAt is not null.</summary>
    Task HardDelete(Guid id, CancellationToken ct = default);

    /// <summary>Bulk-purges soft-deleted entities older than the given cutoff. Writes audit entries before physical deletion.</summary>
    Task<int> BulkPurge(DateTime cutoff, CancellationToken ct = default);

    /// <summary>Bulk update entities matching the given filters.</summary>
    Task<int> BulkUpdate(Dictionary<string, FilterOp> filters, Dictionary<string, object?> values, CancellationToken ct = default);

    /// <summary>Bulk delete entities matching the given filters. Soft-deletes if ISoftDeletable.</summary>
    Task<int> BulkDelete(Dictionary<string, FilterOp> filters, CancellationToken ct = default);

    /// <summary>
    /// Updates a single property on a tracked entity by ID. Used by Transition endpoint.
    /// Loads entity, sets property, runs SaveChanges. Returns the updated entity.
    /// </summary>
    Task<T> SetProperty(Guid id, string propertyName, object? value, CancellationToken ct = default);
}
