using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

public class EndpointMappingGeneratorTests
{
    private const string TwoEntities = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;

        namespace App.Entities
        {
            [CrudEntity(Table = "Products")]
            public class Product : IAuditableEntity
            {
                public Guid Id { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public string Name { get; set; } = string.Empty;
            }

            [CrudEntity(Table = "Catalogs", ReadOnly = true)]
            public class Catalog : IAuditableEntity
            {
                public Guid Id { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public string Title { get; set; } = string.Empty;
            }
        }
        """;

    [Fact]
    public void EndpointMapping_GeneratesMapAllCrudEndpoints()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(TwoEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitEndpoints.g.cs");

        Assert.Contains("MapAllCrudEndpoints", source);
        Assert.Contains("IEndpointRouteBuilder", source);
    }

    [Fact]
    public void EndpointMapping_UsesFullCrudOverload_ForFullEntity()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(TwoEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitEndpoints.g.cs");

        Assert.Contains("MapCrudEndpoints<Product, CreateProduct, UpdateProduct,", source);
    }

    [Fact]
    public void EndpointMapping_UsesReadOnlyOverload_ForReadOnlyEntity()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(TwoEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitEndpoints.g.cs");

        Assert.Contains("MapCrudEndpoints<Catalog,", source);
    }

    [Fact]
    public void HookStub_Generated_PerEntity()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(TwoEntities);

        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("ProductHooks"));
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("CatalogHooks"));
    }

    [Fact]
    public void HookStub_ImplementsICrudHooks()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(TwoEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductHooks.g.cs");

        Assert.Contains("ICrudHooks<Product>", source);
        Assert.Contains("partial class ProductHooks", source);
    }
}
