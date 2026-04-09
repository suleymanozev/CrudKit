using System.Reflection;
using CrudKit.Core.Events;
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
    private readonly IDataFilter<ISoftDeletable>? _softDeleteFilter;
    private readonly IDataFilter<IMultiTenant>? _tenantFilter;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    public bool IsAuditSave { get; set; }

    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();

    protected CrudKitIdentityDbContext(
        DbContextOptions options,
        ICurrentUser currentUser,
        TimeProvider? timeProvider = null,
        CrudKitEfOptions? efOptions = null,
        ITenantContext? tenantContext = null,
        IAuditWriter? auditWriter = null,
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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // Identity tables first

        var currentTenantIdProperty = GetType()
            .GetProperty(nameof(CurrentTenantId), BindingFlags.Public | BindingFlags.Instance)!;

        var isSoftDeleteFilterEnabledProperty = GetType()
            .GetProperty(nameof(IsSoftDeleteFilterEnabled), BindingFlags.Public | BindingFlags.Instance)!;

        var isTenantFilterEnabledProperty = GetType()
            .GetProperty(nameof(IsTenantFilterEnabled), BindingFlags.Public | BindingFlags.Instance)!;

        CrudKitDbContextHelper.ConfigureModel(builder, this, _efOptions,
            currentTenantIdProperty, isSoftDeleteFilterEnabledProperty, isTenantFilterEnabledProperty);
        OnModelCreatingCustom(builder);

        CrudKitDbContextHelper.ValidateSchemaSupport(builder, this);
    }

    /// <summary>Override to add entity configurations and seed data.</summary>
    protected virtual void OnModelCreatingCustom(ModelBuilder modelBuilder) { }

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
        IAuditWriter? auditWriter = null,
        IDataFilter<ISoftDeletable>? softDeleteFilter = null,
        IDataFilter<IMultiTenant>? tenantFilter = null,
        IDomainEventDispatcher? domainEventDispatcher = null)
        : base(options, currentUser, timeProvider, efOptions, tenantContext, auditWriter, softDeleteFilter, tenantFilter, domainEventDispatcher) { }
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
        IAuditWriter? auditWriter = null,
        IDataFilter<ISoftDeletable>? softDeleteFilter = null,
        IDataFilter<IMultiTenant>? tenantFilter = null,
        IDomainEventDispatcher? domainEventDispatcher = null)
        : base(options, currentUser, timeProvider, efOptions, tenantContext, auditWriter, softDeleteFilter, tenantFilter, domainEventDispatcher) { }
}
