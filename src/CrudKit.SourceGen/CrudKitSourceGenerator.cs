using Microsoft.CodeAnalysis;

namespace CrudKit.SourceGen;

[Generator]
public sealed class CrudKitSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(ctx =>
            ctx.AddSource("CrudKit.SourceGen.Marker.g.cs",
                "// CrudKit.SourceGen loaded successfully.\n"));
    }
}
