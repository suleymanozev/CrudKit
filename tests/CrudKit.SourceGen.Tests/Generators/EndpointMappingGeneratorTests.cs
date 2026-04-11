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
            [CrudEntity(Resource = "Products")]
            public class Product : IAuditableEntity
            {
                public Guid Id { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public string Name { get; set; } = string.Empty;
            }

            [CrudEntity(Resource = "Catalogs", ReadOnly = true)]
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
        Assert.Contains("WebApplication", source);
    }

    [Fact]
    public void EndpointMapping_FallsBackToEntityOnly_WhenNoDtoAttributes()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(TwoEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitEndpoints.g.cs");

        // Without [CreateDtoFor]/[UpdateDtoFor], falls back to entity-only overload
        Assert.Contains("MapCrudEndpoints<App.Entities.Product>()", source);
    }

    [Fact]
    public void EndpointMapping_UsesCrudOverload_WhenDtoAttributesPresent()
    {
        const string entitiesWithDtos = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace App.Entities
            {
                [CrudEntity(Resource = "Products")]
                public class Product : IAuditableEntity
                {
                    public Guid Id { get; set; }
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Name { get; set; } = string.Empty;
                }
            }

            namespace App.Dtos
            {
                [CreateDtoFor(typeof(App.Entities.Product))]
                public record CreateProduct(string Name);

                [UpdateDtoFor(typeof(App.Entities.Product))]
                public record UpdateProduct(string? Name);
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entitiesWithDtos);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitEndpoints.g.cs");

        Assert.Contains("MapCrudEndpoints<App.Entities.Product, App.Dtos.CreateProduct, App.Dtos.UpdateProduct>()", source);
    }

    [Fact]
    public void EndpointMapping_UsesReadOnlyOverload_ForReadOnlyEntity()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(TwoEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitEndpoints.g.cs");

        Assert.Contains("MapCrudEndpoints<App.Entities.Catalog>()", source);
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
