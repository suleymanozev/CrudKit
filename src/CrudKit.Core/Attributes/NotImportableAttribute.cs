namespace CrudKit.Core.Attributes;

/// <summary>
/// When applied to a class, disables the import endpoint for this entity even when
/// global UseImport() is enabled. When applied to a property, excludes it from import mapping.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public class NotImportableAttribute : Attribute { }
