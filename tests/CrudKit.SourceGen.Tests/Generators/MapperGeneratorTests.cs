using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

public class MapperGeneratorTests
{
    private const string InvoiceEntity = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;

        namespace Billing.Entities
        {
            [CrudEntity(Table = "Invoices")]
            public class Invoice : IAuditableEntity, ISoftDeletable
            {
                public Guid Id { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public DateTime? DeletedAt { get; set; }

                public string Number { get; set; } = string.Empty;
                public decimal Amount { get; set; }

                [SkipResponse]
                public string PaymentToken { get; set; } = string.Empty;
            }
        }
        """;

    [Fact]
    public void Mapper_ImplementsICrudMapper_WhenAllOpsEnabled()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        // InvoiceEntity has create + update enabled → ICrudMapper
        Assert.Contains("ICrudMapper<Invoice, CreateInvoice, UpdateInvoice, InvoiceResponse>", source);
    }

    [Fact]
    public void Mapper_ImplementsIResponseMapper_WhenReadOnly()
    {
        const string readonlyEntity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Billing.Entities
            {
                [CrudEntity(Table = "Summaries", ReadOnly = true)]
                public class Summary : IAuditableEntity
                {
                    public Guid Id { get; set; }
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public decimal Total { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(readonlyEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "SummaryMapper.g.cs");

        Assert.Contains("IResponseMapper<Summary, SummaryResponse>", source);
        Assert.DoesNotContain("ICrudMapper", source);
        Assert.DoesNotContain("ICreateMapper", source);
        Assert.DoesNotContain("IUpdateMapper", source);
    }

    [Fact]
    public void Mapper_HasMapMethod()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        Assert.Contains("public InvoiceResponse Map(Invoice entity)", source);
    }

    [Fact]
    public void Mapper_HasProjectMethod()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        Assert.Contains("public IQueryable<InvoiceResponse> Project(IQueryable<Invoice> query)", source);
    }

    [Fact]
    public void Mapper_HasFromCreateDtoMethod()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        Assert.Contains("public Invoice FromCreateDto(CreateInvoice dto)", source);
    }

    [Fact]
    public void Mapper_HasApplyUpdateMethod()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        Assert.Contains("public void ApplyUpdate(Invoice entity, UpdateInvoice dto)", source);
    }

    [Fact]
    public void Mapper_ProjectUsesSelectLambda()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        Assert.Contains("query.Select(entity => new InvoiceResponse(", source);
    }

    [Fact]
    public void Mapper_ExcludesSkipResponseFields()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        // SkipResponse fields must not appear in the response record constructor
        // (i.e., not mapped in Map() or Project() to the response DTO).
        // They may still appear in FromCreateDto/ApplyUpdate since those deal with input DTOs.
        Assert.DoesNotContain("new InvoiceResponse(\n            entity.PaymentToken", source);

        // Verify that response-specific lines do not include PaymentToken
        var lines = source.Split('\n');
        var responseConstructorLines = lines
            .Where(l => l.Contains("InvoiceResponse(") || l.TrimStart().StartsWith("entity."))
            .ToList();
        // In the Map/Project methods the response constructor args should not reference PaymentToken
        var responseBlock = false;
        foreach (var line in lines)
        {
            if (line.Contains("new InvoiceResponse("))
                responseBlock = true;
            if (responseBlock && line.Contains("PaymentToken"))
                Assert.Fail("PaymentToken should not appear in response mapping");
            if (responseBlock && line.Contains(");"))
                responseBlock = false;
        }
    }

    [Fact]
    public void Mapper_IncludesDeletedAt_WhenSoftDeletable()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        Assert.Contains("entity.DeletedAt", source);
    }

    [Fact]
    public void Mapper_NoFromCreateDto_WhenCreateDisabled()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Billing.Entities
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
        var source = GeneratorTestHelper.GetGeneratedSource(result, "LogMapper.g.cs");

        Assert.DoesNotContain("FromCreateDto", source);
        Assert.DoesNotContain("ICreateMapper", source);
        // Should still have update and response
        Assert.Contains("IUpdateMapper", source);
        Assert.Contains("IResponseMapper", source);
    }

    [Fact]
    public void Mapper_NoApplyUpdate_WhenUpdateDisabled()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Billing.Entities
            {
                [CrudEntity(Table = "Immutable", EnableUpdate = false)]
                public class ImmutableLog : IAuditableEntity
                {
                    public Guid Id { get; set; }
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Data { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ImmutableLogMapper.g.cs");

        Assert.DoesNotContain("ApplyUpdate", source);
        Assert.DoesNotContain("IUpdateMapper", source);
        // Should still have create and response
        Assert.Contains("ICreateMapper", source);
        Assert.Contains("IResponseMapper", source);
    }
}
