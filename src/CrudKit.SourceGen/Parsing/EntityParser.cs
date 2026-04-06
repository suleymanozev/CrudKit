using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using CrudKit.SourceGen.Models;

namespace CrudKit.SourceGen.Parsing;

/// <summary>
/// Extracts <see cref="EntityMetadata"/> from a Roslyn <see cref="INamedTypeSymbol"/>
/// decorated with [CrudEntity].
/// </summary>
internal static class EntityParser
{
    // System fields automatically excluded from generated DTOs.
    private static readonly HashSet<string> SystemFields = new HashSet<string>(System.StringComparer.Ordinal)
    {
        "Id", "CreatedAt", "UpdatedAt", "DeletedAt", "TenantId", "RowVersion"
    };

    private const string CrudEntityAttributeFqn = "CrudKit.Core.Attributes.CrudEntityAttribute";
    private const string IEntityFqn              = "CrudKit.Core.Interfaces.IEntity";
    private const string IAuditableEntityFqn     = "CrudKit.Core.Interfaces.IAuditableEntity";
    private const string ISoftDeletableFqn       = "CrudKit.Core.Interfaces.ISoftDeletable";
    private const string IMultiTenantFqn         = "CrudKit.Core.Interfaces.IMultiTenant";

    /// <summary>
    /// Parses a [CrudEntity] class symbol into an <see cref="EntityMetadata"/> snapshot.
    /// Returns <c>null</c> when the attribute is missing.
    /// </summary>
    public static EntityMetadata? Parse(INamedTypeSymbol classSymbol)
    {
        var attr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CrudEntityAttributeFqn);

        if (attr is null)
            return null;

        var attrArgs = BuildAttributeArguments(attr);

        string name      = classSymbol.Name;
        string ns        = classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        string fullName  = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        string table     = GetString(attrArgs, "Table", name + "s");
        bool multiTenant = GetBool(attrArgs, "MultiTenant");
        bool readOnly    = GetBool(attrArgs, "ReadOnly");
        bool enableCreate  = GetBool(attrArgs, "EnableCreate", defaultValue: true);
        bool enableUpdate  = GetBool(attrArgs, "EnableUpdate", defaultValue: true);
        bool enableDelete  = GetBool(attrArgs, "EnableDelete", defaultValue: true);
        bool bulkUpdate  = GetBool(attrArgs, "EnableBulkUpdate");
        bool bulkDelete  = GetBool(attrArgs, "EnableBulkDelete");
        string? workflow = GetNullableString(attrArgs, "Workflow");

        bool implementsIEntity        = ImplementsInterface(classSymbol, IEntityFqn)
                                       || ImplementsInterface(classSymbol, IAuditableEntityFqn);
        bool implementsISoftDeletable = ImplementsInterface(classSymbol, ISoftDeletableFqn);
        bool implementsIMultiTenant   = ImplementsInterface(classSymbol, IMultiTenantFqn);

        var properties = ParseProperties(classSymbol);

