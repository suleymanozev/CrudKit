using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using CrudKit.SourceGen.Models;

namespace CrudKit.SourceGen.Diagnostics;

/// <summary>
/// Validates an <see cref="EntityMetadata"/> snapshot and returns any diagnostics.
/// The generator emits these diagnostics via the <see cref="SourceProductionContext"/>.
/// </summary>
internal static class EntityValidator
{
    /// <summary>
    /// Returns all diagnostics for the given entity metadata.
    /// <paramref name="location"/> should be the [CrudEntity] attribute syntax location.
    /// Pass <c>null</c> (or <see cref="Location.None"/>) when no source location is available.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Validate(
        EntityMetadata metadata,
        Location? location)
    {
        var diagnostics = new List<Diagnostic>();
        var loc = location ?? Location.None;

        // CRUD001 — must implement IEntity
        if (!metadata.ImplementsIEntity)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.MissingIEntity,
                loc,
                metadata.Name));
        }

        // CRUD010 — explicit empty table name (default is auto-derived, so empty = user set it wrong)
        // We detect this by checking: attribute has Table= but it's empty.
        // EntityParser defaults to "EntityName + s" when Table is empty/omitted, so we need to
        // re-read the raw attribute here. This check is done in the generator before Parse().
        // See EntityParser.Parse() — if you get an EntityMetadata back, Table is never empty.
        // CRUD010 is raised in CrudKitSourceGenerator.cs before calling Parse() (see Task 10).
        if (string.IsNullOrEmpty(metadata.Table))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.EmptyTableName,
                loc,
                metadata.Name));
        }

        return diagnostics;
    }
}
