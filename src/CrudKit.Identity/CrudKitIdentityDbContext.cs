using System.Reflection;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CrudKit.Identity;

// ============================================================
// 1-param overload: most common usage
// ============================================================

/// <summary>
/// CrudKit-aware IdentityDbContext with a single TUser type parameter.
/// Provides all CrudKit cross-cutting concerns (timestamps, soft delete, tenant,
/// concurrency, audit, enum-as-string, unique indexes) alongside ASP.NET Identity tables.
/// </summary>
public abstract class CrudKitIdentityDbContext<TUser>
    : IdentityDbContext<TUser>, ICrudKitDbContext
    where TUser : IdentityUser
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly CrudKitEfOptions? _efOptions;
    private readonly ITenantContext? _tenantContext;
    private readonly IAuditWriter? _auditWriter;

    /// <summary>
    /// When true, SaveChanges skips audit entry collection.
    /// Used internally by DbAuditWriter to prevent recursive auditing.
    /// </summary>
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
        base.OnModelCreating(builder); // Register Identity tables first

        var currentTenantIdProperty = GetType()
            .GetProperty(nameof(CurrentTenantId), BindingFlags.Public | BindingFlags.Instance)!;

        CrudKitDbContextHelper.ConfigureModel(builder, this, _efOptions, currentTenantIdProperty);
        OnModelCreatingCustom(builder);
    }

    /// <summary>
    /// Override to add entity configurations and seed data.
    /// Do not call base.OnModelCreating here.
    /// </summary>
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
// 3-param overload: custom TUser, TRole, TKey
// ============================================================

/// <summary>
/// CrudKit-aware IdentityDbContext with custom TUser, TRole, and TKey type parameters.
/// Provides all CrudKit cross-cutting concerns alongside ASP.NET Identity tables.
/// </summary>
public abstract class CrudKitIdentityDbContext<TUser, TRole, TKey>
    : IdentityDbContext<TUser, TRole, TKey>, ICrudKitDbContext
    where TUser : IdentityUser<TKey>
    where TRole : IdentityRole<TKey>
    where TKey : IEquatable<TKey>
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly CrudKitEfOptions? _efOptions;
    private readonly ITenantContext? _tenantContext;
    private readonly IAuditWriter? _auditWriter;

    /// <summary>
    /// When true, SaveChanges skips audit entry collection.
    /// Used internally by DbAuditWriter to prevent recursive auditing.
    /// </summary>
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
        base.OnModelCreating(builder);

        var currentTenantIdProperty = GetType()
            .GetProperty(nameof(CurrentTenantId), BindingFlags.Public | BindingFlags.Instance)!;

        CrudKitDbContextHelper.ConfigureModel(builder, this, _efOptions, currentTenantIdProperty);
        OnModelCreatingCustom(builder);
    }

    /// <summary>
    /// Override to add entity configurations and seed data.
    /// Do not call base.OnModelCreating here.
    /// </summary>
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
// 8-param overload: full customization
// ============================================================

/// <summary>
/// CrudKit-aware IdentityDbContext with all eight ASP.NET Identity type parameters.
/// Use this overload when you need fully custom Identity entity types.
/// Provides all CrudKit cross-cutting concerns alongside ASP.NET Identity tables.
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

    /// <summary>
    /// When true, SaveChanges skips audit entry collection.
    /// Used internally by DbAuditWriter to prevent recursive auditing.
    /// </summary>
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
        base.OnModelCreating(builder);

        var currentTenantIdProperty = GetType()
            .GetProperty(nameof(CurrentTenantId), BindingFlags.Public | BindingFlags.Instance)!;

        CrudKitDbContextHelper.ConfigureModel(builder, this, _efOptions, currentTenantIdProperty);
        OnModelCreatingCustom(builder);
    }

    /// <summary>
    /// Override to add entity configurations and seed data.
    /// Do not call base.OnModelCreating here.
    /// </summary>
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
