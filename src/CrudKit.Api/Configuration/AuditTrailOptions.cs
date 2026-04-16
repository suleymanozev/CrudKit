namespace CrudKit.Api.Configuration;

/// <summary>
/// Fluent builder for audit trail configuration.
/// Returned by <see cref="CrudKitApiOptions.UseAuditTrail()"/> to allow chaining
/// audit-specific options that only make sense when audit trail is enabled.
/// </summary>
public class AuditTrailOptions
{
    private readonly CrudKitApiOptions _parent;

    internal AuditTrailOptions(CrudKitApiOptions parent) => _parent = parent;

    /// <summary>
    /// Also log failed SaveChanges operations to the audit trail.
    /// Failed operations are recorded with action "FailedCreate", "FailedUpdate", "FailedDelete".
    /// Useful for security auditing and compliance.
    /// </summary>
    public AuditTrailOptions EnableAuditFailedOperations()
    {
        _parent.AuditFailedOperations = true;
        return this;
    }

    /// <summary>
    /// Store audit logs in a specific database schema.
    /// Only affects the __crud_audit_logs table location.
    /// </summary>
    public AuditTrailOptions UseSchema(string schema)
    {
        _parent.AuditSchema = schema;
        return this;
    }

    /// <summary>
    /// Store audit logs in a separate DbContext (different database).
    /// All modules will write audit entries to this centralized context.
    /// </summary>
    public AuditTrailOptions UseContext<TAuditContext>() where TAuditContext : class
    {
        _parent.AuditContextType = typeof(TAuditContext);
        return this;
    }
}
