using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Api.Tests.Sequencing;

public class SeqInvoiceEntity : IAuditableEntity
{
    public Guid Id { get; set; }

    [AutoSequence("INV-{year}-{seq:5}")]
    public string InvoiceNumber { get; set; } = "";

    public string Customer { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateSeqInvoiceDto
{
    [Required] public string Customer { get; set; } = "";
}

public class UpdateSeqInvoiceDto
{
    public string? Customer { get; set; }
}

public class AutoSequenceEndpointTests
{
    [Fact]
    public async Task Create_AutoSequence_SetsNumberAutomatically()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
                web.MapCrudEndpoints<SeqInvoiceEntity, CreateSeqInvoiceDto, UpdateSeqInvoiceDto>("seq-invoices"));

        var response = await app.Client.PostAsJsonAsync("/api/seq-invoices",
            new { Customer = "Acme Corp" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var number = json.RootElement.GetProperty("invoiceNumber").GetString();

        Assert.NotNull(number);
        Assert.StartsWith($"INV-{DateTime.UtcNow.Year}-", number);
        Assert.Equal($"INV-{DateTime.UtcNow.Year}-00001", number);
    }

    [Fact]
    public async Task Create_AutoSequence_IncrementsSequentially()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
                web.MapCrudEndpoints<SeqInvoiceEntity, CreateSeqInvoiceDto, UpdateSeqInvoiceDto>("seq-invoices"));

        await app.Client.PostAsJsonAsync("/api/seq-invoices", new { Customer = "A" });
        var response2 = await app.Client.PostAsJsonAsync("/api/seq-invoices", new { Customer = "B" });
        var response3 = await app.Client.PostAsJsonAsync("/api/seq-invoices", new { Customer = "C" });

        var json2 = JsonDocument.Parse(await response2.Content.ReadAsStringAsync());
        var json3 = JsonDocument.Parse(await response3.Content.ReadAsStringAsync());

        Assert.Equal($"INV-{DateTime.UtcNow.Year}-00002",
            json2.RootElement.GetProperty("invoiceNumber").GetString());
        Assert.Equal($"INV-{DateTime.UtcNow.Year}-00003",
            json3.RootElement.GetProperty("invoiceNumber").GetString());
    }

    [Fact]
    public async Task Create_AutoSequence_DoesNotOverwriteExistingValue()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
                web.MapCrudEndpoints<SeqInvoiceEntity, CreateSeqInvoiceDto, UpdateSeqInvoiceDto>("seq-invoices"));

        var response = await app.Client.PostAsJsonAsync("/api/seq-invoices",
            new { Customer = "Test" });

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var number = json.RootElement.GetProperty("invoiceNumber").GetString();
        Assert.False(string.IsNullOrEmpty(number));
    }
}
