using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

/// <summary>
/// Tests for [assembly: CrudKit(...)] naming convention attribute.
/// Only HooksNamingTemplate is relevant — DTO/mapper naming is user-controlled.
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

    [Fact]
    public void DefaultNaming_GeneratesHookStub()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);
        var fileNames = result.GeneratedTrees
            .Select(t => System.IO.Path.GetFileName(t.FilePath))
            .ToList();

        Assert.Contains("ProductHooks.g.cs", fileNames);
        Assert.Empty(result.Diagnostics.Where(d => d.Id.StartsWith("CRUD011") || d.Id.StartsWith("CRUD012")));
    }

    [Fact]
    public void DefaultNaming_DoesNotGenerateDtosOrMappers()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);
        var fileNames = result.GeneratedTrees
            .Select(t => System.IO.Path.GetFileName(t.FilePath))
            .ToList();

        Assert.DoesNotContain("CreateProduct.g.cs", fileNames);
        Assert.DoesNotContain("UpdateProduct.g.cs", fileNames);
        Assert.DoesNotContain("ProductResponse.g.cs", fileNames);
        Assert.DoesNotContain("ProductMapper.g.cs", fileNames);
    }

    [Fact]
    public void CustomNaming_AppliesHooksPattern()
    {
        const string assemblyAttr = """
            using CrudKit.Core.Attributes;
            [assembly: CrudKit(HooksNamingTemplate = "{Name}CrudHooks")]
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity, assemblyAttr);
        var fileNames = result.GeneratedTrees
            .Select(t => System.IO.Path.GetFileName(t.FilePath))
            .ToList();

        Assert.Contains("ProductCrudHooks.g.cs", fileNames);
        Assert.DoesNotContain("ProductHooks.g.cs", fileNames);
    }

    [Fact]
    public void EmptyHooksPattern_EmitsCRUD011()
    {
        const string assemblyAttr = """
            using CrudKit.Core.Attributes;
            [assembly: CrudKit(HooksNamingTemplate = "")]
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity, assemblyAttr);

        Assert.Contains(result.Diagnostics, d => d.Id == "CRUD011");
    }

    [Fact]
    public void MissingPlaceholderInHooksPattern_EmitsCRUD012()
    {
        const string assemblyAttr = """
            using CrudKit.Core.Attributes;
            [assembly: CrudKit(HooksNamingTemplate = "CrudHooks")]
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity, assemblyAttr);

        Assert.Contains(result.Diagnostics, d => d.Id == "CRUD012");
    }

    [Fact]
    public void EmptyHooksPattern_NoCodeGenerated()
    {
        const string assemblyAttr = """
            using CrudKit.Core.Attributes;
            [assembly: CrudKit(HooksNamingTemplate = "")]
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity, assemblyAttr);

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("ProductHooks"));
    }
}
