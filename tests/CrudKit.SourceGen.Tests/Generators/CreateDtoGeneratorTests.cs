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
            public class Product : IAuditableEntity
            {
                public Guid Id { get; set; }
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
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CreateProduct.g.cs");

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
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CreateProduct.g.cs");

        Assert.DoesNotContain("InternalCode", source);
    }

    [Fact]
    public void CreateDto_ExcludesSystemFields()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CreateProduct.g.cs");

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
                public class Log : IAuditableEntity
                {
                    public Guid Id { get; set; }
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Message { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("CreateLog"));
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
                public class View : IAuditableEntity
                {
                    public Guid Id { get; set; }
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Label { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("CreateView"));
    }

    [Fact]
    public void CreateDto_HasRangeAttribute_WhenPropertyHasRange()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CreateProduct.g.cs");

        Assert.Contains("[Range(0.01, 99999.99)]", source);
    }

    [Fact]
    public void CreateDto_NotGenerated_WhenManualDtoHasCreateDtoForAttribute()
    {
        const string source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp
            {
                [CrudEntity(Table = "Orders")]
                public class Order : IEntity, IAuditableEntity
                {
                    public Guid Id { get; set; }
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    [Required]
                    public string CustomerName { get; set; } = "";
                    public decimal Total { get; set; }
                }

                [CreateDtoFor(typeof(Order))]
                public record CreateOrder(string CustomerName, decimal Total);
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(source);

        // CreateDto should NOT be generated — manual DTO exists
        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("CreateOrder"));

        // UpdateDto SHOULD still be generated — no manual override
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("UpdateOrder"));
    }

    [Fact]
    public void CreateDto_NotGenerated_WhenManualRecordHasCreateDtoForAttribute()
    {
        // Verifies that record syntax (not just class) is also recognised
        const string source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace TestApp
            {
                [CrudEntity(Table = "Invoices")]
                public class Invoice : IEntity, IAuditableEntity
                {
                    public Guid Id { get; set; }
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Number { get; set; } = "";
                }

                [CreateDtoFor(typeof(Invoice))]
                public record CreateInvoice(string Number);
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(source);

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("CreateInvoice"));
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("UpdateInvoice"));
    }

    [Fact]
    public void BothDtos_Generated_WhenNoManualAttributesPresent()
    {
        // Verifies existing behaviour is unchanged when no manual DTOs exist
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);

        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("CreateProduct"));
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("UpdateProduct"));
    }
}
