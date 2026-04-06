using CrudKit.Core.Models;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Writes audit trail entries. Default implementation writes to the __crud_audit_logs table.
/// Replace with a custom implementation for Elasticsearch, separate DB, file storage, etc.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct = default);
}
