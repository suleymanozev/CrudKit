using System.Collections.Generic;
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
///   - CreateDto, UpdateDto, ResponseDto
///   - Mapper (ICrudMapper / IResponseMapper implementation)
///   - Hook stub (partial class implementing ICrudHooks)
///   - Collective endpoint mapping and DI registration
/// </summary>
[Generator]
public sealed class CrudKitSourceGenerator : IIncrementalGenerator
{
    private const string CrudEntityAttributeFqn    = "CrudKit.Core.Attributes.CrudEntityAttribute";
    private const string CreateDtoForAttributeFqn  = "CrudKit.Core.Attributes.CreateDtoForAttribute";
    private const string UpdateDtoForAttributeFqn  = "CrudKit.Core.Attributes.UpdateDtoForAttribute";

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
        //    Collect the entity type names that have manual Create DTOs.
        var manualCreateDtos = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CreateDtoForAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var attr = ctx.TargetSymbol.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CreateDtoForAttributeFqn);
                    if (attr?.ConstructorArguments.Length > 0
                        && attr.ConstructorArguments[0].Value is INamedTypeSymbol entityType)
                        return entityType.Name;
                    return null;
                })
            .Where(static n => n is not null)
            .Collect();

        // 3. Find all classes/records decorated with [UpdateDtoFor(...)]
        //    Collect the entity type names that have manual Update DTOs.
        var manualUpdateDtos = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                UpdateDtoForAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    var attr = ctx.TargetSymbol.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UpdateDtoForAttributeFqn);
                    if (attr?.ConstructorArguments.Length > 0
                        && attr.ConstructorArguments[0].Value is INamedTypeSymbol entityType)
                        return entityType.Name;
                    return null;
                })
            .Where(static n => n is not null)
            .Collect();

        // 4. Combine per-entity symbols with the manual DTO sets so each output
        //    step knows which DTOs already exist.
        var entityWithManualDtos = entitySymbols
            .Combine(manualCreateDtos)
            .Combine(manualUpdateDtos);

        // 5. Validate and emit per-entity files
        context.RegisterSourceOutput(entityWithManualDtos, static (spc, data) =>
        {
            var ((item, createSkipSet), updateSkipSet) = data;
            var (symbol, location) = item;

            // CRUD010 — check for explicit empty Table name before parsing
            var attr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CrudEntityAttributeFqn);

            if (attr != null)
            {
                var tableArg = attr.NamedArguments
                    .FirstOrDefault(kv => kv.Key == "Table");

                // TypedConstant default is empty — only report if explicitly set to empty string
                if (tableArg.Key == "Table"
                    && tableArg.Value.Value is string tableVal
                    && tableVal.Length == 0)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.EmptyTableName,
                        location,
                        symbol.Name));
                    return; // Cannot generate safely without a table name
                }
            }

            var metadata = EntityParser.Parse(symbol);
            if (metadata is null)
                return;

            // Emit CRUD001/002/003 diagnostics
            var diagnostics = EntityValidator.Validate(metadata, location);
            foreach (var diag in diagnostics)
                spc.ReportDiagnostic(diag);

            // Do not generate code if IEntity is missing (CRUD001)
            if (!metadata.ImplementsIEntity)
                return;

            bool skipCreate = createSkipSet.Contains(metadata.Name);
            bool skipUpdate = updateSkipSet.Contains(metadata.Name);

            // Per-entity files
            EmitPerEntityFiles(spc, metadata, skipCreate, skipUpdate);
        });

        // 6. Collect all valid entities for collective generators
        var allEntities = entitySymbols
            .Where(static item => EntityParser.Parse(item.Symbol) != null)
            .Select(static (item, _) => EntityParser.Parse(item.Symbol)!)
            .Where(static m => m.ImplementsIEntity)
            .Collect();

        // 7. Emit collective files
        context.RegisterSourceOutput(allEntities, static (spc, entities) =>
        {
            if (entities.IsEmpty)
                return;

            var list = entities.ToList();

            // CrudKitEndpoints.g.cs
            var endpointsSource = EndpointMappingGenerator.Generate(list);
            if (!string.IsNullOrEmpty(endpointsSource))
                spc.AddSource("CrudKitEndpoints.g.cs", endpointsSource);

            // CrudKitMappers.g.cs
            var mappersSource = DiRegistrationGenerator.Generate(list);
            if (!string.IsNullOrEmpty(mappersSource))
                spc.AddSource("CrudKitMappers.g.cs", mappersSource);
        });
    }

    // ---------------------------------------------------------------------------
    // Per-entity file emission
    // ---------------------------------------------------------------------------

    private static void EmitPerEntityFiles(
        SourceProductionContext spc,
        EntityMetadata entity,
        bool skipCreate,
        bool skipUpdate)
    {
        // CreateDto — skip when manual DTO is present or feature is disabled
        if (!skipCreate)
        {
            var createDtoSource = CreateDtoGenerator.Generate(entity);
            if (createDtoSource != null)
                spc.AddSource($"{entity.Name}CreateDto.g.cs", createDtoSource);
        }

        // UpdateDto — skip when manual DTO is present or feature is disabled
        if (!skipUpdate)
        {
            var updateDtoSource = UpdateDtoGenerator.Generate(entity);
            if (updateDtoSource != null)
                spc.AddSource($"{entity.Name}UpdateDto.g.cs", updateDtoSource);
        }

        // ResponseDto — always emitted (no manual override attribute)
        var responseDtoSource = ResponseDtoGenerator.Generate(entity);
        spc.AddSource($"{entity.Name}ResponseDto.g.cs", responseDtoSource);

        // Mapper — always emitted
        var mapperSource = MapperGenerator.Generate(entity);
        spc.AddSource($"{entity.Name}Mapper.g.cs", mapperSource);

        // Hook stub — always emitted
        var hookStubSource = HookStubGenerator.Generate(entity);
        spc.AddSource($"{entity.Name}Hooks.g.cs", hookStubSource);
    }
}
