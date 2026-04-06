namespace CrudKit.Core.Attributes;

/// <summary>
/// Overrides global UseAuditTrail() for this entity — changes are not logged
/// even when audit trail is enabled globally.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class NotAuditedAttribute : Attribute { }
