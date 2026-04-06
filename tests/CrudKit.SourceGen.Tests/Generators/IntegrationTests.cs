using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

/// <summary>
/// End-to-end: one fully-featured entity verifies all expected files are generated.
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
                Table = "Products",
                MultiTenant = true,
                EnableBulkUpdate = true)]
            public class Product : IAuditableEntity, ISoftDeletable, IMultiTenant
            {
                public Guid Id { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public DateTime? DeletedAt { get; set; }
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
    public void FullEntity_GeneratesAllExpectedFiles()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);

        var fileNames = result.GeneratedTrees.Select(t => System.IO.Path.GetFileName(t.FilePath)).ToList();

        Assert.Contains("ProductCreateDto.g.cs", fileNames);
        Assert.Contains("ProductUpdateDto.g.cs", fileNames);
        Assert.Contains("ProductResponseDto.g.cs", fileNames);
        Assert.Contains("ProductMapper.g.cs", fileNames);
        Assert.Contains("ProductHooks.g.cs", fileNames);
        Assert.Contains("CrudKitEndpoints.g.cs", fileNames);
        Assert.Contains("CrudKitMappers.g.cs", fileNames);
    }

    [Fact]
    public void FullEntity_NoCrudDiagnosticsEmitted()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);

        var crudDiags = result.Diagnostics.Where(d => d.Id.StartsWith("CRUD")).ToList();
        Assert.Empty(crudDiags);
    }

    [Fact]
    public void FullEntity_CreateDto_HasCorrectShape()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductCreateDto.g.cs");

        // Required props
        Assert.Contains("[Required]", source);
        Assert.Contains("[MaxLength(200)]", source);
        Assert.Contains("string Name", source);
        Assert.Contains("[Range(0.01, 999999.99)]", source);
        Assert.Contains("decimal Price", source);

        // System fields excluded
        Assert.DoesNotContain("CreatedAt", source);
        Assert.DoesNotContain("TenantId", source);

        // Protected excluded
        Assert.DoesNotContain("CreatedById", source);
    }

    [Fact]
    public void FullEntity_UpdateDto_WrapsInOptional()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductUpdateDto.g.cs");

        Assert.Contains("Optional<string> Name", source);
        Assert.Contains("Optional<decimal> Price", source);

        // SkipUpdate and Protected excluded
        Assert.DoesNotContain("Sku", source);
        Assert.DoesNotContain("CreatedById", source);
    }

    [Fact]
    public void FullEntity_ResponseDto_ExcludesSkipResponse()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductResponseDto.g.cs");

        Assert.DoesNotContain("InternalTag", source);
        Assert.Contains("DateTime? DeletedAt", source);
        Assert.Contains("string TenantId", source);
    }

    [Fact]
    public void FullEntity_Mapper_IsComplete()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductMapper.g.cs");

        // Full CRUD entity → ICrudMapper
        Assert.Contains("ICrudMapper<Product, CreateProduct, UpdateProduct, ProductResponse>", source);
        Assert.Contains("public ProductResponse Map(Product entity)", source);
        Assert.Contains("public IQueryable<ProductResponse> Project(IQueryable<Product> query)", source);
        Assert.Contains("public Product FromCreateDto(CreateProduct dto)", source);
        Assert.Contains("public void ApplyUpdate(Product entity, UpdateProduct dto)", source);
    }

    [Fact]
    public void FullEntity_EndpointMapping_UsesCrudOverload()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitEndpoints.g.cs");

        Assert.Contains("MapCrudEndpoints<Product, CreateProduct, UpdateProduct,", source);
    }

    [Fact]
    public void FullEntity_DiRegistration_RegistersMapper()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        // Full CRUD entity → registered as ICrudMapper with individual interface forwarding
        Assert.Contains("ICrudMapper<Product, CreateProduct, UpdateProduct, ProductResponse>, ProductMapper", source);
        Assert.Contains("IResponseMapper<Product, ProductResponse>", source);
        Assert.Contains("ICreateMapper<Product, CreateProduct>", source);
        Assert.Contains("IUpdateMapper<Product, UpdateProduct>", source);
    }
}
