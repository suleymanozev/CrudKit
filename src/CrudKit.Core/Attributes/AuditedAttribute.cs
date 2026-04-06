namespace CrudKit.Core.Attributes;

/// <summary>
/// When applied to an entity class, changes to this entity are logged to the audit trail
/// (__crud_audit_logs table). Requires UseAuditTrail() to be enabled in CrudKit options.
/// If UseAuditTrail() is not enabled, this attribute is silently ignored.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class AuditedAttribute : Attribute { }
