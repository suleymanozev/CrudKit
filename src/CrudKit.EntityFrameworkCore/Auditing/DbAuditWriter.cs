using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.EntityFrameworkCore.Auditing;

/// <summary>
/// Default <see cref="IAuditWriter"/> that writes audit entries to the __crud_audit_logs table.
/// Resolves the target DbContext via <see cref="AuditDbContextAccessor"/> — this allows routing
/// audit writes to a dedicated centralized context when UseContext&lt;T&gt;() is configured.
/// Uses an internal flag to prevent recursive audit collection when saving audit records.
/// </summary>
public class DbAuditWriter : IAuditWriter
{
    private readonly IServiceProvider _services;
    private readonly AuditDbContextAccessor _accessor;

    public DbAuditWriter(IServiceProvider services, AuditDbContextAccessor accessor)
    {
        _services = services;
        _accessor = accessor;
    }

    public async Task WriteAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0) return;

        var db = _accessor.Resolve(_services);

        foreach (var entry in entries)
        {
            db.AuditLogs.Add(new AuditLogEntry
            {
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                Action = entry.Action,
                UserId = entry.UserId,
                CorrelationId = entry.CorrelationId,
                Timestamp = entry.Timestamp,
                OldValues = entry.OldValues,
                NewValues = entry.NewValues,
                ChangedFields = entry.ChangedFields,
            });
        }

        // Save with the internal audit flag set so SaveChanges does not
        // recursively collect audit entries for these audit log rows.
        db.IsAuditSave = true;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.IsAuditSave = false;
        }
    }
}
