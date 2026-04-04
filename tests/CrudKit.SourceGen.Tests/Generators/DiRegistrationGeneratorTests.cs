using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

public class DiRegistrationGeneratorTests
{
    private const string ThreeEntities = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;

        namespace App.Entities
        {
            [CrudEntity(Table = "Products")]
            public class Product : IEntity
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public string Name { get; set; } = string.Empty;
            }

            [CrudEntity(Table = "Categories")]
            public class Category : IEntity
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public string Label { get; set; } = string.Empty;
            }

            [CrudEntity(Table = "Brands")]
            public class Brand : IEntity
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public string Name { get; set; } = string.Empty;
            }
        }
        """;

    [Fact]
    public void DiRegistration_GeneratesAddAllCrudMappers()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ThreeEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        Assert.Contains("AddAllCrudMappers", source);
        Assert.Contains("IServiceCollection", source);
    }

    [Fact]
    public void DiRegistration_RegistersICrudMapper_ForFullCrudEntities()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ThreeEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        // Full CRUD entities are registered as ICrudMapper
        Assert.Contains("ICrudMapper<Product, CreateProduct, UpdateProduct, ProductResponse>, ProductMapper", source);
        Assert.Contains("ICrudMapper<Category, CreateCategory, UpdateCategory, CategoryResponse>, CategoryMapper", source);
        Assert.Contains("ICrudMapper<Brand, CreateBrand, UpdateBrand, BrandResponse>, BrandMapper", source);
    }

    [Fact]
    public void DiRegistration_ForwardsIndividualInterfaces_ForFullCrudEntities()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ThreeEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        // Individual interface forwarding registrations must be present
        Assert.Contains("IResponseMapper<Product, ProductResponse>", source);
        Assert.Contains("ICreateMapper<Product, CreateProduct>", source);
        Assert.Contains("IUpdateMapper<Product, UpdateProduct>", source);
    }

    [Fact]
    public void DiRegistration_RegistersIResponseMapperOnly_ForReadOnlyEntity()
    {
        const string readonlyEntities = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace App.Entities
            {
                [CrudEntity(Table = "Reports", ReadOnly = true)]
                public class Report : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Title { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(readonlyEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        Assert.Contains("IResponseMapper<Report, ReportResponse>, ReportMapper", source);
        Assert.DoesNotContain("ICrudMapper", source);
        Assert.DoesNotContain("ICreateMapper", source);
        Assert.DoesNotContain("IUpdateMapper", source);
    }

    [Fact]
    public void DiRegistration_UsesAddScoped()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ThreeEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        // Each full-CRUD entity produces 4 AddScoped calls (ICrudMapper + 3 forwarding)
        // Three entities = 12 total AddScoped calls
        var scopedCount = source.Split(new[] { "AddScoped" }, System.StringSplitOptions.None).Length - 1;
        Assert.Equal(12, scopedCount);
    }

    [Fact]
    public void DiRegistration_ImportsRequiredNamespaces()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ThreeEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        Assert.Contains("using Microsoft.Extensions.DependencyInjection;", source);
        Assert.Contains("using CrudKit.Core.Interfaces;", source);
    }
}
