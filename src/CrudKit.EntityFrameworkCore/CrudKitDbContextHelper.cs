using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Concurrency;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace CrudKit.EntityFrameworkCore;

/// <summary>
/// Shared logic used by both CrudKitDbContext and CrudKitIdentityDbContext.
/// Extracted to avoid code duplication across different base-class hierarchies.
/// All methods are stateless; dependencies are passed as parameters.
/// </summary>
public static class CrudKitDbContextHelper
{
    /// <summary>
    /// Configures entity type mappings: Id generation, soft-delete filter, tenant filter,
    /// concurrency token, enum-as-string conversion, unique indexes, and CrudKit internal tables.
    /// Call from OnModelCreating after base.OnModelCreating.
    /// </summary>
    /// <param name="modelBuilder">The EF Core model builder.</param>
    /// <param name="context">The DbContext instance (used for tenant filter closure and dialect detection).</param>
    /// <param name="efOptions">Optional CrudKit EF options.</param>
    /// <param name="currentTenantIdProperty">
    /// A PropertyInfo pointing to the <c>CurrentTenantId</c> property on the concrete context type.
    /// Required so the tenant filter lambda can capture the right property accessor.
    /// </param>
    public static void ConfigureModel(
        ModelBuilder modelBuilder,
        DbContext context,
        CrudKitEfOptions? efOptions,
        PropertyInfo currentTenantIdProperty)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            var isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(clrType);
            var isMultiTenant = typeof(IMultiTenant).IsAssignableFrom(clrType);
            var isConcurrent = typeof(IConcurrent).IsAssignableFrom(clrType);
            var isEntity = typeof(IEntity).IsAssignableFrom(clrType);

            // ---- IEntity: generate Guid Id before tracking ----
            if (isEntity)
            {
                modelBuilder.Entity(clrType)
                    .Property<Guid>(nameof(IEntity.Id))
                    .ValueGeneratedOnAdd()
                    .HasSentinel(Guid.Empty)
                    .HasValueGenerator<GuidValueGenerator>();
            }

            // ---- Soft delete global filter ----
            if (isSoftDeletable && !isMultiTenant)
            {
                modelBuilder.Entity(clrType).HasQueryFilter(
                    BuildSoftDeleteFilter(clrType));
            }

            // ---- Multi-tenant global filter (combines with soft delete if both apply) ----
            if (isMultiTenant)
            {
                var tenantFilter = BuildTenantFilter(clrType, context, currentTenantIdProperty);
                var softFilter = isSoftDeletable ? BuildSoftDeleteFilter(clrType) : null;
                modelBuilder.Entity(clrType).HasQueryFilter(
                    CombineFilters(tenantFilter, softFilter));
            }

            // ---- Optimistic concurrency ----
            if (isConcurrent)
            {
                DialectDetector.Detect(context).ConfigureConcurrencyToken(modelBuilder, clrType);
            }

            // ---- Enum properties stored as strings (opt-in via UseEnumAsString()) ----
            if (efOptions?.EnumAsStringEnabled == true)
            {
                foreach (var prop in clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    if (propType.IsEnum)
                    {
                        modelBuilder.Entity(clrType)
                            .Property(prop.Name)
                            .HasConversion<string>();
                    }
                }
            }

            // ---- [Unique] attribute → unique index ----
            foreach (var prop in clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetCustomAttribute<UniqueAttribute>() == null) continue;

                var indexBuilder = modelBuilder.Entity(clrType)
                    .HasIndex(prop.Name)
                    .IsUnique();

