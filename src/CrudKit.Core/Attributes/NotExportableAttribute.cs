namespace CrudKit.Core.Attributes;

/// <summary>
/// When applied to a class, disables the export endpoint for this entity even when
/// global UseExport() is enabled. When applied to a property, excludes it from export output.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public class NotExportableAttribute : Attribute { }
