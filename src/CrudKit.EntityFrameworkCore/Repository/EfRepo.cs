using System.Linq.Expressions;
using System.Reflection;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.EntityFrameworkCore.Repository;

/// <summary>
/// Generic EF Core repository. Handles DTO→entity mapping via reflection.
/// All cross-cutting concerns (timestamps, tenant, soft delete, audit) are in CrudKitDbContext.
/// </summary>
public class EfRepo<T> : IRepo<T> where T : class, IEntity
{
    private readonly ICrudKitDbContext _db;
    private readonly IServiceProvider _services;
    private readonly QueryBuilder<T> _queryBuilder;
    private readonly FilterApplier _filterApplier;
    private readonly ICrudHooks<T>? _hooks;
    private readonly IDataFilter<ISoftDeletable>? _softDeleteFilter;

    public EfRepo(IServiceProvider services, QueryBuilder<T> queryBuilder, FilterApplier filterApplier, ICrudHooks<T>? hooks = null)
    {
        _db = ResolveContext(services);
        _services = services;
        _queryBuilder = queryBuilder;
        _filterApplier = filterApplier;
        _hooks = hooks;
        _softDeleteFilter = services.GetService<IDataFilter<ISoftDeletable>>();
    }

    /// <summary>
    /// Resolve the correct ICrudKitDbContext for entity type T using the context registry.
    /// Falls back to ICrudKitDbContext when no registry is available (backward compat).
    /// </summary>
    private static ICrudKitDbContext ResolveContext(IServiceProvider services)
    {
        var registry = services.GetService<CrudKitContextRegistry>();
        if (registry is not null)
            return registry.ResolveFor<T>(services);
        return services.GetRequiredService<ICrudKitDbContext>();
    }

    /// <summary>
    /// Throws a 400 BadRequest if the entity type implements IMultiTenant but no tenant
    /// context is available. Called at the start of every public IRepo method.
    /// </summary>
    private void EnsureTenantContext()
    {
        if (typeof(IMultiTenant).IsAssignableFrom(typeof(T)))
        {
            var tenantId = _db.CurrentTenantId;
            if (string.IsNullOrEmpty(tenantId))
                throw AppError.BadRequest("Tenant context is required for multi-tenant operations.");
        }
    }

    /// <summary>
    /// Builds a minimal AppContext for hook calls.
    /// </summary>
    private CrudKit.Core.Context.AppContext BuildAppContext()
    {
        return new CrudKit.Core.Context.AppContext
        {
            Services = _services,
            CurrentUser = _db.CurrentUser,
            TenantContext = _db.TenantCtx,
        };
    }

    public async Task<T> FindById(Guid id, CancellationToken ct = default)
    {
        EnsureTenantContext();
        var query = _db.Set<T>().AsNoTracking();
        query = IncludeApplier.Apply(query, includeParam: null, isDetailQuery: true);
        if (_hooks is not null)
            query = _hooks.ApplyScope(query, BuildAppContext());
        var entity = await query.FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");
    }

    public async Task<T?> FindByIdOrDefault(Guid id, CancellationToken ct = default)
    {
        EnsureTenantContext();
        var query = _db.Set<T>().AsNoTracking();
        query = IncludeApplier.Apply(query, includeParam: null, isDetailQuery: true);
        if (_hooks is not null)
            query = _hooks.ApplyScope(query, BuildAppContext());
        return await query.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default)
    {
        EnsureTenantContext();
        var query = _db.Set<T>().AsNoTracking();
        if (_hooks is not null)
            query = _hooks.ApplyScope(query, BuildAppContext());
        return await _queryBuilder.Apply(query, listParams, ct);
    }

    public async Task<List<T>> FindByField(string fieldName, object value, CancellationToken ct = default)
    {
        EnsureTenantContext();
        var prop = typeof(T).GetProperty(fieldName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop is null) return [];

        object? converted;
        var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        try
        {
            if (targetType == typeof(Guid) && value is string s)
                converted = Guid.Parse(s);
            else
                converted = Convert.ChangeType(value, targetType);
        }
        catch
        {
            throw AppError.BadRequest($"Invalid filter value '{value}' for field '{fieldName}'. Expected type: {targetType.Name}");
        }

        var param = Expression.Parameter(typeof(T), "e");
        var body = Expression.Equal(
            Expression.Property(param, prop),
            Expression.Constant(converted, prop.PropertyType));
        var predicate = Expression.Lambda<Func<T, bool>>(body, param);

        return await _db.Set<T>().AsNoTracking().Where(predicate).ToListAsync(ct);
    }

