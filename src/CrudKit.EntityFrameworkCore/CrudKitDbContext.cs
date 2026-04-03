using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Concurrency;
using CrudKit.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace CrudKit.EntityFrameworkCore;

/// <summary>
/// Abstract base DbContext. Inherit from this, define your DbSets, done.
/// Cross-cutting concerns handled automatically:
/// - IEntity          → Id generation (Guid), CreatedAt/UpdatedAt (UTC)
/// - ISoftDeletable   → DELETE intercepted → soft delete, global query filter
/// - IMultiTenant     → global tenant filter, TenantId auto-set on Create
/// - IConcurrent      → EF concurrency token
/// - IAuditable       → audit log written on Create/Update/Delete
/// - Enum properties  → stored as strings
/// - [Unique]         → unique index (partial if ISoftDeletable)
/// </summary>
public abstract class CrudKitDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;

    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<SequenceEntry> Sequences => Set<SequenceEntry>();

    protected CrudKitDbContext(DbContextOptions options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
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

            // ---- IEntity: generate GUID-based string Id before tracking ----
            // HasSentinelValue("") tells EF Core that empty string means "not yet set",
            // so it invokes the value generator (StringGuidValueGenerator) at track time.
            // This prevents duplicate-key conflicts when multiple new entities with
            // Id = "" are added together via AddRange.
            if (isEntity)
            {
                modelBuilder.Entity(clrType)
                    .Property<string>(nameof(IEntity.Id))
                    .ValueGeneratedOnAdd()
                    .HasSentinel(string.Empty)
                    .HasValueGenerator<StringGuidValueGenerator>();
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
            // IsConcurrencyToken works on all providers (including SQLite).
            // For SQL Server users who want true DB-generated rowversion,
            // reconfigure in OnModelCreatingCustom.
            if (isConcurrent)
            {
                modelBuilder.Entity(clrType)
                    .Property(nameof(IConcurrent.RowVersion))
                    .IsConcurrencyToken();
            }

            // ---- Enum properties → stored as strings ----
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

            // ---- [Unique] attribute → unique index ----
            foreach (var prop in clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetCustomAttribute<UniqueAttribute>() == null) continue;

                var indexBuilder = modelBuilder.Entity(clrType)
                    .HasIndex(prop.Name)
                    .IsUnique();

                if (isSoftDeletable)
                    indexBuilder.HasFilter("deleted_at IS NULL");
            }
        }

        // CrudKit internal tables
        modelBuilder.Entity<AuditLogEntry>(b =>
        {
            b.ToTable("__crud_audit_logs");
            b.HasIndex(e => new { e.EntityType, e.EntityId });
            b.HasIndex(e => e.Timestamp);
        });

        modelBuilder.Entity<SequenceEntry>(b =>
        {
            b.ToTable("__crud_sequences");
            b.HasIndex(e => new { e.EntityType, e.TenantId, e.Year }).IsUnique();
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
        BeforeSaveChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken ct = default)
    {
        BeforeSaveChanges();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    private void BeforeSaveChanges()
    {
        // Step 1: Generate IDs for new entities first (needed by audit log)
        foreach (var entry in ChangeTracker.Entries<IEntity>()
            .Where(e => e.State == EntityState.Added).ToList())
        {
            if (string.IsNullOrEmpty(entry.Entity.Id))
                entry.Entity.Id = Guid.NewGuid().ToString();
        }

        // Step 2: Write audit logs (now IDs are available for new entities,
        // and Deleted state is still intact for soft-delete detection)
        WriteAuditLogs();

        // Step 3: Set remaining fields and handle soft-delete conversion
        foreach (var entry in ChangeTracker.Entries<IEntity>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // Id already set in Step 1
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    if (entry.Entity is IMultiTenant mt && _currentUser.TenantId != null)
                        mt.TenantId = _currentUser.TenantId;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    entry.Property(nameof(IEntity.CreatedAt)).IsModified = false;
                    break;

                case EntityState.Deleted:
                    if (entry.Entity is ISoftDeletable sd)
                    {
                        entry.State = EntityState.Modified;
                        sd.DeletedAt = DateTime.UtcNow;
                        entry.Entity.UpdatedAt = DateTime.UtcNow;
                    }
                    break;
            }
        }
    }

    private void WriteAuditLogs()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditable
                && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (entries.Count == 0) return;

        var auditEntries = new List<AuditLogEntry>();
        foreach (var entry in entries)
        {
            var log = new AuditLogEntry
            {
                EntityType = entry.Entity.GetType().Name,
                EntityId = (entry.Entity as IEntity)?.Id ?? string.Empty,
                UserId = _currentUser.Id,
                Timestamp = DateTime.UtcNow,
            };

            switch (entry.State)
            {
                case EntityState.Added:
                    log.Action = "Create";
                    log.NewValues = SerializeCurrentValues(entry.Properties);
                    break;

                case EntityState.Modified:
                    var modified = entry.Properties.Where(p => p.IsModified).ToList();
                    log.Action = "Update";
                    log.OldValues = SerializeOriginalValues(modified);
                    log.NewValues = SerializeCurrentValues(modified);
                    log.ChangedFields = JsonSerializer.Serialize(
                        modified.Select(p => p.Metadata.Name));
                    break;

                case EntityState.Deleted:
                    log.Action = "Delete";
                    log.OldValues = SerializeCurrentValues(entry.Properties);
                    break;
            }

            auditEntries.Add(log);
        }

        AuditLogs.AddRange(auditEntries);
    }

    // ---- Runtime tenant value used by EF Core global filter ----
    // EF Core captures a reference to the DbContext instance, so this property
    // is evaluated fresh on every query.
    private string? CurrentTenantId => _currentUser.TenantId;

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
            props.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));

    private static string SerializeOriginalValues(IEnumerable<PropertyEntry> props)
        => JsonSerializer.Serialize(
            props.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue));
}

/// <summary>
/// Generates a new GUID string for string primary keys when the value is null or empty.
/// Assigned to IEntity.Id via ValueGeneratedOnAdd so that EF Core assigns the key
/// before tracking, preventing duplicate-key conflicts on bulk Add operations.
/// </summary>
internal sealed class StringGuidValueGenerator : ValueGenerator<string>
{
    public override bool GeneratesTemporaryValues => false;

    public override string Next(EntityEntry entry)
    {
        // Preserve an Id that was already set by the caller.
        if (entry.Entity is IEntity entity && !string.IsNullOrEmpty(entity.Id))
            return entity.Id;

        return Guid.NewGuid().ToString();
    }
}
