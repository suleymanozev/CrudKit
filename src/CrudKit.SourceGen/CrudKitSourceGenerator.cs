using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CrudKit.SourceGen.Diagnostics;
using CrudKit.SourceGen.Generators;
using CrudKit.SourceGen.Models;
using CrudKit.SourceGen.Parsing;

namespace CrudKit.SourceGen;

/// <summary>
/// Roslyn incremental source generator for CrudKit.
/// Scans for classes decorated with [CrudEntity] and generates:
///   - Hook stub (partial class implementing ICrudHooks)
///   - Collective endpoint mapping (MapAllCrudEndpoints)
/// DTO and mapper generation is not performed — users write their own DTOs
/// and register them via [CreateDtoFor], [UpdateDtoFor], [ResponseDtoFor].
/// </summary>
[Generator]
public sealed class CrudKitSourceGenerator : IIncrementalGenerator
{
    private const string CrudEntityAttributeFqn    = "CrudKit.Core.Attributes.CrudEntityAttribute";
    private const string CreateDtoForAttributeFqn  = "CrudKit.Core.Attributes.CreateDtoForAttribute";
    private const string UpdateDtoForAttributeFqn  = "CrudKit.Core.Attributes.UpdateDtoForAttribute";
    private const string CrudKitAttributeFqn       = "CrudKit.Core.Attributes.CrudKitAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Find all classes decorated with [CrudEntity]
        var entitySymbols = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CrudEntityAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return (Symbol: (INamedTypeSymbol)ctx.TargetSymbol,
                            Location: ctx.TargetNode.GetLocation());
                })
            .WithTrackingName("CrudEntityClasses");

        // 2. Find all classes/records decorated with [CreateDtoFor(...)]
        //    Collect (entityName -> dtoFqn) pairs for endpoint mapping.
        var createDtoPairs = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CreateDtoForAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var dtoSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
                    var attr = dtoSymbol.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CreateDtoForAttributeFqn);
                    if (attr?.ConstructorArguments.Length > 0
                        && attr.ConstructorArguments[0].Value is INamedTypeSymbol entityType)
                        return (EntityName: entityType.Name, DtoFqn: dtoSymbol.ToDisplayString());
                    return (EntityName: (string?)null, DtoFqn: (string?)null);
                })
            .Where(static p => p.EntityName is not null)
            .Collect();

        // 3. Find all classes/records decorated with [UpdateDtoFor(...)]
        //    Collect (entityName -> dtoFqn) pairs for endpoint mapping.
        var updateDtoPairs = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                UpdateDtoForAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var dtoSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
                    var attr = dtoSymbol.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UpdateDtoForAttributeFqn);
                    if (attr?.ConstructorArguments.Length > 0
                        && attr.ConstructorArguments[0].Value is INamedTypeSymbol entityType)
                        return (EntityName: entityType.Name, DtoFqn: dtoSymbol.ToDisplayString());
                    return (EntityName: (string?)null, DtoFqn: (string?)null);
                })
            .Where(static p => p.EntityName is not null)
            .Collect();

        // 4. Read [assembly: CrudKit(...)] naming convention (only HooksNamingTemplate is relevant now)
        var namingConvention = context.CompilationProvider.Select(static (compilation, _) =>
        {
            var attr = compilation.Assembly.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CrudKitAttributeFqn);

            if (attr == null)
                return NamingConvention.Default;

            var naming = NamingConvention.Default;
            foreach (var arg in attr.NamedArguments)
            {
                var value = arg.Value.Value as string ?? string.Empty;
                naming = arg.Key switch
                {
                    "HooksNamingTemplate" => naming with { HooksNamingTemplate = value },
                    _                     => naming,
                };
            }
            return naming;
        });

        // 5. Per-entity: validate and emit hook stubs
        var entityWithNaming = entitySymbols.Combine(namingConvention);

        context.RegisterSourceOutput(entityWithNaming, static (spc, data) =>
        {
            var (item, naming) = data;
            var (symbol, location) = item;

            // CRUD010 — check for explicit empty Resource name before parsing
            var attr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CrudEntityAttributeFqn);

            if (attr != null)
            {
                var resourceArg = attr.NamedArguments
                    .FirstOrDefault(kv => kv.Key == "Resource");

                if (resourceArg.Key == "Resource"
                    && resourceArg.Value.Value is string resourceVal
                    && resourceVal.Length == 0)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.EmptyTableName,
                        location,
                        symbol.Name));
                    return;
                }
            }

            // Validate hooks naming template
            ValidateNamingPattern(spc, "HooksNamingTemplate", naming.HooksNamingTemplate);

            var metadata = EntityParser.Parse(symbol);
            if (metadata is null)
                return;

            // Emit CRUD001/002/003 diagnostics
            var diagnostics = EntityValidator.Validate(metadata, location);
            foreach (var diag in diagnostics)
                spc.ReportDiagnostic(diag);

            if (!metadata.ImplementsIEntity)
                return;

            if (!IsNamingValid(naming))
                return;

            // Hook stub — always emitted
            var hookStubSource = HookStubGenerator.Generate(metadata, naming);
            spc.AddSource($"{naming.FormatHooksName(metadata.Name)}.g.cs", hookStubSource);
        });

        // 6. Collect all valid entities for collective generators
        var allEntities = entitySymbols
            .Where(static item => EntityParser.Parse(item.Symbol) != null)
            .Select(static (item, _) => EntityParser.Parse(item.Symbol)!)
            .Where(static m => m.ImplementsIEntity)
            .Collect();

        var collectiveData = allEntities
            .Combine(createDtoPairs)
            .Combine(updateDtoPairs);

        // 7. Emit collective files (endpoint mapping)
        context.RegisterSourceOutput(collectiveData, static (spc, data) =>
        {
            var ((entities, createPairs), updatePairs) = data;

            if (entities.IsEmpty)
                return;

            var list = entities.ToList();

            // Build FQN lookup maps from [CreateDtoFor] / [UpdateDtoFor] attributes
            var createMap = new Dictionary<string, string>();
            foreach (var pair in createPairs)
            {
                if (pair.EntityName != null && pair.DtoFqn != null)
                    createMap[pair.EntityName] = pair.DtoFqn;
            }

            var updateMap = new Dictionary<string, string>();
            foreach (var pair in updatePairs)
            {
                if (pair.EntityName != null && pair.DtoFqn != null)
                    updateMap[pair.EntityName] = pair.DtoFqn;
            }

            // CrudKitEndpoints.g.cs
            var endpointsSource = EndpointMappingGenerator.Generate(list, createMap, updateMap);
            if (!string.IsNullOrEmpty(endpointsSource))
                spc.AddSource("CrudKitEndpoints.g.cs", endpointsSource);
        });
    }

    // ---------------------------------------------------------------------------
    // Naming validation helpers
    // ---------------------------------------------------------------------------

    private static void ValidateNamingPattern(SourceProductionContext spc, string property, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.NamingPatternEmpty, Location.None, property));
            return;
        }

        if (!pattern.Contains("{Name}"))
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.NamingPatternMissingPlaceholder, Location.None, property, pattern));
        }
    }

    private static bool IsNamingValid(NamingConvention naming)
    {
        return IsPatternValid(naming.HooksNamingTemplate);
    }

    private static bool IsPatternValid(string pattern)
        => !string.IsNullOrEmpty(pattern) && pattern.Contains("{Name}");
}
