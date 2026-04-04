using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CrudKit.SourceGen.Diagnostics;
using CrudKit.SourceGen.Generators;
using CrudKit.SourceGen.Parsing;

namespace CrudKit.SourceGen;

[Generator]
public sealed class CrudKitSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entityDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "CrudKit.Core.Attributes.CrudEntityAttribute",
                predicate: (node, _) => node is ClassDeclarationSyntax,
                transform: (ctx, _) =>
                {
                    var symbol = ctx.TargetSymbol as INamedTypeSymbol;
                    return symbol is not null ? EntityParser.Parse(symbol) : null;
                })
            .Where(e => e is not null)
            .Select((e, _) => e!);

        context.RegisterSourceOutput(entityDeclarations, (spc, entity) =>
        {
            // Validate and report diagnostics
            var diagnostics = EntityValidator.Validate(entity, null);
            foreach (var diag in diagnostics)
                spc.ReportDiagnostic(diag);

            // Skip generation if IEntity is missing (CRUD001)
            if (!entity.ImplementsIEntity)
                return;

            // CreateDto
            var createDto = CreateDtoGenerator.Generate(entity);
            if (createDto is not null)
                spc.AddSource($"{entity.Name}CreateDto.g.cs", createDto);

            // UpdateDto
            var updateDto = UpdateDtoGenerator.Generate(entity);
            if (updateDto is not null)
                spc.AddSource($"{entity.Name}UpdateDto.g.cs", updateDto);

            // ResponseDto
            var responseDto = ResponseDtoGenerator.Generate(entity);
            spc.AddSource($"{entity.Name}ResponseDto.g.cs", responseDto);
        });
    }
}
