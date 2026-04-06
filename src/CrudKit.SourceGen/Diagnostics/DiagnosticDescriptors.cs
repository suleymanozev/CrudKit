using Microsoft.CodeAnalysis;

namespace CrudKit.SourceGen.Diagnostics;

/// <summary>
/// Compile-time diagnostic descriptors emitted by CrudKitSourceGenerator.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "CrudKit";

    /// <summary>
    /// CRUD001: A [CrudEntity] class does not implement IEntity.
    /// Without IEntity the generated code cannot compile — hard error.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingIEntity = new DiagnosticDescriptor(
        id: "CRUD001",
        title: "Entity does not implement IEntity",
        messageFormat: "'{0}' is decorated with [CrudEntity] but does not implement CrudKit.Core.Interfaces.IEntity. Implement IEntity to use CrudKit source generation.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://crudkit.dev/diagnostics/CRUD001");

    /// <summary>
    /// CRUD003: MultiTenant=true but the entity does not implement IMultiTenant.
    /// </summary>
    public static readonly DiagnosticDescriptor MultiTenantWithoutIMultiTenant = new DiagnosticDescriptor(
        id: "CRUD003",
        title: "MultiTenant enabled but IMultiTenant not implemented",
        messageFormat: "'{0}' has MultiTenant=true but does not implement CrudKit.Core.Interfaces.IMultiTenant. Add IMultiTenant or set MultiTenant=false.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://crudkit.dev/diagnostics/CRUD003");

    /// <summary>
    /// CRUD010: CrudEntityAttribute.Table is explicitly set to an empty string.
    /// </summary>
    public static readonly DiagnosticDescriptor EmptyTableName = new DiagnosticDescriptor(
        id: "CRUD010",
        title: "CrudEntity Table name is empty",
        messageFormat: "'{0}' has an empty Table name in [CrudEntity]. Provide a non-empty table name or omit it to use the default (entity name + 's').",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://crudkit.dev/diagnostics/CRUD010");
}
