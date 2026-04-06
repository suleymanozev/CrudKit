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

    /// <summary>
    /// CRUD011: A naming pattern in [assembly: CrudKit] is set to an empty string.
    /// </summary>
    public static readonly DiagnosticDescriptor NamingPatternEmpty = new DiagnosticDescriptor(
        id: "CRUD011",
        title: "CrudKit naming pattern is empty",
        messageFormat: "'{0}' naming pattern in [assembly: CrudKit] cannot be empty",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://crudkit.dev/diagnostics/CRUD011");

    /// <summary>
    /// CRUD012: A naming pattern in [assembly: CrudKit] does not contain the {Name} placeholder.
    /// </summary>
    public static readonly DiagnosticDescriptor NamingPatternMissingPlaceholder = new DiagnosticDescriptor(
        id: "CRUD012",
        title: "CrudKit naming pattern missing {Name} placeholder",
        messageFormat: "'{0}' naming pattern '{1}' in [assembly: CrudKit] must contain {{Name}} placeholder",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://crudkit.dev/diagnostics/CRUD012");
}