                if (isSoftDeletable)
                    indexBuilder.HasFilter($"\"{nameof(ISoftDeletable.DeletedAt)}\" IS NULL");
            }
        }

        // CrudKit internal tables — audit log when global UseAuditTrail() is on,
        // OR when any registered entity type carries [Audited] (entity-level override).
        var anyEntityAudited = modelBuilder.Model.GetEntityTypes()
            .Any(et => et.ClrType.GetCustomAttribute<AuditedAttribute>() != null);

        if (efOptions?.AuditTrailEnabled == true || anyEntityAudited)
        {
            modelBuilder.Entity<AuditLogEntry>(b =>
            {
                b.ToTable("__crud_audit_logs");
                b.HasIndex(e => new { e.EntityType, e.EntityId });
                b.HasIndex(e => e.Timestamp);
            });
        }

        modelBuilder.Entity<SequenceEntry>(b =>
        {
            b.ToTable("__crud_sequences");
            b.HasIndex(e => new { e.EntityType, e.TenantId, e.Year }).IsUnique();
            b.Property(e => e.CurrentVal).IsConcurrencyToken();
        });
    }

    /// <summary>
    /// Processes ChangeTracker state before SaveChanges:
    /// sets timestamps, tenant id, user tracking fields, soft-delete interception,
    /// concurrency token increment, and collects cascade soft-delete operations.
    /// Returns the list of cascade SQL ops to execute after the main save.
    /// </summary>
    public static List<(string Sql, object[] Params)> ProcessBeforeSave(
        ChangeTracker changeTracker,
        ICurrentUser currentUser,
        ITenantContext? tenantContext,
        TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Step 1: Generate IDs for new entities first (needed by audit log)
        foreach (var entry in changeTracker.Entries<IEntity>()
            .Where(e => e.State == EntityState.Added).ToList())
        {
            if (entry.Entity.Id == Guid.Empty)
                entry.Entity.Id = Guid.NewGuid();
        }

        // Step 2: Set remaining fields and handle soft-delete conversion
        foreach (var entry in changeTracker.Entries<IAuditableEntity>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    if (entry.Entity is IMultiTenant mt && tenantContext?.TenantId != null)
                        mt.TenantId = tenantContext.TenantId;
                    TrySetUserField(entry, "CreatedById", currentUser);
                    TrySetUserField(entry, "UpdatedById", currentUser);
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
                    TrySetUserField(entry, "UpdatedById", currentUser);
                    TryPreserveField(entry, "CreatedById");
                    if (entry.Entity is IConcurrent concurrent)
                        concurrent.RowVersion++;
                    break;

                case EntityState.Deleted:
                    if (entry.Entity is ISoftDeletable sd)
                    {
                        entry.State = EntityState.Modified;
                        sd.DeletedAt = now;
                        entry.Entity.UpdatedAt = now;
                        TrySetUserField(entry, "DeletedById", currentUser);
                    }
                    break;
            }
        }

        return CollectCascadeSoftDeleteOps(changeTracker, now);
    }

    /// <summary>
    /// Executes collected cascade soft-delete SQL operations after the main save.
    /// </summary>
    public static void ExecuteCascadeOps(DatabaseFacade database, List<(string Sql, object[] Params)> ops)
    {
        foreach (var (sql, parameters) in ops)
        {
            database.ExecuteSqlRaw(sql, parameters[0], parameters[1], parameters[2]);
        }
    }

    /// <summary>
    /// Scans the ChangeTracker and builds a list of AuditEntry records for entities
    /// that should be audited. Must be called BEFORE base.SaveChanges so that
    /// Added/Modified/Deleted states are still available.
    /// </summary>
    public static List<AuditEntry> CollectAuditEntries(
        ChangeTracker changeTracker,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        CrudKitEfOptions? efOptions)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var globalAuditEnabled = efOptions?.AuditTrailEnabled == true;

        var entries = changeTracker.Entries()
            .Where(e =>
            {
                var type = e.Entity.GetType();
                if (type.GetCustomAttribute<NotAuditedAttribute>() != null) return false;
                if (type.GetCustomAttribute<AuditedAttribute>() != null) return true;
                return globalAuditEnabled;
            })
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (entries.Count == 0) return [];

        var auditEntries = new List<AuditEntry>();
        foreach (var entry in entries)
        {
            var audit = new AuditEntry
            {
                EntityType = entry.Entity.GetType().Name,
                EntityId = (entry.Entity as IEntity)?.Id.ToString() ?? string.Empty,
                UserId = currentUser.Id,
                Timestamp = now,
            };

            var auditableProps = entry.Properties
                .Where(p => p.Metadata.PropertyInfo?.GetCustomAttribute<AuditIgnoreAttribute>() == null)
                .ToList();

            switch (entry.State)
            {
                case EntityState.Added:
                    audit.Action = "Create";
                    audit.NewValues = SerializeCurrentValues(auditableProps);
                    break;

                case EntityState.Modified:
                    var modified = auditableProps.Where(p => p.IsModified).ToList();
                    audit.Action = "Update";
                    audit.OldValues = SerializeOriginalValues(modified);
                    audit.NewValues = SerializeCurrentValues(modified);
                    audit.ChangedFields = JsonSerializer.Serialize(
                        modified.Select(p => p.Metadata.Name));
                    break;

                case EntityState.Deleted:
                    audit.Action = "Delete";
                    audit.OldValues = SerializeCurrentValues(auditableProps);
                    break;
            }

            auditEntries.Add(audit);
        }

        return auditEntries;
    }

    // ---- Filter expression builders ----

    /// <summary>Builds a lambda expression that filters out soft-deleted entities.</summary>
    public static LambdaExpression BuildSoftDeleteFilter(Type entityType)
    {
        var param = Expression.Parameter(entityType, "e");
        var prop = Expression.Property(param, nameof(ISoftDeletable.DeletedAt));
        var condition = Expression.Equal(prop, Expression.Constant(null, typeof(DateTime?)));
        return Expression.Lambda(condition, param);
    }

    /// <summary>
    /// Builds a lambda expression that filters entities by the current tenant id.
    /// The filter closure captures a property accessor on the concrete context instance
    /// so EF Core re-evaluates it per query.
    /// </summary>
    public static LambdaExpression BuildTenantFilter(
        Type entityType,
        DbContext context,
        PropertyInfo currentTenantIdProperty)
    {
        var param = Expression.Parameter(entityType, "e");
        var prop = Expression.Property(param, nameof(IMultiTenant.TenantId));
        var tenantIdAccess = Expression.Property(Expression.Constant(context), currentTenantIdProperty);
        var condition = Expression.Equal(prop, tenantIdAccess);
        return Expression.Lambda(condition, param);
    }

    /// <summary>Combines two filter lambdas with a logical AND.</summary>
    public static LambdaExpression CombineFilters(
        LambdaExpression filter1, LambdaExpression? filter2)
    {
        if (filter2 == null) return filter1;
        var param = filter1.Parameters[0];
        var body = Expression.AndAlso(
            filter1.Body,
            Expression.Invoke(filter2, param));
        return Expression.Lambda(body, param);
    }

    // ---- Private helpers ----

    private static List<(string Sql, object[] Params)> CollectCascadeSoftDeleteOps(
        ChangeTracker changeTracker, DateTime now)
    {
        var ops = new List<(string Sql, object[] Params)>();

        var softDeletedEntries = changeTracker.Entries<IAuditableEntity>()
            .Where(e => e.State == EntityState.Modified
                && e.Entity is ISoftDeletable
                && ((ISoftDeletable)e.Entity).DeletedAt != null
                && e.Property(nameof(ISoftDeletable.DeletedAt)).IsModified)
            .ToList();

        foreach (var entry in softDeletedEntries)
        {
            var entityType = entry.Entity.GetType();
            var cascadeAttributes = entityType.GetCustomAttributes<CascadeSoftDeleteAttribute>();

            foreach (var attr in cascadeAttributes)
            {
                var childEntityType = changeTracker.Context.Model.FindEntityType(attr.ChildType);
                if (childEntityType == null) continue;

                var tableName = childEntityType.GetTableName();
                var schema = childEntityType.GetSchema();
                if (tableName == null) continue;

                var storeObject = StoreObjectIdentifier.Table(tableName, schema);

                var fkColumn = childEntityType.FindProperty(attr.ForeignKeyProperty)?.GetColumnName(storeObject);
                var deletedAtColumn = childEntityType.FindProperty(nameof(ISoftDeletable.DeletedAt))?.GetColumnName(storeObject);
                var updatedAtColumn = childEntityType.FindProperty(nameof(IAuditableEntity.UpdatedAt))?.GetColumnName(storeObject);

                if (fkColumn == null || deletedAtColumn == null || updatedAtColumn == null)
                    continue;

                var sql = string.Format(
                    "UPDATE \"{0}\" SET \"{1}\" = {{0}}, \"{2}\" = {{1}} WHERE \"{3}\" = {{2}} AND \"{1}\" IS NULL",
                    tableName, deletedAtColumn, updatedAtColumn, fkColumn);
                ops.Add((sql, new object[] { now, now, entry.Entity.Id }));
            }
        }

        return ops;
    }

    private static void TrySetUserField(EntityEntry entry, string propertyName, ICurrentUser currentUser)
    {
        var userId = currentUser.Id;
        if (string.IsNullOrEmpty(userId)) return;

        var prop = entry.Entity.GetType().GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance);
        if (prop == null || !prop.CanWrite) return;

        var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

        if (targetType == typeof(string))
        {
            prop.SetValue(entry.Entity, userId);
        }
        else if (targetType == typeof(Guid) && Guid.TryParse(userId, out var guidValue))
        {
            prop.SetValue(entry.Entity, guidValue);
        }
        else
        {
            try { prop.SetValue(entry.Entity, Convert.ChangeType(userId, targetType)); }
            catch { /* skip if conversion fails */ }
        }
    }

    private static void TryPreserveField(EntityEntry entry, string propertyName)
    {
        var efProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == propertyName);
        if (efProp != null)
            efProp.IsModified = false;
    }

    private static string SerializeCurrentValues(IEnumerable<PropertyEntry> props)
        => JsonSerializer.Serialize(
            props.ToDictionary(p => p.Metadata.Name, p => MaskIfHashed(p, p.CurrentValue)));

    private static string SerializeOriginalValues(IEnumerable<PropertyEntry> props)
        => JsonSerializer.Serialize(
            props.ToDictionary(p => p.Metadata.Name, p => MaskIfHashed(p, p.OriginalValue)));

    private static object? MaskIfHashed(PropertyEntry prop, object? value)
    {
        if (prop.Metadata.PropertyInfo?.GetCustomAttribute<HashedAttribute>() != null)
            return "***";
        return value;
    }
}
