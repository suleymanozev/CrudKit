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
            [CrudEntity(Table = "Orders")]
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
        var source = GeneratorTestHelper.GetGeneratedSource(result, "OrderUpdateDto.g.cs");

        Assert.Contains("UpdateOrder", source);
        Assert.Contains("Optional<string> CustomerName", source);
        Assert.Contains("Optional<decimal> Total", source);
        Assert.Contains("Optional<string?> Notes", source);
    }

    [Fact]
    public void UpdateDto_ExcludesSkipUpdateProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "OrderUpdateDto.g.cs");

        Assert.DoesNotContain("OrderNumber", source);
    }

    [Fact]
    public void UpdateDto_ExcludesProtectedProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "OrderUpdateDto.g.cs");

        Assert.DoesNotContain("CreatedById", source);
    }

    [Fact]
    public void UpdateDto_ExcludesSystemFields()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "OrderUpdateDto.g.cs");

        Assert.DoesNotContain("CreatedAt", source);
        Assert.DoesNotContain("UpdatedAt", source);
    }

    [Fact]
    public void UpdateDto_HasDefaultValues_ForOptionalParams()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "OrderUpdateDto.g.cs");

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
                [CrudEntity(Table = "Immutable", EnableUpdate = false)]
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

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("UpdateDto"));
    }

    [Fact]
    public void UpdateDto_ImportsOptionalNamespace()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "OrderUpdateDto.g.cs");

        Assert.Contains("using CrudKit.Core.Models;", source);
    }
}
