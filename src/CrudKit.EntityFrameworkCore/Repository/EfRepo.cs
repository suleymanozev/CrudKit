using System.Linq.Expressions;
using System.Reflection;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Numbering;
using CrudKit.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.EntityFrameworkCore.Repository;

/// <summary>
/// Generic EF Core repository. Handles DTO→entity mapping via reflection.
/// All cross-cutting concerns (timestamps, tenant, soft delete, audit) are in CrudKitDbContext.
/// </summary>
public class EfRepo<T> : IRepo<T> where T : class, IAuditableEntity
{
    private readonly CrudKitDbContext _db;
    private readonly QueryBuilder<T> _queryBuilder;
    private readonly FilterApplier _filterApplier;
    private readonly ICrudHooks<T>? _hooks;
    private readonly SequenceGenerator? _sequenceGenerator;

    public EfRepo(IServiceProvider services, QueryBuilder<T> queryBuilder, FilterApplier filterApplier, ICrudHooks<T>? hooks = null, SequenceGenerator? sequenceGenerator = null)
    {
        _db = ResolveContext(services);
        _queryBuilder = queryBuilder;
        _filterApplier = filterApplier;
        _hooks = hooks;
        _sequenceGenerator = sequenceGenerator;
    }

    /// <summary>
    /// Resolve the correct DbContext for entity type T using the context registry.
    /// Falls back to CrudKitDbContext when no registry is available (backward compat).
    /// </summary>
    private static CrudKitDbContext ResolveContext(IServiceProvider services)
    {
        var registry = services.GetService<CrudKitContextRegistry>();
        if (registry != null)
            return registry.ResolveFor<T>(services);
        return services.GetRequiredService<CrudKitDbContext>();
    }

    /// <summary>
    /// Builds a minimal AppContext for hook calls. Services is null because EfRepo
    /// does not hold a reference to IServiceProvider.
    /// </summary>
    private CrudKit.Core.Context.AppContext BuildAppContext()
    {
        return new CrudKit.Core.Context.AppContext
        {
            Services = null!,
            CurrentUser = _db.CurrentUser,
            TenantContext = _db.TenantCtx,
        };
    }

    public async Task<T> FindById(Guid id, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        query = IncludeApplier.Apply(query, includeParam: null, isDetailQuery: true);
        if (_hooks != null)
            query = _hooks.ApplyScope(query, BuildAppContext());
        var entity = await query.FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");
    }

    public async Task<T?> FindByIdOrDefault(Guid id, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        query = IncludeApplier.Apply(query, includeParam: null, isDetailQuery: true);
        if (_hooks != null)
            query = _hooks.ApplyScope(query, BuildAppContext());
        return await query.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        if (_hooks != null)
            query = _hooks.ApplyScope(query, BuildAppContext());
        return await _queryBuilder.Apply(query, listParams, ct);
    }

