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
    public CrudKitApiOptions EnableAuditFailedOperations()
    {
        _parent.AuditFailedOperations = true;
        return _parent;
    }
}
