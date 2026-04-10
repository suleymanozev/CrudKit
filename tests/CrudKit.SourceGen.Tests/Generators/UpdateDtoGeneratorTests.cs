using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

public class UpdateDtoGeneratorTests
{
    private const string OrderEntity = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;
        using System.ComponentModel.DataAnnotations;

        namespace Store.Entities
        {
            [CrudEntity(Resource = "Orders")]
            public class Order : IAuditableEntity
            {
                public Guid Id { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }

                [Required]
                public string CustomerName { get; set; } = string.Empty;

                [SkipUpdate]
                public string OrderNumber { get; set; } = string.Empty;

                [Protected]
                public string CreatedById { get; set; } = string.Empty;

                public decimal Total { get; set; }
                public string? Notes { get; set; }
            }
        }
        """;

    [Fact]
    public void UpdateDto_ContainsOptionalWrappedProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.g.cs");

        Assert.Contains("UpdateOrder", source);
        Assert.Contains("Optional<string> CustomerName", source);
        Assert.Contains("Optional<decimal> Total", source);
        Assert.Contains("Optional<string?> Notes", source);
    }

    [Fact]
    public void UpdateDto_ExcludesSkipUpdateProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.g.cs");

        Assert.DoesNotContain("OrderNumber", source);
    }

    [Fact]
    public void UpdateDto_ExcludesProtectedProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.g.cs");

        Assert.DoesNotContain("CreatedById", source);
    }

    [Fact]
    public void UpdateDto_ExcludesSystemFields()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.g.cs");

        Assert.DoesNotContain("CreatedAt", source);
        Assert.DoesNotContain("UpdatedAt", source);
    }

    [Fact]
    public void UpdateDto_HasDefaultValues_ForOptionalParams()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.g.cs");

        // Each parameter should default to Optional<T>.Undefined (default)
        Assert.Contains("= default", source);
    }

    [Fact]
    public void UpdateDto_NotGenerated_WhenEnableUpdateFalse()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Store.Entities
            {
                [CrudEntity(Resource = "Immutable", EnableUpdate = false)]
                public class ImmutableRecord : IAuditableEntity
                {
                    public Guid Id { get; set; }
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Data { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("UpdateImmutableRecord"));
    }

    [Fact]
    public void UpdateDto_ImportsOptionalNamespace()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.g.cs");

        Assert.Contains("using CrudKit.Core.Models;", source);
    }

    [Fact]
    public void UpdateDto_NotGenerated_WhenManualDtoHasUpdateDtoForAttribute()
    {
        const string source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace TestApp
            {
                [CrudEntity(Resource = "Orders")]
                public class Order : IEntity, IAuditableEntity
                {
                    public Guid Id { get; set; }
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string CustomerName { get; set; } = "";
                    public decimal Total { get; set; }
                }

                [UpdateDtoFor(typeof(Order))]
                public record UpdateOrder
                {
                    public string? CustomerName { get; init; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(source);

        // UpdateDto should NOT be generated — manual DTO exists
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("UpdateOrder"));

        // CreateDto SHOULD still be generated — no manual override
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("CreateOrder"));
    }

    [Fact]
    public void BothDtos_NotGenerated_WhenBothManualAttributesPresent()
    {
        const string source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace TestApp
            {
                [CrudEntity(Resource = "Products")]
                public class Product : IEntity, IAuditableEntity
                {
                    public Guid Id { get; set; }
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Name { get; set; } = "";
                    public decimal Price { get; set; }
                }

                [CreateDtoFor(typeof(Product))]
                public record CreateProduct(string Name, decimal Price);

                [UpdateDtoFor(typeof(Product))]
                public record UpdateProduct
                {
                    public string? Name { get; init; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(source);

        // Neither CreateDto nor UpdateDto should be generated
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("CreateProduct"));
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("UpdateProduct"));

        // ResponseDto should still be generated
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("ProductResponse"));
    }
}
