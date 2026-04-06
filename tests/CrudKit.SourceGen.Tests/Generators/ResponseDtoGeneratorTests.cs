using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

public class ResponseDtoGeneratorTests
{
    private const string CategoryEntity = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;

        namespace Store.Entities
        {
            [CrudEntity(Table = "Categories")]
            public class Category : IAuditableEntity, ISoftDeletable
            {
                public Guid Id { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public DateTime? DeletedAt { get; set; }

                public string Name { get; set; } = string.Empty;

                [SkipResponse]
                public string InternalTag { get; set; } = string.Empty;

                public int SortOrder { get; set; }
            }
        }
        """;

    [Fact]
    public void ResponseDto_ContainsSystemFields()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(CategoryEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CategoryResponseDto.g.cs");

        Assert.Contains("Guid Id", source);
        Assert.Contains("DateTime CreatedAt", source);
        Assert.Contains("DateTime UpdatedAt", source);
    }

    [Fact]
    public void ResponseDto_ContainsDeletedAt_WhenSoftDeletable()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(CategoryEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CategoryResponseDto.g.cs");

        Assert.Contains("DateTime? DeletedAt", source);
    }

    [Fact]
    public void ResponseDto_ExcludesSkipResponseProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(CategoryEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CategoryResponseDto.g.cs");

        Assert.DoesNotContain("InternalTag", source);
    }

    [Fact]
    public void ResponseDto_IncludesUserProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(CategoryEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CategoryResponseDto.g.cs");

        Assert.Contains("string Name", source);
        Assert.Contains("int SortOrder", source);
    }

    [Fact]
    public void ResponseDto_AlwaysGenerated_ForReadOnlyEntity()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Store.Entities
            {
                [CrudEntity(Table = "ReadViews", ReadOnly = true)]
                public class ReadView : IAuditableEntity
                {
                    public Guid Id { get; set; }
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Label { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);

        var responseDtoTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("ResponseDto"));

        Assert.NotNull(responseDtoTree);
    }

    [Fact]
    public void ResponseDto_IncludesTenantId_WhenMultiTenant()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Store.Entities
            {
                [CrudEntity(Table = "Tenanted", MultiTenant = true)]
                public class Tenanted : IAuditableEntity, IMultiTenant
                {
                    public Guid Id { get; set; }
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string TenantId { get; set; } = string.Empty;
                    public string Data { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "TenantedResponseDto.g.cs");

        Assert.Contains("string TenantId", source);
    }
}
