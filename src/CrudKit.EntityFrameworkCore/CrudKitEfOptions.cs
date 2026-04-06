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
}