    public async Task<List<T>> FindByField(string fieldName, object value, CancellationToken ct = default)
    {
        var prop = typeof(T).GetProperty(fieldName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop == null) return [];

        object? converted;
        try
        {
            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (targetType == typeof(Guid) && value is string s)
                converted = Guid.Parse(s);
            else
                converted = Convert.ChangeType(value, targetType);
        }
        catch
        {
            return [];
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
        var entity = Activator.CreateInstance<T>();
        MapDtoToEntity(createDto, entity, isCreate: true);

        // Auto-assign document number for IDocumentNumbering entities
        if (entity is IDocumentNumbering numbering && _sequenceGenerator != null)
        {
            var tenantId = (entity is IMultiTenant mt) ? mt.TenantId : "__global__";
            var nextMethod = typeof(SequenceGenerator)
                .GetMethod(nameof(SequenceGenerator.Next))!
                .MakeGenericMethod(typeof(T));
            var number = await (Task<string>)nextMethod.Invoke(_sequenceGenerator, [tenantId, ct])!;
            numbering.DocumentNumber = number;
        }

        _db.Set<T>().Add(entity);
        await _db.SaveChangesAsync(ct);

        ClearSkipResponseFields(entity);
        return entity;
    }

    public async Task<T> Update(Guid id, object updateDto, CancellationToken ct = default)
    {
        var entity = await _db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");

        MapDtoToEntity(updateDto, entity, isCreate: false);
        await _db.SaveChangesAsync(ct);

        ClearSkipResponseFields(entity);
        return entity;
    }

    public async Task Delete(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");

        _db.Set<T>().Remove(entity);
        // CrudKitDbContext.BeforeSaveChanges intercepts DELETE for ISoftDeletable entities
        await _db.SaveChangesAsync(ct);
    }

    public async Task Restore(Guid id, CancellationToken ct = default)
    {
        if (typeof(T).IsAssignableTo(typeof(ISoftDeletable)) == false)
            throw new InvalidOperationException($"{typeof(T).Name} does not implement ISoftDeletable.");

        var entity = await _db.Set<T>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound($"Deleted {typeof(T).Name} with id '{id}' was not found.");

        // Re-enforce tenant isolation since IgnoreQueryFilters bypasses the tenant filter.
        if (entity is IMultiTenant multiTenant)
        {
            var currentTenantId = _db.CurrentTenantId;
            if (currentTenantId != null && multiTenant.TenantId != currentTenantId)
                throw AppError.NotFound($"Deleted {typeof(T).Name} with id '{id}' was not found.");
        }

        ((ISoftDeletable)entity).DeletedAt = null;

        // Check [Unique] properties for conflicts with active (non-deleted) records before restoring
        var uniqueProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<UniqueAttribute>() != null)
            .ToList();

        foreach (var prop in uniqueProps)
        {
            var value = prop.GetValue(entity);
            if (value == null) continue;

            // Build predicate: e => e.Id != entity.Id && e.<prop> == value
            var param = Expression.Parameter(typeof(T), "e");
            var body = Expression.AndAlso(
                Expression.NotEqual(
                    Expression.Property(param, nameof(IAuditableEntity.Id)),
                    Expression.Constant(entity.Id)),
                Expression.Equal(
                    Expression.Property(param, prop),
                    Expression.Constant(value, prop.PropertyType)));
            var predicate = Expression.Lambda<Func<T, bool>>(body, param);

            // Normal query (no IgnoreQueryFilters) — checks only active records
            var conflicts = await _db.Set<T>().Where(predicate).AnyAsync(ct);
            if (conflicts)
                throw AppError.Conflict(
                    $"Cannot restore: active {typeof(T).Name} with {prop.Name} = '{value}' already exists.");
        }

        await _db.SaveChangesAsync(ct);

        // Cascade restore to child entities declared via [CascadeSoftDelete] attributes
        var cascadeAttributes = typeof(T).GetCustomAttributes<CascadeSoftDeleteAttribute>();
        foreach (var attr in cascadeAttributes)
        {
            var childEntityType = _db.Model.FindEntityType(attr.ChildType);
            if (childEntityType == null) continue;

            var tableName = childEntityType.GetTableName();
            var schema = childEntityType.GetSchema();
            if (tableName == null) continue;

            var storeObject = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(tableName, schema);
            var fkColumn = childEntityType.FindProperty(attr.ForeignKeyProperty)?.GetColumnName(storeObject);
            var deletedAtColumn = childEntityType.FindProperty(nameof(ISoftDeletable.DeletedAt))?.GetColumnName(storeObject);
            var updatedAtColumn = childEntityType.FindProperty(nameof(IAuditableEntity.UpdatedAt))?.GetColumnName(storeObject);

            if (fkColumn == null || deletedAtColumn == null || updatedAtColumn == null)
                continue;

            var now = entity.UpdatedAt; // Use the same timestamp set by SaveChanges
            var sql = string.Format(
                "UPDATE \"{0}\" SET \"{1}\" = NULL, \"{2}\" = {{0}} WHERE \"{3}\" = {{1}} AND \"{1}\" IS NOT NULL",
                tableName, deletedAtColumn, updatedAtColumn, fkColumn);
            _db.Database.ExecuteSqlRaw(sql, now, id);
        }
    }

    public Task<bool> Exists(Guid id, CancellationToken ct = default)
        => _db.Set<T>().AnyAsync(e => e.Id == id, ct);

    public Task<long> Count(CancellationToken ct = default)
        => _db.Set<T>().LongCountAsync(ct);

    // ---- Bulk operations ----

    public async Task<long> BulkCount(Dictionary<string, FilterOp> filters, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        foreach (var (field, op) in filters)
            query = _filterApplier.Apply(query, field, op);
        return await query.LongCountAsync(ct);
    }

    public async Task<int> BulkDelete(Dictionary<string, FilterOp> filters, CancellationToken ct = default)
    {
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
        var query = _db.Set<T>().AsQueryable();
        foreach (var (field, op) in filters)
            query = _filterApplier.Apply(query, field, op);

        var builderType = typeof(Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<T>);

        var setPropertyMethods = builderType.GetMethods()
            .Where(m => m.Name == "SetProperty" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
            .ToList();

        var setPropertyBase = setPropertyMethods.FirstOrDefault(m =>
        {
            var p = m.GetParameters()[1];
            return !p.ParameterType.IsGenericType || p.ParameterType.GetGenericTypeDefinition() != typeof(Expression<>);
        });

        if (setPropertyBase == null)
            throw new InvalidOperationException($"Could not find SetProperty<TProperty>(Expression<Func<T,TProperty>>, TProperty) method on UpdateSettersBuilder<{typeof(T).Name}>");

        var setterCalls = new List<(MethodInfo method, object lambda, object? value)>();
        var entityParam = Expression.Parameter(typeof(T), "e");

        foreach (var (fieldName, value) in values)
        {
            var prop = typeof(T).GetProperty(fieldName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) continue;

            object? converted;
            try
            {
                converted = value == null ? null : Convert.ChangeType(value, prop.PropertyType);
            }
            catch
            {
                continue;
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
            if (entityProp.Name is nameof(IAuditableEntity.Id)
                or nameof(IAuditableEntity.CreatedAt)
                or nameof(IAuditableEntity.UpdatedAt)) continue;

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

            // Apply BCrypt hashing for [Hashed] fields
            if (entityProp.GetCustomAttribute<HashedAttribute>() != null
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
            if (prop.GetCustomAttribute<SkipResponseAttribute>() == null || !prop.CanWrite) continue;
            if (prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) == null) continue;
            prop.SetValue(entity, null);
        }
    }
}
