namespace CrudKit.Core.Attributes;

/// <summary>
/// PUT isteğinde bu alan gönderilemez — WorkflowProtectionFilter engeller.
/// Workflow veya sistem tarafından yönetilen alanlar için kullanılır.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ProtectedAttribute : Attribute { }
