namespace CrudKit.Core.Attributes;

/// <summary>
/// Excludes a property from export output (CSV/XLSX).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class NotExportableAttribute : Attribute { }
