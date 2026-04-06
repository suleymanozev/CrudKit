using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Concurrency;
using CrudKit.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace CrudKit.EntityFrameworkCore;

/// <summary>
/// Abstract base DbContext. Inherit from this, define your DbSets, done.
/// Cross-cutting concerns handled automatically:
/// - IEntity           → Guid Id generation
/// - IAuditableEntity  → CreatedAt/UpdatedAt (UTC)
/// - ISoftDeletable    → DELETE intercepted → soft delete, global query filter
/// - IMultiTenant      → global tenant filter, TenantId auto-set on Create
/// - IConcurrent       → EF concurrency token
/// - [Audited]         → audit log written on Create/Update/Delete (requires UseAuditTrail())
/// - Enum properties   → stored as strings (opt-in via UseEnumAsString())
/// - [Unique]          → unique index (partial if ISoftDeletable)
/// </summary>
public abstract class CrudKitDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly CrudKitEfOptions? _efOptions;
    private readonly ITenantContext? _tenantContext;
    private readonly IAuditWriter? _auditWriter;

    /// <summary>
    /// When true, SaveChanges skips audit entry collection. Used internally by
    /// <see cref="Auditing.DbAuditWriter"/> to prevent recursive auditing.
    /// </summary>
    internal bool IsAuditSave { get; set; }

    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<SequenceEntry> Sequences => Set<SequenceEntry>();

    protected CrudKitDbContext(DbContextOptions options, ICurrentUser currentUser,
        TimeProvider? timeProvider = null, CrudKitEfOptions? efOptions = null,
        ITenantContext? tenantContext = null, IAuditWriter? auditWriter = null)
        : base(options)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _efOptions = efOptions;
        _tenantContext = tenantContext;
        _auditWriter = auditWriter;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
                var tenantFilter = BuildTenantFilter(clrType);
                var softFilter = isSoftDeletable ? BuildSoftDeleteFilter(clrType) : null;
                modelBuilder.Entity(clrType).HasQueryFilter(
                    CombineFilters(tenantFilter, softFilter));
            }

            // ---- Optimistic concurrency ----
            if (isConcurrent)
            {
                modelBuilder.Entity(clrType)
                    .Property(nameof(IConcurrent.RowVersion))
                    .IsConcurrencyToken();
            }

            // ---- Enum properties → stored as strings (opt-in via UseEnumAsString()) ----
            if (_efOptions?.EnumAsStringEnabled == true)
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

        if (_efOptions?.AuditTrailEnabled == true || anyEntityAudited)
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

        OnModelCreatingCustom(modelBuilder);
    }

    /// <summary>
    /// Override to add entity configurations and seed data.
    /// Do not call base.OnModelCreating here.
    /// </summary>
    protected virtual void OnModelCreatingCustom(ModelBuilder modelBuilder) { }

    // ---- SaveChanges overrides ----

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (IsAuditSave)
            return base.SaveChanges(acceptAllChangesOnSuccess);

        var auditEntries = CollectAuditEntries();
        var cascadeOps = BeforeSaveChanges();

        try
        {
            var result = base.SaveChanges(acceptAllChangesOnSuccess);
            ExecuteCascadeOps(cascadeOps);

            if (auditEntries.Count > 0 && _auditWriter != null)
                _auditWriter.WriteAsync(auditEntries, CancellationToken.None).GetAwaiter().GetResult();

            return result;
        }
        catch when (_efOptions?.AuditFailedOperations == true && auditEntries.Count > 0 && _auditWriter != null)
        {
            foreach (var e in auditEntries) e.Action = $"Failed{e.Action}";
            _auditWriter.WriteAsync(auditEntries, CancellationToken.None).GetAwaiter().GetResult();
            throw;
        }
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken ct = default)
    {
        if (IsAuditSave)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);

        var auditEntries = CollectAuditEntries();
        var cascadeOps = BeforeSaveChanges();

        try
        {
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
            ExecuteCascadeOps(cascadeOps);

            if (auditEntries.Count > 0 && _auditWriter != null)
                await _auditWriter.WriteAsync(auditEntries, ct);

            return result;
        }
        catch when (_efOptions?.AuditFailedOperations == true && auditEntries.Count > 0 && _auditWriter != null)
        {
            foreach (var e in auditEntries) e.Action = $"Failed{e.Action}";
            await _auditWriter.WriteAsync(auditEntries, ct);
            throw;
        }
    }

    private List<(string Sql, object[] Params)> BeforeSaveChanges()
    {
        // Capture current UTC time once to ensure consistency across all timestamps
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Step 1: Generate IDs for new entities first (needed by audit log)
        foreach (var entry in ChangeTracker.Entries<IEntity>()
            .Where(e => e.State == EntityState.Added).ToList())
        {
            if (entry.Entity.Id == Guid.Empty)
                entry.Entity.Id = Guid.NewGuid();
        }

        // Step 2: Set remaining fields and handle soft-delete conversion
        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // Id already set in Step 1
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    if (entry.Entity is IMultiTenant mt && _tenantContext?.TenantId != null)
                        mt.TenantId = _tenantContext.TenantId;
                    // User tracking — set CreatedBy and UpdatedBy
                    TrySetUserField(entry, "CreatedById");
                    TrySetUserField(entry, "UpdatedById");
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
                    // User tracking — set UpdatedBy only
                    TrySetUserField(entry, "UpdatedById");
                    // Prevent overwriting CreatedById on update
                    TryPreserveField(entry, "CreatedById");
                    break;

                case EntityState.Deleted:
                    if (entry.Entity is ISoftDeletable sd)
                    {
                        entry.State = EntityState.Modified;
                        sd.DeletedAt = now;
                        entry.Entity.UpdatedAt = now;
                        // User tracking — set DeletedBy
                        TrySetUserField(entry, "DeletedById");
                    }
                    break;
            }
        }

        // Step 4: Collect cascade soft-delete operations (executed after base.SaveChanges)
        return CollectCascadeSoftDeleteOps(now);
    }

    /// <summary>
    /// Finds entities that were just soft-deleted in this SaveChanges batch and
    /// collects cascade soft-delete SQL operations to run after the main save completes.
    /// Uses raw SQL to avoid loading children into the change tracker.
    /// </summary>
    private List<(string Sql, object[] Params)> CollectCascadeSoftDeleteOps(DateTime now)
    {
        var ops = new List<(string Sql, object[] Params)>();

        var softDeletedEntries = ChangeTracker.Entries<IAuditableEntity>()
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
                var childEntityType = Model.FindEntityType(attr.ChildType);
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

    /// <summary>
    /// Executes collected cascade soft-delete SQL operations after the main save.
    /// </summary>
    private void ExecuteCascadeOps(List<(string Sql, object[] Params)> ops)
    {
        foreach (var (sql, parameters) in ops)
        {
            Database.ExecuteSqlRaw(sql, parameters[0], parameters[1], parameters[2]);
        }
    }

    /// <summary>
    /// Scans the ChangeTracker and builds a list of <see cref="AuditEntry"/> records
    /// for entities that should be audited. Must be called BEFORE base.SaveChanges
    /// so that Added/Modified/Deleted states are still available.
    /// </summary>
    private List<AuditEntry> CollectAuditEntries()
    {
        if (_auditWriter == null) return [];

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var globalAuditEnabled = _efOptions?.AuditTrailEnabled == true;

        var entries = ChangeTracker.Entries()
            .Where(e =>
            {
                var type = e.Entity.GetType();
                // [NotAudited] = force disable, even when global is on
                if (type.GetCustomAttribute<NotAuditedAttribute>() != null) return false;
                // [Audited] = force enable, even when global is off
                if (type.GetCustomAttribute<AuditedAttribute>() != null) return true;
                // No attribute: fall back to global flag
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
                UserId = _currentUser.Id,
                Timestamp = now,
            };

            // Filter out [AuditIgnore] properties
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

    // ---- Runtime tenant value used by EF Core global filter ----
    internal string? CurrentTenantId => _tenantContext?.TenantId;

    // Exposed for EfRepo so it can build AppContext without taking ICurrentUser as a separate dependency.
    internal ICurrentUser CurrentUser => _currentUser;

    // Exposed for EfRepo so it can build AppContext with tenant information.
    internal ITenantContext? TenantCtx => _tenantContext;

    // ---- Filter expression builders ----

    private static LambdaExpression BuildSoftDeleteFilter(Type entityType)
    {
        var param = Expression.Parameter(entityType, "e");
        var prop = Expression.Property(param, nameof(ISoftDeletable.DeletedAt));
        var condition = Expression.Equal(prop, Expression.Constant(null, typeof(DateTime?)));
        return Expression.Lambda(condition, param);
    }

    private LambdaExpression BuildTenantFilter(Type entityType)
    {
        var param = Expression.Parameter(entityType, "e");
        var prop = Expression.Property(param, nameof(IMultiTenant.TenantId));
        var currentTenantProp = typeof(CrudKitDbContext)
            .GetProperty(nameof(CurrentTenantId), BindingFlags.NonPublic | BindingFlags.Instance)!;
        var tenantIdAccess = Expression.Property(Expression.Constant(this), currentTenantProp);
        var condition = Expression.Equal(prop, tenantIdAccess);
        return Expression.Lambda(condition, param);
    }

    private static LambdaExpression CombineFilters(
        LambdaExpression filter1, LambdaExpression? filter2)
    {
        if (filter2 == null) return filter1;
        var param = filter1.Parameters[0];
        var body = Expression.AndAlso(
            filter1.Body,
            Expression.Invoke(filter2, param));
        return Expression.Lambda(body, param);
    }

    // ---- Serialization helpers for audit log ----

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

    // ---- User tracking helpers ----

    /// <summary>
    /// Sets a user tracking field (CreatedById, UpdatedById, DeletedById) from ICurrentUser.Id.
    /// Handles type conversion: if the property is Guid, parses the string; if string, assigns directly.
    /// Silently skips if the property does not exist or the user is not authenticated.
    /// </summary>
    private void TrySetUserField(EntityEntry entry, string propertyName)
    {
        var userId = _currentUser.Id;
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

    /// <summary>
    /// Prevents a field from being overwritten on update (e.g. CreatedById should not change).
    /// </summary>
    private static void TryPreserveField(EntityEntry entry, string propertyName)
    {
        var efProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == propertyName);
        if (efProp != null)
            efProp.IsModified = false;
    }
}

/// <summary>
/// Generates a new Guid for primary keys when the value is empty.
/// Assigned to IEntity.Id via ValueGeneratedOnAdd so that EF Core assigns the key
/// before tracking, preventing duplicate-key conflicts on bulk Add operations.
/// </summary>
internal sealed class GuidValueGenerator : ValueGenerator<Guid>
{
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry)
    {
        // Preserve an Id that was already set by the caller.
        if (entry.Entity is IEntity entity && entity.Id != Guid.Empty)
            return entity.Id;

        return Guid.NewGuid();
    }
}
