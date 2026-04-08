using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CrudKit.EntityFrameworkCore;

/// <summary>
/// Marker interface for CrudKit-aware DbContext types.
/// Implemented by both <see cref="CrudKitDbContext"/> and <c>CrudKitIdentityDbContext</c>.
/// Enables EfRepo and related infrastructure to work with both base-class hierarchies.
/// </summary>
public interface ICrudKitDbContext
{
    /// <summary>The current tenant identifier, or null when not in a multi-tenant context.</summary>
    string? CurrentTenantId { get; }

    /// <summary>When false, the global soft-delete query filter is bypassed for this scope.</summary>
    bool IsSoftDeleteFilterEnabled { get; }

    /// <summary>When false, the global tenant query filter is bypassed for this scope.</summary>
    bool IsTenantFilterEnabled { get; }

    /// <summary>The current user for user-tracking fields and audit log.</summary>
    ICurrentUser CurrentUser { get; }

    /// <summary>The current tenant context, if any.</summary>
    ITenantContext? TenantCtx { get; }

    /// <summary>
    /// When true, SaveChanges skips audit entry collection.
    /// Used internally by DbAuditWriter to prevent recursive auditing.
    /// </summary>
    bool IsAuditSave { get; set; }

    DbSet<AuditLogEntry> AuditLogs { get; }

    DatabaseFacade Database { get; }
    DbSet<TEntity> Set<TEntity>() where TEntity : class;
    ChangeTracker ChangeTracker { get; }
    Microsoft.EntityFrameworkCore.Metadata.IModel Model { get; }

    int SaveChanges();
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