    public async Task<T> Create(object createDto, CancellationToken ct = default)
    {
        EnsureTenantContext();
        T entity;
        try
        {
            entity = Activator.CreateInstance<T>()!;
        }
        catch (MissingMethodException)
        {
            throw new InvalidOperationException(
                $"Entity '{typeof(T).Name}' must have a public parameterless constructor for entity-as-DTO mapping. " +
                $"If using DDD pattern (private constructor), register an ICreateMapper<{typeof(T).Name}, TCreateDto>.");
        }

        MapDtoToEntity(createDto, entity, isCreate: true);

        _db.Set<T>().Add(entity);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw AppError.Conflict($"A {typeof(T).Name} with the same unique value already exists.");
        }

        ClearSkipResponseFields(entity);
        return entity;
    }

    public async Task<T> Update(Guid id, object updateDto, CancellationToken ct = default)
    {
        EnsureTenantContext();
        var entity = await _db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");

        MapDtoToEntity(updateDto, entity, isCreate: false);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw AppError.Conflict($"A {typeof(T).Name} with the same unique value already exists.");
        }

        ClearSkipResponseFields(entity);
        return entity;
    }

    public async Task Delete(Guid id, CancellationToken ct = default)
    {
        EnsureTenantContext();
        var entity = await _db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");

        _db.Set<T>().Remove(entity);
        // CrudKitDbContext.BeforeSaveChanges intercepts DELETE for ISoftDeletable entities
        await _db.SaveChangesAsync(ct);
    }

    public async Task Restore(Guid id, CancellationToken ct = default)
    {
        EnsureTenantContext();
        if (!typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
            throw new InvalidOperationException($"{typeof(T).Name} does not implement ISoftDeletable.");

        // Disable only soft-delete filter — tenant filter stays active (no cross-tenant leak)
        using (_softDeleteFilter!.Disable())
        {
            var entity = await _db.Set<T>()
                .FirstOrDefaultAsync(e => e.Id == id, ct)
                ?? throw AppError.NotFound($"Deleted {typeof(T).Name} with id '{id}' was not found.");

            // Capture the batch ID before clearing soft-delete state
            var deleteBatchId = ((ISoftDeletable)entity).DeleteBatchId;

            ((ISoftDeletable)entity).DeletedAt = null;
            ((ISoftDeletable)entity).DeleteBatchId = null;

            // Check [Unique] properties — re-enable soft-delete filter to check only active records
            using (_softDeleteFilter.Enable())
            {
                var entityInfo = EntityMetadataCache.GetEntityInfo(typeof(T));
                var uniqueProps = entityInfo.UniqueProperties;

                foreach (var prop in uniqueProps)
                {
                    var value = prop.GetValue(entity);
                    if (value is null) continue;

                    var param = Expression.Parameter(typeof(T), "e");
                    var body = Expression.AndAlso(
                        Expression.NotEqual(
                            Expression.Property(param, nameof(IAuditableEntity.Id)),
                            Expression.Constant(entity.Id)),
                        Expression.Equal(
                            Expression.Property(param, prop),
                            Expression.Constant(value, prop.PropertyType)));
                    var predicate = Expression.Lambda<Func<T, bool>>(body, param);

                    var conflicts = await _db.Set<T>().Where(predicate).AnyAsync(ct);
                    if (conflicts)
                        throw AppError.Conflict(
                            $"Cannot restore: active {typeof(T).Name} with {prop.Name} = '{value}' already exists.");
                }
            }

            await _db.SaveChangesAsync(ct);

            // Cascade restore: restore child entities that were deleted in the same batch as the parent
            if (deleteBatchId is not null)
            {
                var cascadeAttributes = EntityMetadataCache.GetEntityInfo(typeof(T)).CascadeAttributes;
                foreach (var attr in cascadeAttributes)
                {
                    var childEntityType = _db.Model.FindEntityType(attr.ChildType);
                    if (childEntityType is null) continue;

                    var tableName = childEntityType.GetTableName();
                    var schema = childEntityType.GetSchema();
                    if (tableName is null) continue;

                    var qualifiedTable = schema is not null ? $"\"{schema}\".\"{tableName}\"" : $"\"{tableName}\"";
                    var storeObject = StoreObjectIdentifier.Table(tableName, schema);
                    var fkColumn = childEntityType.FindProperty(attr.ForeignKeyProperty)?.GetColumnName(storeObject);
                    var deletedAtColumn = childEntityType.FindProperty(nameof(ISoftDeletable.DeletedAt))?.GetColumnName(storeObject);
                    var updatedAtColumn = childEntityType.FindProperty(nameof(IAuditableEntity.UpdatedAt))?.GetColumnName(storeObject);
                    var batchIdColumn = childEntityType.FindProperty(nameof(ISoftDeletable.DeleteBatchId))?.GetColumnName(storeObject);

                    if (fkColumn is null || deletedAtColumn is null || batchIdColumn is null) continue;

                    var now = DateTime.UtcNow;

                    if (updatedAtColumn is not null)
                    {
                        // Restore only children deleted in the same batch — set DeletedAt and DeleteBatchId to NULL
                        var sql = $"UPDATE {qualifiedTable} SET \"{deletedAtColumn}\" = NULL, \"{batchIdColumn}\" = NULL, \"{updatedAtColumn}\" = {{2}} WHERE \"{fkColumn}\" = {{0}} AND \"{batchIdColumn}\" = {{1}}";
                        _db.Database.ExecuteSqlRaw(sql, id, deleteBatchId, now);
                    }
                    else
                    {
                        var sql = $"UPDATE {qualifiedTable} SET \"{deletedAtColumn}\" = NULL, \"{batchIdColumn}\" = NULL WHERE \"{fkColumn}\" = {{0}} AND \"{batchIdColumn}\" = {{1}}";
                        _db.Database.ExecuteSqlRaw(sql, id, deleteBatchId);
                    }
                }
            }
        } // soft-delete filter re-enabled
    }

    public async Task HardDelete(Guid id, CancellationToken ct = default)
    {
        EnsureTenantContext();
        if (!typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
            throw new InvalidOperationException($"{typeof(T).Name} does not implement ISoftDeletable.");

        // Disable soft-delete filter — tenant filter stays active
        using (_softDeleteFilter!.Disable())
        {
            var entity = await _db.Set<T>()
                .FirstOrDefaultAsync(e => e.Id == id, ct)
                ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");

            // Must be soft-deleted already
            if (((ISoftDeletable)entity).DeletedAt is null)
                throw AppError.BadRequest($"{typeof(T).Name} with id '{id}' is not soft-deleted. Delete it first.");

            // Hard delete — ExecuteDeleteAsync bypasses SaveChanges interception
            await _db.Set<T>()
                .Where(e => e.Id == id)
                .ExecuteDeleteAsync(ct);
        }
    }

    public Task<bool> Exists(Guid id, CancellationToken ct = default)
        => _db.Set<T>().AnyAsync(e => e.Id == id, ct);

    public Task<long> Count(CancellationToken ct = default)
        => _db.Set<T>().LongCountAsync(ct);

    // ---- Bulk operations ----

    public async Task<long> BulkCount(Dictionary<string, FilterOp> filters, CancellationToken ct = default)
    {
        EnsureTenantContext();
        var query = _db.Set<T>().AsNoTracking();
        foreach (var (field, op) in filters)
            query = _filterApplier.Apply(query, field, op);
        return await query.LongCountAsync(ct);
    }

    public async Task<int> BulkDelete(Dictionary<string, FilterOp> filters, CancellationToken ct = default)
    {
        EnsureTenantContext();
        var query = _db.Set<T>().AsQueryable();
        foreach (var (field, op) in filters)
            query = _filterApplier.Apply(query, field, op);

        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
        {
            var now = DateTime.UtcNow;

            // Build property expressions without interface casts so EF Core can translate them.
            var deletedAtProp = typeof(T).GetProperty(nameof(ISoftDeletable.DeletedAt))!;
            var updatedAtProp = typeof(T).GetProperty(nameof(IAuditableEntity.UpdatedAt))!;

            var param = Expression.Parameter(typeof(T), "e");
            var deletedAtLambda = Expression.Lambda<Func<T, DateTime?>>(
                Expression.Property(param, deletedAtProp), param);
            var updatedAtLambda = Expression.Lambda<Func<T, DateTime>>(
                Expression.Property(param, updatedAtProp), param);

            return await query.ExecuteUpdateAsync(setters =>
            {
                setters.SetProperty(deletedAtLambda, now);
                setters.SetProperty(updatedAtLambda, now);
            }, ct);
        }

        return await query.ExecuteDeleteAsync(ct);
    }

    public async Task<int> BulkUpdate(Dictionary<string, FilterOp> filters, Dictionary<string, object?> values, CancellationToken ct = default)
    {
        EnsureTenantContext();
        var query = _db.Set<T>().AsQueryable();
        foreach (var (field, op) in filters)
            query = _filterApplier.Apply(query, field, op);

        var builderType = typeof(Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<T>);

        var setPropertyBase = EntityMetadataCache.GetSetPropertyMethod(builderType);

        if (setPropertyBase is null)
            throw new InvalidOperationException($"Could not find SetProperty<TProperty>(Expression<Func<T,TProperty>>, TProperty) method on UpdateSettersBuilder<{typeof(T).Name}>");

        var setterCalls = new List<(MethodInfo method, object lambda, object? value)>();
        var entityParam = Expression.Parameter(typeof(T), "e");

        foreach (var (fieldName, value) in values)
        {
            var prop = typeof(T).GetProperty(fieldName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is null) continue;

            object? converted;
            try
            {
                converted = value is null ? null : Convert.ChangeType(value, prop.PropertyType);
            }
            catch
            {
                throw AppError.BadRequest($"Invalid value for property '{fieldName}'. Expected type: {prop.PropertyType.Name}");
            }

            var propAccess = Expression.Property(entityParam, prop);
            var funcType = typeof(Func<,>).MakeGenericType(typeof(T), prop.PropertyType);
            var lambda = Expression.Lambda(funcType, propAccess, entityParam);

            var typedSetProperty = setPropertyBase.MakeGenericMethod(prop.PropertyType);
            setterCalls.Add((typedSetProperty, lambda, converted));
        }

        return await query.ExecuteUpdateAsync(setters =>
        {
            foreach (var (method, lambda, value) in setterCalls)
                method.Invoke(setters, [lambda, value]);
        }, ct);
    }

    // ---- Unique constraint detection ----

    /// <summary>
    /// Returns true when the exception originates from a unique/duplicate key constraint violation.
    /// Covers PostgreSQL ("duplicate key value"), SQL Server ("duplicate key" / "UNIQUE KEY constraint"),
    /// and SQLite ("UNIQUE constraint failed").
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner is null) return false;

        var message = inner.Message;
        // PostgreSQL: "duplicate key value violates unique constraint"
        // SQL Server: "Cannot insert duplicate key" or "Violation of UNIQUE KEY constraint"
        // SQLite:     "UNIQUE constraint failed"
        return message.Contains("unique", StringComparison.OrdinalIgnoreCase)
            || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
    }

    // ---- Reflection-based DTO mapping ----

    private static void MapDtoToEntity(object dto, T entity, bool isCreate)
    {
        var mappings = EntityMetadataCache.GetMappings(typeof(T), dto.GetType());

        foreach (var m in mappings)
        {
            if (!isCreate)
            {
                if (m.IsProtected) continue;
                if (m.IsSkipUpdate) continue;
            }

            var dtoValue = m.DtoProperty.GetValue(dto);

            // Handle Optional<T> — absent fields (HasValue=false) are skipped
            if (m.IsOptional)
            {
                if (dtoValue is null) continue;
                var hasValueProp = m.DtoProperty.PropertyType.GetProperty("HasValue");
                if (hasValueProp is null) continue;
                var hasValue = (bool)(hasValueProp.GetValue(dtoValue) ?? false);
                if (!hasValue) continue;
                var valueProp = m.DtoProperty.PropertyType.GetProperty("Value");
                if (valueProp is null) continue;
                dtoValue = valueProp.GetValue(dtoValue);
            }
            else if (!isCreate && dtoValue is null)
            {
                // For plain nullable types in Update, null means "don't touch"
                continue;
            }

            // Apply BCrypt hashing for [Hashed] fields
            if (m.IsHashed && dtoValue is string plainText)
            {
                m.EntityProperty.SetValue(entity, BCrypt.Net.BCrypt.HashPassword(plainText));
                continue;
            }

            m.EntityProperty.SetValue(entity, dtoValue);
        }

        // Handle [Flatten] value object properties — use cached flatten props
        var entityInfo = EntityMetadataCache.GetEntityInfo(typeof(T));
        foreach (var flatProp in entityInfo.FlattenProperties)
        {
            var vo = flatProp.GetValue(entity) ?? Activator.CreateInstance(flatProp.PropertyType);
            var voType = flatProp.PropertyType;
            bool anySet = false;

            foreach (var voProp in voType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var flatName = flatProp.Name + voProp.Name;
                var dtoPropInfo = dto.GetType().GetProperty(flatName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (dtoPropInfo is null) continue;

                var dtoValue = dtoPropInfo.GetValue(dto);

                // Handle Optional<T> for update DTOs
                if (IsOptionalType(dtoPropInfo.PropertyType))
                {
                    if (dtoValue is null) continue;
                    var hasValueProp = dtoPropInfo.PropertyType.GetProperty("HasValue");
                    if (hasValueProp is null) continue;
                    var hasValue = (bool)(hasValueProp.GetValue(dtoValue) ?? false);
                    if (!hasValue) continue;
                    var valueProp = dtoPropInfo.PropertyType.GetProperty("Value");
                    if (valueProp is null) continue;
                    dtoValue = valueProp.GetValue(dtoValue);
                }
                else if (!isCreate && dtoValue is null)
                {
                    continue;
                }

                voProp.SetValue(vo, dtoValue);
                anySet = true;
            }

            if (anySet)
                flatProp.SetValue(entity, vo);
        }
    }

    private static bool IsOptionalType(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>);

    private static void ClearSkipResponseFields(T entity)
    {
        var entityInfo = EntityMetadataCache.GetEntityInfo(typeof(T));
        foreach (var prop in entityInfo.SkipResponseProperties)
        {
            prop.SetValue(entity, null);
        }
    }
}
