namespace CrudKit.Core.Attributes;

/// <summary>
/// When applied to an entity class, a GET /export endpoint is generated
/// supporting CSV and XLSX formats. All public properties are included
/// unless marked with <see cref="NotExportableAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ExportableAttribute : Attribute { }
