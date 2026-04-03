using System.Reflection;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Repository;

/// <summary>
/// Generic EF Core repository. Handles DTO→entity mapping via reflection.
/// All cross-cutting concerns (timestamps, tenant, soft delete, audit) are in CrudKitDbContext.
/// </summary>
public class EfRepo<T> : IRepo<T> where T : class, IEntity
{
    private readonly CrudKitDbContext _db;
    private readonly QueryBuilder<T> _queryBuilder;

    public EfRepo(CrudKitDbContext db, QueryBuilder<T> queryBuilder)
    {
        _db = db;
        _queryBuilder = queryBuilder;
    }

    public async Task<T> FindById(string id, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        query = IncludeApplier.Apply(query);
        var entity = await query.FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");
    }

    public async Task<T?> FindByIdOrDefault(string id, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        query = IncludeApplier.Apply(query);
        return await query.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        return await _queryBuilder.Apply(query, listParams, ct);
    }

    public async Task<List<T>> FindByField(string fieldName, object value, CancellationToken ct = default)
    {
        var prop = typeof(T).GetProperty(fieldName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop == null) return [];

        var all = await _db.Set<T>().AsNoTracking().ToListAsync(ct);
        return all.Where(e =>
        {
            var propVal = prop.GetValue(e);
            return propVal != null && propVal.Equals(value);
        }).ToList();
    }

    public async Task<T> Create(object createDto, CancellationToken ct = default)
    {
        var entity = Activator.CreateInstance<T>();
        MapDtoToEntity(createDto, entity, isCreate: true);

        _db.Set<T>().Add(entity);
        await _db.SaveChangesAsync(ct);

        ClearSkipResponseFields(entity);
        return entity;
    }

    public async Task<T> Update(string id, object updateDto, CancellationToken ct = default)
    {
        var entity = await _db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");

        MapDtoToEntity(updateDto, entity, isCreate: false);
        await _db.SaveChangesAsync(ct);

        ClearSkipResponseFields(entity);
        return entity;
    }

    public async Task Delete(string id, CancellationToken ct = default)
    {
        var entity = await _db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");

        _db.Set<T>().Remove(entity);
        // CrudKitDbContext.BeforeSaveChanges intercepts DELETE for ISoftDeletable entities
        await _db.SaveChangesAsync(ct);
    }

    public async Task Restore(string id, CancellationToken ct = default)
    {
        if (typeof(T).IsAssignableTo(typeof(ISoftDeletable)) == false)
            throw new InvalidOperationException($"{typeof(T).Name} does not implement ISoftDeletable.");

        var entity = await _db.Set<T>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound($"Deleted {typeof(T).Name} with id '{id}' was not found.");

        ((ISoftDeletable)entity).DeletedAt = null;
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> Exists(string id, CancellationToken ct = default)
        => _db.Set<T>().AnyAsync(e => e.Id == id, ct);

    public Task<long> Count(CancellationToken ct = default)
        => _db.Set<T>().LongCountAsync(ct);

    // ---- Reflection-based DTO mapping ----

    private static void MapDtoToEntity(object dto, T entity, bool isCreate)
    {
        var entityProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var entityPropMap = entityProps.ToDictionary(
            p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        foreach (var dtoProp in dto.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!entityPropMap.TryGetValue(dtoProp.Name, out var entityProp)) continue;

            // Skip system/infrastructure fields (managed by DbContext)
            if (entityProp.Name is nameof(IEntity.Id)
                or nameof(IEntity.CreatedAt)
                or nameof(IEntity.UpdatedAt)) continue;

            if (!isCreate)
            {
                if (entityProp.GetCustomAttribute<ProtectedAttribute>() != null) continue;
                if (entityProp.GetCustomAttribute<SkipUpdateAttribute>() != null) continue;
            }

            var dtoValue = dtoProp.GetValue(dto);

            // Handle Optional<T> — absent fields (HasValue=false) are skipped
            if (IsOptionalType(dtoProp.PropertyType))
            {
                var hasValue = (bool)dtoProp.PropertyType.GetProperty("HasValue")!.GetValue(dtoValue)!;
                if (!hasValue) continue;
                dtoValue = dtoProp.PropertyType.GetProperty("Value")!.GetValue(dtoValue);
            }
            else if (!isCreate && dtoValue == null)
            {
                // For plain nullable types in Update, null means "don't touch"
                continue;
            }

            // Apply BCrypt hashing for [Hashed] fields on Create
            if (isCreate
                && entityProp.GetCustomAttribute<HashedAttribute>() != null
                && dtoValue is string plainText)
            {
                entityProp.SetValue(entity, BCrypt.Net.BCrypt.HashPassword(plainText));
                continue;
            }

            entityProp.SetValue(entity, dtoValue);
        }
    }

    private static bool IsOptionalType(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>);

    private static void ClearSkipResponseFields(T entity)
    {
        foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<SkipResponseAttribute>() != null
                && prop.CanWrite)
            {
                prop.SetValue(entity, null);
            }
        }
    }
}
