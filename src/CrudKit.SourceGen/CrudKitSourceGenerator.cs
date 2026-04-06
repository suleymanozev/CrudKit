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

        // 4. Read [assembly: CrudKit(...)] naming convention
        var namingConvention = context.CompilationProvider.Select(static (compilation, _) =>
        {
            var attr = compilation.Assembly.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CrudKitAttributeFqn);

            if (attr == null)
                return NamingConvention.Default;

            // Start from defaults and apply each named argument
            var naming = NamingConvention.Default;
            foreach (var arg in attr.NamedArguments)
            {
                var value = arg.Value.Value as string ?? string.Empty;
                naming = arg.Key switch
                {
                    "CreateDto"  => naming with { CreateDtoPattern  = value },
                    "UpdateDto"  => naming with { UpdateDtoPattern  = value },
                    "ResponseDto"=> naming with { ResponseDtoPattern = value },
                    "Mapper"     => naming with { MapperPattern     = value },
                    "Hooks"      => naming with { HooksPattern      = value },
                    _            => naming,
                };
            }
            return naming;
        });

        // 5. Combine per-entity symbols with the manual DTO sets and naming convention
        var entityWithManualDtos = entitySymbols
            .Combine(manualCreateDtos)
            .Combine(manualUpdateDtos);

        var entityWithNaming = entityWithManualDtos.Combine(namingConvention);

        // 6. Validate and emit per-entity files
        context.RegisterSourceOutput(entityWithNaming, static (spc, data) =>
        {
            var (((item, createSkipSet), updateSkipSet), naming) = data;
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

            // Validate naming patterns — report CRUD011/CRUD012 errors
            ValidateNamingPattern(spc, "CreateDto",   naming.CreateDtoPattern);
            ValidateNamingPattern(spc, "UpdateDto",   naming.UpdateDtoPattern);
            ValidateNamingPattern(spc, "ResponseDto", naming.ResponseDtoPattern);
            ValidateNamingPattern(spc, "Mapper",      naming.MapperPattern);
            ValidateNamingPattern(spc, "Hooks",       naming.HooksPattern);

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

            // Do not generate code when naming patterns are invalid
            if (!IsNamingValid(naming))
                return;

            bool skipCreate = createSkipSet.Contains(metadata.Name);
            bool skipUpdate = updateSkipSet.Contains(metadata.Name);

            // Per-entity files
            EmitPerEntityFiles(spc, metadata, naming, skipCreate, skipUpdate);
        });

        // 7. Collect all valid entities for collective generators
        var allEntities = entitySymbols
            .Where(static item => EntityParser.Parse(item.Symbol) != null)
            .Select(static (item, _) => EntityParser.Parse(item.Symbol)!)
            .Where(static m => m.ImplementsIEntity)
            .Collect();

        var allEntitiesWithNaming = allEntities.Combine(namingConvention);

        // 8. Emit collective files
        context.RegisterSourceOutput(allEntitiesWithNaming, static (spc, data) =>
        {
            var (entities, naming) = data;

            if (entities.IsEmpty)
                return;

            // Skip collective generation when naming is invalid
            if (!IsNamingValid(naming))
                return;

            var list = entities.ToList();

            // CrudKitEndpoints.g.cs
            var endpointsSource = EndpointMappingGenerator.Generate(list, naming);
            if (!string.IsNullOrEmpty(endpointsSource))
                spc.AddSource("CrudKitEndpoints.g.cs", endpointsSource);

            // CrudKitMappers.g.cs
            var mappersSource = DiRegistrationGenerator.Generate(list, naming);
            if (!string.IsNullOrEmpty(mappersSource))
                spc.AddSource("CrudKitMappers.g.cs", mappersSource);
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
        return IsPatternValid(naming.CreateDtoPattern)
            && IsPatternValid(naming.UpdateDtoPattern)
            && IsPatternValid(naming.ResponseDtoPattern)
            && IsPatternValid(naming.MapperPattern)
            && IsPatternValid(naming.HooksPattern);
    }

    private static bool IsPatternValid(string pattern)
        => !string.IsNullOrEmpty(pattern) && pattern.Contains("{Name}");

    // ---------------------------------------------------------------------------
    // Per-entity file emission
    // ---------------------------------------------------------------------------

    private static void EmitPerEntityFiles(
        SourceProductionContext spc,
        EntityMetadata entity,
        NamingConvention naming,
        bool skipCreate,
        bool skipUpdate)
    {
        // CreateDto — skip when manual DTO is present or feature is disabled
        if (!skipCreate)
        {
            var createDtoSource = CreateDtoGenerator.Generate(entity, naming);
            if (createDtoSource != null)
                spc.AddSource($"{naming.FormatCreateDto(entity.Name)}.g.cs", createDtoSource);
        }

        // UpdateDto — skip when manual DTO is present or feature is disabled
        if (!skipUpdate)
        {
            var updateDtoSource = UpdateDtoGenerator.Generate(entity, naming);
            if (updateDtoSource != null)
                spc.AddSource($"{naming.FormatUpdateDto(entity.Name)}.g.cs", updateDtoSource);
        }

        // ResponseDto — always emitted (no manual override attribute)
        var responseDtoSource = ResponseDtoGenerator.Generate(entity, naming);
        spc.AddSource($"{naming.FormatResponseDto(entity.Name)}.g.cs", responseDtoSource);

        // Mapper — always emitted
        var mapperSource = MapperGenerator.Generate(entity, naming);
        spc.AddSource($"{naming.FormatMapper(entity.Name)}.g.cs", mapperSource);

        // Hook stub — always emitted
        var hookStubSource = HookStubGenerator.Generate(entity, naming);
        spc.AddSource($"{naming.FormatHooks(entity.Name)}.g.cs", hookStubSource);
    }
}
