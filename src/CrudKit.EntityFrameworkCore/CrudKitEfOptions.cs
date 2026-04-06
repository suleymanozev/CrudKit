namespace CrudKit.EntityFrameworkCore;

/// <summary>
/// Runtime options for the CrudKit EF Core layer.
/// Registered as a singleton and injected into CrudKitDbContext.
/// </summary>
public class CrudKitEfOptions
{
    /// <summary>
    /// When true, entities decorated with [Audited] have their changes logged
    /// to the __crud_audit_logs table.
    /// </summary>
    public bool AuditTrailEnabled { get; set; }

    /// <summary>
    /// When true, all enum properties on entities are stored as strings in the database.
    /// Default: false (stored as integers).
    /// </summary>
    public bool EnumAsStringEnabled { get; set; }

    /// <summary>
    /// When true, failed SaveChanges operations are also logged to the audit trail
    /// with action prefixed as "Failed" (e.g. "FailedCreate", "FailedUpdate").
    /// Useful for security auditing and compliance. Default: false.
    /// </summary>
    public bool AuditFailedOperations { get; set; }
}
