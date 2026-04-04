using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

public class CreateDtoGeneratorTests
{
    private const string ProductEntity = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;
        using System.ComponentModel.DataAnnotations;

        namespace Store.Entities
        {
            [CrudEntity(Table = "Products")]
            public class Product : IEntity
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }

                [Required]
                [MaxLength(200)]
                public string Name { get; set; } = string.Empty;

                [Range(0.01, 99999.99)]
                public decimal Price { get; set; }

                public string? Description { get; set; }

                [Protected]
                public string InternalCode { get; set; } = string.Empty;
            }
        }
        """;

    [Fact]
    public void CreateDto_ContainsRequiredProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductCreateDto.g.cs");

        Assert.Contains("CreateProduct", source);
        Assert.Contains("[Required]", source);
        Assert.Contains("[MaxLength(200)]", source);
        Assert.Contains("decimal Price", source);
        Assert.Contains("string? Description", source);
    }

    [Fact]
    public void CreateDto_ExcludesProtectedProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductCreateDto.g.cs");

        Assert.DoesNotContain("InternalCode", source);
    }

    [Fact]
    public void CreateDto_ExcludesSystemFields()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductCreateDto.g.cs");

        Assert.DoesNotContain("CreatedAt", source);
        Assert.DoesNotContain("UpdatedAt", source);
        Assert.DoesNotContain(" Id ", source);
    }

    [Fact]
    public void CreateDto_NotGenerated_WhenEnableCreateFalse()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Store.Entities
            {
                [CrudEntity(Table = "Logs", EnableCreate = false)]
                public class Log : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Message { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("CreateDto"));
    }

    [Fact]
    public void CreateDto_NotGenerated_WhenReadOnly()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Store.Entities
            {
                [CrudEntity(Table = "Views", ReadOnly = true)]
                public class View : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Label { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("CreateDto"));
    }

    [Fact]
    public void CreateDto_HasRangeAttribute_WhenPropertyHasRange()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductCreateDto.g.cs");

        Assert.Contains("[Range(0.01, 99999.99)]", source);
    }
}
