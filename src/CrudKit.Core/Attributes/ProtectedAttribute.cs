namespace CrudKit.Core.Attributes;

/// <summary>
/// This field cannot be set via PUT requests — WorkflowProtectionFilter blocks it.
/// Use for fields managed by the workflow engine or the system.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ProtectedAttribute : Attribute { }
