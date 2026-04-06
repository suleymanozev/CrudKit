using System.Reflection;

namespace CrudKit.Api.Configuration;

public class CrudKitApiOptions
{
    public int DefaultPageSize { get; set; } = 20;
    public int MaxPageSize { get; set; } = 100;
    public string ApiPrefix { get; set; } = "/api";
    public int BulkLimit { get; set; } = 10_000;
    public Assembly? ScanModulesFromAssembly { get; set; }
    public bool EnableIdempotency { get; set; }

    /// <summary>
    /// When true, entities decorated with [Audited] have their changes logged
    /// to the __crud_audit_logs table. Default: false.
    /// </summary>
    public bool AuditTrailEnabled { get; set; }

    /// <summary>
    /// Enables audit trail logging for entities decorated with [Audited].
    /// Changes are written to the __crud_audit_logs table on Create, Update, and Delete.
    /// </summary>
    public CrudKitApiOptions UseAuditTrail()
    {
        AuditTrailEnabled = true;
        return this;
    }
}
