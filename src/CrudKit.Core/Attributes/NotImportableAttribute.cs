namespace CrudKit.Core.Attributes;

/// <summary>
/// Excludes a property from import mapping. The column is ignored during CSV/XLSX import.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class NotImportableAttribute : Attribute { }
