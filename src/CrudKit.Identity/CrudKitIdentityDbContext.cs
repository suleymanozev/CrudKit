using System.Reflection;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.Identity;

// ============================================================
// 8-param overload: ALL logic lives here
// ============================================================

/// <summary>
/// CrudKit-aware IdentityDbContext with all eight ASP.NET Identity type parameters.
/// All CrudKit cross-cutting concerns (timestamps, soft delete, tenant, concurrency,
/// audit, enum-as-string, unique indexes) are provided alongside ASP.NET Identity tables.
/// </summary>
public abstract class CrudKitIdentityDbContext<TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken>
    : IdentityDbContext<TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken>,
      ICrudKitDbContext
    where TUser : IdentityUser<TKey>
    where TRole : IdentityRole<TKey>
    where TKey : IEquatable<TKey>
    where TUserClaim : IdentityUserClaim<TKey>
    where TUserRole : IdentityUserRole<TKey>
    where TUserLogin : IdentityUserLogin<TKey>
    where TRoleClaim : IdentityRoleClaim<TKey>
    where TUserToken : IdentityUserToken<TKey>
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly CrudKitEfOptions? _efOptions;
    private readonly ITenantContext? _tenantContext;
    private readonly IAuditWriter? _auditWriter;

    public bool IsAuditSave { get; set; }

    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<SequenceEntry> Sequences => Set<SequenceEntry>();

    protected CrudKitIdentityDbContext(
        DbContextOptions options,
        ICurrentUser currentUser,
        TimeProvider? timeProvider = null,
        CrudKitEfOptions? efOptions = null,
        ITenantContext? tenantContext = null,
        IAuditWriter? auditWriter = null)
        : base(options)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _efOptions = efOptions;
        _tenantContext = tenantContext;
        _auditWriter = auditWriter;
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // Identity tables first

        var currentTenantIdProperty = GetType()
            .GetProperty(nameof(CurrentTenantId), BindingFlags.Public | BindingFlags.Instance)!;

        CrudKitDbContextHelper.ConfigureModel(builder, this, _efOptions, currentTenantIdProperty);
        OnModelCreatingCustom(builder);
    }

    /// <summary>Override to add entity configurations and seed data.</summary>
    protected virtual void OnModelCreatingCustom(ModelBuilder modelBuilder) { }

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

    public string? CurrentTenantId => _tenantContext?.TenantId;
    public ICurrentUser CurrentUser => _currentUser;
    public ITenantContext? TenantCtx => _tenantContext;
}

// ============================================================
// 3-param overload: inherits from 8-param with default Identity types
// ============================================================

/// <summary>
/// CrudKit-aware IdentityDbContext with custom TUser, TRole, and TKey.
/// </summary>
public abstract class CrudKitIdentityDbContext<TUser, TRole, TKey>
    : CrudKitIdentityDbContext<TUser, TRole, TKey,
        IdentityUserClaim<TKey>, IdentityUserRole<TKey>, IdentityUserLogin<TKey>,
        IdentityRoleClaim<TKey>, IdentityUserToken<TKey>>
    where TUser : IdentityUser<TKey>
    where TRole : IdentityRole<TKey>
    where TKey : IEquatable<TKey>
{
    protected CrudKitIdentityDbContext(
        DbContextOptions options,
        ICurrentUser currentUser,
        TimeProvider? timeProvider = null,
        CrudKitEfOptions? efOptions = null,
        ITenantContext? tenantContext = null,
        IAuditWriter? auditWriter = null)
        : base(options, currentUser, timeProvider, efOptions, tenantContext, auditWriter) { }
}

// ============================================================
// 1-param overload: most common — inherits from 3-param with defaults
// ============================================================

/// <summary>
/// CrudKit-aware IdentityDbContext with a single TUser type parameter.
/// Uses string keys and default IdentityRole. Most common usage.
/// </summary>
public abstract class CrudKitIdentityDbContext<TUser>
    : CrudKitIdentityDbContext<TUser, IdentityRole, string>
    where TUser : IdentityUser
{
    protected CrudKitIdentityDbContext(
        DbContextOptions options,
        ICurrentUser currentUser,
        TimeProvider? timeProvider = null,
        CrudKitEfOptions? efOptions = null,
        ITenantContext? tenantContext = null,
        IAuditWriter? auditWriter = null)
        : base(options, currentUser, timeProvider, efOptions, tenantContext, auditWriter) { }
}
