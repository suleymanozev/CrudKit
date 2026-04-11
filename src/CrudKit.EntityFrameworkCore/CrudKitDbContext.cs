using System.Reflection;
using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
public abstract class CrudKitDbContext : DbContext, ICrudKitDbContext
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly CrudKitEfOptions? _efOptions;
    private readonly ITenantContext? _tenantContext;
    private readonly IAuditWriter? _auditWriter;
    private readonly IDataFilter<ISoftDeletable>? _softDeleteFilter;
    private readonly IDataFilter<IMultiTenant>? _tenantFilter;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    /// <summary>
    /// When true, SaveChanges skips audit entry collection. Used internally by
    /// <see cref="Auditing.DbAuditWriter"/> to prevent recursive auditing.
    /// </summary>
    public bool IsAuditSave { get; set; }

    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();

    protected CrudKitDbContext(DbContextOptions options, ICurrentUser currentUser,
        TimeProvider? timeProvider = null, CrudKitEfOptions? efOptions = null,
        ITenantContext? tenantContext = null, IAuditWriter? auditWriter = null,
        IDataFilter<ISoftDeletable>? softDeleteFilter = null,
        IDataFilter<IMultiTenant>? tenantFilter = null,
        IDomainEventDispatcher? domainEventDispatcher = null)
        : base(options)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _efOptions = efOptions;
        _tenantContext = tenantContext;
        _auditWriter = auditWriter;
        _softDeleteFilter = softDeleteFilter;
        _tenantFilter = tenantFilter;
        _domainEventDispatcher = domainEventDispatcher;
    }

    /// <summary>
    /// Simplified constructor using bundled dependencies.
    /// Preferred over the multi-parameter constructor for cleaner subclass definitions.
    /// </summary>
    protected CrudKitDbContext(
        DbContextOptions options,
        CrudKitDbContextDependencies deps)
        : this(options, deps.CurrentUser, deps.TimeProvider, deps.EfOptions,
               deps.TenantContext, deps.AuditWriter, deps.SoftDeleteFilter,
               deps.TenantFilter, deps.DomainEventDispatcher) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var currentTenantIdProperty = GetType()
            .GetProperty(nameof(CurrentTenantId), BindingFlags.Public | BindingFlags.Instance)!;

        var isSoftDeleteFilterEnabledProperty = GetType()
            .GetProperty(nameof(IsSoftDeleteFilterEnabled), BindingFlags.Public | BindingFlags.Instance)!;

        var isTenantFilterEnabledProperty = GetType()
            .GetProperty(nameof(IsTenantFilterEnabled), BindingFlags.Public | BindingFlags.Instance)!;

        CrudKitDbContextHelper.ConfigureModel(modelBuilder, this, _efOptions,
            currentTenantIdProperty, isSoftDeleteFilterEnabledProperty, isTenantFilterEnabledProperty);

        OnModelCreatingCustom(modelBuilder);

        CrudKitDbContextHelper.ValidateSchemaSupport(modelBuilder, this);
    }

    /// <summary>
    /// Override to add entity configurations and seed data.
    /// Do not call base.OnModelCreating here.
    /// </summary>
    protected virtual void OnModelCreatingCustom(ModelBuilder modelBuilder) { }

    /// <summary>
    /// Sets the default schema for this module's entities.
    /// On providers that don't support schemas (e.g. MySQL), this call is silently skipped.
    /// Use this instead of modelBuilder.HasDefaultSchema() for cross-provider compatibility.
    /// </summary>
    protected void UseModuleSchema(ModelBuilder modelBuilder, string schemaName)
    {
        if (Dialect.DialectDetector.Detect(this).SupportsSchemas)
            modelBuilder.HasDefaultSchema(schemaName);
    }

    // ---- SaveChanges overrides — delegated to helper ----

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => CrudKitDbContextHelper.SaveChanges(
            this, base.SaveChanges, acceptAllChangesOnSuccess,
            _currentUser, _tenantContext, _timeProvider, _efOptions, _auditWriter,
            _domainEventDispatcher);

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken ct = default)
        => CrudKitDbContextHelper.SaveChangesAsync(
            this, base.SaveChangesAsync, acceptAllChangesOnSuccess,
            _currentUser, _tenantContext, _timeProvider, _efOptions, _auditWriter, ct,
            _domainEventDispatcher);

    // ---- Runtime values used by EF Core global filters ----
    public string? CurrentTenantId => _tenantContext?.TenantId;

    /// <summary>
    /// Indicates whether the soft-delete query filter is currently active.
    /// Read by the EF Core filter expression per query via a captured property accessor.
    /// </summary>
    public bool IsSoftDeleteFilterEnabled => _softDeleteFilter?.IsEnabled ?? true;

    /// <summary>
    /// Indicates whether the tenant query filter is currently active.
    /// Read by the EF Core filter expression per query via a captured property accessor.
    /// </summary>
    public bool IsTenantFilterEnabled => _tenantFilter?.IsEnabled ?? true;

    // Exposed for EfRepo so it can build AppContext without taking ICurrentUser as a separate dependency.
    public ICurrentUser CurrentUser => _currentUser;

    // Exposed for EfRepo so it can build AppContext with tenant information.
    public ITenantContext? TenantCtx => _tenantContext;
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
