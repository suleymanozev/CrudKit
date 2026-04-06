namespace CrudKit.Core.Attributes;

/// <summary>
/// Excludes a property from audit trail logging.
/// Use on sensitive fields like passwords, security stamps, or tokens
/// that should not appear in __crud_audit_logs.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class AuditIgnoreAttribute : Attribute { }
