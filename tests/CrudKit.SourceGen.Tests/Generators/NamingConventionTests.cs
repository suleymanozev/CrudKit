using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

/// <summary>
/// Tests for [assembly: CrudKit(...)] naming convention attribute.
/// </summary>
public class NamingConventionTests
{
    private const string ProductEntity = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;

        namespace Store.Entities
        {
            [CrudEntity(Resource = "Products")]
            public class Product : IAuditableEntity
            {
                public Guid Id { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public string Name { get; set; } = string.Empty;
                public decimal Price { get; set; }
            }
        }
        """;

    // ---------------------------------------------------------------------------
    // Default naming — no [assembly: CrudKit] present
    // ---------------------------------------------------------------------------

    [Fact]
    public void DefaultNaming_UsesStandardPatterns()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);
        var fileNames = result.GeneratedTrees
            .Select(t => System.IO.Path.GetFileName(t.FilePath))
            .ToList();

        // Default: Create{Name}, Update{Name}, {Name}Response, {Name}Mapper, {Name}Hooks
        Assert.Contains("CreateProduct.g.cs", fileNames);
        Assert.Contains("UpdateProduct.g.cs", fileNames);
        Assert.Contains("ProductResponse.g.cs", fileNames);
        Assert.Contains("ProductMapper.g.cs", fileNames);
        Assert.Contains("ProductHooks.g.cs", fileNames);
        Assert.Empty(result.Diagnostics.Where(d => d.Id.StartsWith("CRUD011") || d.Id.StartsWith("CRUD012")));
    }

    // ---------------------------------------------------------------------------
    // Custom naming — [assembly: CrudKit(...)] overrides patterns
    // ---------------------------------------------------------------------------

    [Fact]
    public void CustomNaming_AppliesCreateDtoPattern()
    {
        const string assemblyAttr = """
            using CrudKit.Core.Attributes;
            [assembly: CrudKit(CreateDtoNamingTemplate = "{Name}CreateRequest")]
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity, assemblyAttr);
        var fileNames = result.GeneratedTrees
            .Select(t => System.IO.Path.GetFileName(t.FilePath))
            .ToList();

        Assert.Contains("ProductCreateRequest.g.cs", fileNames);
        Assert.DoesNotContain("CreateProduct.g.cs", fileNames);

        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductCreateRequest.g.cs");
        Assert.Contains("public sealed record ProductCreateRequest(", source);
    }

    [Fact]
    public void CustomNaming_AppliesUpdateDtoPattern()
    {
        const string assemblyAttr = """
            using CrudKit.Core.Attributes;
            [assembly: CrudKit(UpdateDtoNamingTemplate = "{Name}UpdateRequest")]
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity, assemblyAttr);
        var fileNames = result.GeneratedTrees
            .Select(t => System.IO.Path.GetFileName(t.FilePath))
            .ToList();

        Assert.Contains("ProductUpdateRequest.g.cs", fileNames);
        Assert.DoesNotContain("UpdateProduct.g.cs", fileNames);

        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductUpdateRequest.g.cs");
        Assert.Contains("public sealed record ProductUpdateRequest(", source);
    }

    [Fact]
    public void CustomNaming_AppliesResponseDtoPattern()
    {
        const string assemblyAttr = """
            using CrudKit.Core.Attributes;
            [assembly: CrudKit(ResponseDtoNamingTemplate = "{Name}Dto")]
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity, assemblyAttr);
        var fileNames = result.GeneratedTrees
            .Select(t => System.IO.Path.GetFileName(t.FilePath))
            .ToList();

        Assert.Contains("ProductDto.g.cs", fileNames);
        Assert.DoesNotContain("ProductResponse.g.cs", fileNames);

        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductDto.g.cs");
        Assert.Contains("public sealed record ProductDto(", source);
    }

    // ---------------------------------------------------------------------------
    // Partial override — only one pattern changed, others use defaults
    // ---------------------------------------------------------------------------

    [Fact]
    public void PartialOverride_MixesCustomAndDefault()
    {
        const string assemblyAttr = """
            using CrudKit.Core.Attributes;
            [assembly: CrudKit(CreateDtoNamingTemplate = "{Name}CreateRequest")]
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity, assemblyAttr);
        var fileNames = result.GeneratedTrees
            .Select(t => System.IO.Path.GetFileName(t.FilePath))
            .ToList();

        // CreateDtoNamingTemplate overridden
        Assert.Contains("ProductCreateRequest.g.cs", fileNames);
        // UpdateDtoNamingTemplate, ResponseDtoNamingTemplate, MapperNamingTemplate, HooksNamingTemplate use defaults
        Assert.Contains("UpdateProduct.g.cs", fileNames);
        Assert.Contains("ProductResponse.g.cs", fileNames);
        Assert.Contains("ProductMapper.g.cs", fileNames);
        Assert.Contains("ProductHooks.g.cs", fileNames);
    }

    // ---------------------------------------------------------------------------
    // Validation — CRUD011 and CRUD012 errors
    // ---------------------------------------------------------------------------

    [Fact]
    public void EmptyPattern_EmitsCRUD011()
    {
        const string assemblyAttr = """
            using CrudKit.Core.Attributes;
            [assembly: CrudKit(CreateDtoNamingTemplate = "")]
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity, assemblyAttr);

        Assert.Contains(result.Diagnostics, d => d.Id == "CRUD011");
    }

    [Fact]
    public void MissingPlaceholder_EmitsCRUD012()
    {
        const string assemblyAttr = """
            using CrudKit.Core.Attributes;
            [assembly: CrudKit(CreateDtoNamingTemplate = "CreateRequest")]
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity, assemblyAttr);

        Assert.Contains(result.Diagnostics, d => d.Id == "CRUD012");
        var diag = result.Diagnostics.First(d => d.Id == "CRUD012");
        Assert.Contains("CreateRequest", diag.GetMessage());
    }

    [Fact]
    public void EmptyPattern_NoCodeGenerated()
    {
        const string assemblyAttr = """
            using CrudKit.Core.Attributes;
            [assembly: CrudKit(CreateDtoNamingTemplate = "")]
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity, assemblyAttr);

        // No per-entity files should be generated when naming is invalid
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("ProductCreateRequest"));
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("CreateProduct"));
    }

    [Fact]
    public void MissingPlaceholder_MultiplePatterns_AllEmitCRUD012()
    {
        const string assemblyAttr = """
            using CrudKit.Core.Attributes;
            [assembly: CrudKit(CreateDtoNamingTemplate = "CreateRequest", UpdateDtoNamingTemplate = "UpdateRequest")]
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity, assemblyAttr);

        var crud012Diags = result.Diagnostics.Where(d => d.Id == "CRUD012").ToList();
        Assert.Equal(2, crud012Diags.Count);
    }
}