        return new EntityMetadata(
            name: name,
            @namespace: ns,
            fullName: fullName,
            table: table,
            multiTenant: multiTenant,
            readOnly: readOnly,
            isCreateEnabled: !readOnly && enableCreate,
            isUpdateEnabled: !readOnly && enableUpdate,
            isDeleteEnabled: !readOnly && enableDelete,
            enableBulkUpdate: bulkUpdate,
            enableBulkDelete: bulkDelete,
            workflow: workflow,
            implementsIEntity: implementsIEntity,
            implementsISoftDeletable: implementsISoftDeletable,
            implementsIMultiTenant: implementsIMultiTenant,
            properties: properties);
    }

    // ---------------------------------------------------------------------------
    // Property parsing
    // ---------------------------------------------------------------------------

    private static IReadOnlyList<PropertyMetadata> ParseProperties(INamedTypeSymbol classSymbol)
    {
        var result = new List<PropertyMetadata>();

        // Walk the declared members (not inherited) and include inherited IEntity props via AllInterfaces.
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            if (prop.IsStatic || prop.IsIndexer || prop.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (SystemFields.Contains(prop.Name))
                continue;

            result.Add(ParseProperty(prop));
        }

        return result;
    }

    private static PropertyMetadata ParseProperty(IPropertySymbol prop)
    {
        bool isNullable = prop.NullableAnnotation == NullableAnnotation.Annotated
                          || IsNullableValueType(prop.Type);

        string typeName     = GetTypeName(prop.Type);
        string fullTypeName = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Attribute flags
        bool isRequired    = HasAttribute(prop, "System.ComponentModel.DataAnnotations.RequiredAttribute");
        bool isHashed      = HasAttribute(prop, "CrudKit.Core.Attributes.HashedAttribute");
        bool isProtected   = HasAttribute(prop, "CrudKit.Core.Attributes.ProtectedAttribute");
        bool isSkipUpdate  = HasAttribute(prop, "CrudKit.Core.Attributes.SkipUpdateAttribute");
        bool isSkipResponse = HasAttribute(prop, "CrudKit.Core.Attributes.SkipResponseAttribute");
        bool isUnique      = HasAttribute(prop, "CrudKit.Core.Attributes.UniqueAttribute");
        bool isSearchable  = HasAttribute(prop, "CrudKit.Core.Attributes.SearchableAttribute");

        // [MaxLength(n)]
        bool hasMaxLength = false;
        int  maxLength    = 0;
        var  mlAttr       = GetAttribute(prop, "System.ComponentModel.DataAnnotations.MaxLengthAttribute");
        if (mlAttr != null && mlAttr.ConstructorArguments.Length > 0)
        {
            hasMaxLength = true;
            maxLength    = (int)(mlAttr.ConstructorArguments[0].Value ?? 0);
        }

        // [Range(min, max)]
        bool   hasRange  = false;
        string rangeMin  = "0";
        string rangeMax  = "0";
        var    rangeAttr = GetAttribute(prop, "System.ComponentModel.DataAnnotations.RangeAttribute");
        if (rangeAttr != null && rangeAttr.ConstructorArguments.Length >= 2)
        {
            hasRange = true;
            rangeMin = rangeAttr.ConstructorArguments[0].Value?.ToString() ?? "0";
            rangeMax = rangeAttr.ConstructorArguments[1].Value?.ToString() ?? "0";
        }

        return new PropertyMetadata(
            name: prop.Name,
            typeName: typeName,
            fullTypeName: fullTypeName,
            isNullable: isNullable,
            isRequired: isRequired,
            hasMaxLength: hasMaxLength,
            maxLength: maxLength,
            hasRange: hasRange,
            rangeMin: rangeMin,
            rangeMax: rangeMax,
            isHashed: isHashed,
            isProtected: isProtected,
            isSkipUpdate: isSkipUpdate,
            isSkipResponse: isSkipResponse,
            isUnique: isUnique,
            isSearchable: isSearchable);
    }

    // ---------------------------------------------------------------------------
    // Symbol helpers
    // ---------------------------------------------------------------------------

    private static bool ImplementsInterface(INamedTypeSymbol symbol, string fqn)
    {
        return symbol.AllInterfaces.Any(i => i.ToDisplayString() == fqn);
    }

    private static bool HasAttribute(ISymbol symbol, string fqn)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == fqn);

    private static AttributeData? GetAttribute(ISymbol symbol, string fqn)
        => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == fqn);

    private static bool IsNullableValueType(ITypeSymbol type)
    {
        return type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T;
    }

    private static string GetTypeName(ITypeSymbol type)
    {
        // Strip trailing ? from nullable reference types for the base name
        return type.WithNullableAnnotation(NullableAnnotation.None)
                   .ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    // ---------------------------------------------------------------------------
    // Attribute argument helpers
    // ---------------------------------------------------------------------------

    private static Dictionary<string, object?> BuildAttributeArguments(AttributeData attr)
    {
        var dict = new Dictionary<string, object?>(System.StringComparer.Ordinal);

        // Named arguments take precedence
        foreach (var kv in attr.NamedArguments)
            dict[kv.Key] = kv.Value.Value;

        return dict;
    }

    private static bool GetBool(Dictionary<string, object?> args, string key, bool defaultValue = false)
        => args.TryGetValue(key, out var v) && v is bool b ? b : defaultValue;

    private static string GetString(Dictionary<string, object?> args, string key, string defaultValue)
        => args.TryGetValue(key, out var v) && v is string s && !string.IsNullOrEmpty(s) ? s : defaultValue;

    private static string? GetNullableString(Dictionary<string, object?> args, string key)
        => args.TryGetValue(key, out var v) ? v as string : null;
}
