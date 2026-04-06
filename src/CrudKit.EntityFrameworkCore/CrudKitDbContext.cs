using System.Reflection;
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

    /// <summary>
    /// When true, SaveChanges skips audit entry collection. Used internally by
    /// <see cref="Auditing.DbAuditWriter"/> to prevent recursive auditing.
    /// </summary>
    public bool IsAuditSave { get; set; }

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

        var currentTenantIdProperty = GetType()
            .GetProperty(nameof(CurrentTenantId), BindingFlags.Public | BindingFlags.Instance)!;

        CrudKitDbContextHelper.ConfigureModel(modelBuilder, this, _efOptions, currentTenantIdProperty);

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

        var auditEntries = _auditWriter != null
            ? CrudKitDbContextHelper.CollectAuditEntries(ChangeTracker, _currentUser, _timeProvider, _efOptions)
            : [];
        var cascadeOps = CrudKitDbContextHelper.ProcessBeforeSave(ChangeTracker, _currentUser, _tenantContext, _timeProvider);

        try
        {
            var result = base.SaveChanges(acceptAllChangesOnSuccess);
            CrudKitDbContextHelper.ExecuteCascadeOps(Database, cascadeOps);

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

        var auditEntries = _auditWriter != null
            ? CrudKitDbContextHelper.CollectAuditEntries(ChangeTracker, _currentUser, _timeProvider, _efOptions)
            : [];
        var cascadeOps = CrudKitDbContextHelper.ProcessBeforeSave(ChangeTracker, _currentUser, _tenantContext, _timeProvider);

        try
        {
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
            CrudKitDbContextHelper.ExecuteCascadeOps(Database, cascadeOps);

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

    // ---- Runtime tenant value used by EF Core global filter ----
    public string? CurrentTenantId => _tenantContext?.TenantId;

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
