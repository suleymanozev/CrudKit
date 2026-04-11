using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

/// <summary>
/// End-to-end: verifies that only hook stubs and endpoint mapping are generated.
/// DTOs and mappers are no longer auto-generated — users write their own.
/// </summary>
public class IntegrationTests
{
    private const string FullEntity = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;
        using System.ComponentModel.DataAnnotations;

        namespace Acme.Domain.Entities
        {
            [CrudEntity(
                Resource = "Products",
                EnableBulkUpdate = true)]
            public class Product : IAuditableEntity, ISoftDeletable, IMultiTenant
            {
                public Guid Id { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public DateTime? DeletedAt { get; set; }
                public Guid? DeleteBatchId { get; set; }
                public string TenantId { get; set; } = string.Empty;

                [Required]
                [MaxLength(200)]
                public string Name { get; set; } = string.Empty;

                [Range(0.01, 999999.99)]
                public decimal Price { get; set; }

                public string? Description { get; set; }

                [SkipUpdate]
                public string Sku { get; set; } = string.Empty;

                [SkipResponse]
                public string InternalTag { get; set; } = string.Empty;

                [Protected]
                public string CreatedById { get; set; } = string.Empty;

                [Searchable]
                public string Tags { get; set; } = string.Empty;
            }
        }
        """;

    [Fact]
    public void FullEntity_GeneratesOnlyHookAndEndpointFiles()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);

        var fileNames = result.GeneratedTrees.Select(t => System.IO.Path.GetFileName(t.FilePath)).ToList();

        // Should generate hook stub and endpoint mapping
        Assert.Contains("ProductHooks.g.cs", fileNames);
        Assert.Contains("CrudKitEndpoints.g.cs", fileNames);

        // Should NOT generate DTOs, mapper, or DI registration
        Assert.DoesNotContain("CreateProduct.g.cs", fileNames);
        Assert.DoesNotContain("UpdateProduct.g.cs", fileNames);
        Assert.DoesNotContain("ProductResponse.g.cs", fileNames);
        Assert.DoesNotContain("ProductMapper.g.cs", fileNames);
        Assert.DoesNotContain("CrudKitMappers.g.cs", fileNames);
    }

    [Fact]
    public void FullEntity_NoCrudDiagnosticsEmitted()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);

        var crudDiags = result.Diagnostics.Where(d => d.Id.StartsWith("CRUD")).ToList();
        Assert.Empty(crudDiags);
    }

    [Fact]
    public void FullEntity_EndpointMapping_FallsBackToEntityOnly_WhenNoDtoAttributes()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitEndpoints.g.cs");

        // Without [CreateDtoFor]/[UpdateDtoFor], falls back to entity-only overload
        Assert.Contains("MapCrudEndpoints<Acme.Domain.Entities.Product>()", source);
    }

    [Fact]
    public void FullEntity_EndpointMapping_UsesUserDtos_WhenAttributesPresent()
    {
        const string entityWithDtos = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Acme.Domain.Entities
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

            namespace Acme.Domain.Dtos
            {
                [CreateDtoFor(typeof(Acme.Domain.Entities.Product))]
                public record CreateProductRequest(string Name);

                [UpdateDtoFor(typeof(Acme.Domain.Entities.Product))]
                public record UpdateProductRequest(string? Name);
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entityWithDtos);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitEndpoints.g.cs");

        Assert.Contains("MapCrudEndpoints<Acme.Domain.Entities.Product, Acme.Domain.Dtos.CreateProductRequest, Acme.Domain.Dtos.UpdateProductRequest>()", source);
    }
}
