namespace CrudKit.Core.Attributes;

/// <summary>
/// When applied to an entity class, a POST /import endpoint is generated
/// accepting CSV or XLSX uploads. All CreateDto-eligible properties are mapped
/// unless marked with <see cref="NotImportableAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ImportableAttribute : Attribute { }
