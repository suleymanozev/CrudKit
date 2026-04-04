using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using Xunit;

namespace CrudKit.Api.Tests.Endpoints;

public class DetailEndpointMapperTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static Task<TestWebApp> CreateApp() => TestWebApp.CreateAsync(configureEndpoints: web =>
    {
        web.MapCrudEndpoints<InvoiceEntity, CreateInvoiceDto, UpdateInvoiceDto>("invoices")
            .WithDetail<InvoiceLineEntity, CreateInvoiceLineDto>("lines", "InvoiceId");
    });

    private static async Task<string> CreateInvoice(TestWebApp app, string title = "Test Invoice")
    {
        var response = await app.Client.PostAsJsonAsync("/api/invoices", new { Title = title });
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task ListDetails_ReturnsDetailsForMaster()
    {
        await using var app = await CreateApp();
        var invoiceId = await CreateInvoice(app);

        // Create two lines
        await app.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lines",
            new { Description = "Line 1", Amount = 10.0 });
        await app.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lines",
            new { Description = "Line 2", Amount = 20.0 });

        var response = await app.Client.GetAsync($"/api/invoices/{invoiceId}/lines");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task GetDetail_ReturnsSpecificDetail()
    {
        await using var app = await CreateApp();
        var invoiceId = await CreateInvoice(app);

        var createResponse = await app.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lines",
            new { Description = "Detail Item", Amount = 50.0 });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var lineId = created.RootElement.GetProperty("id").GetString()!;

        var response = await app.Client.GetAsync($"/api/invoices/{invoiceId}/lines/{lineId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Detail Item", doc.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public async Task CreateDetail_AutoSetsForeignKey()
    {
        await using var app = await CreateApp();
        var invoiceId = await CreateInvoice(app);

        var response = await app.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lines",
            new { Description = "Auto FK", Amount = 100.0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(invoiceId, doc.RootElement.GetProperty("invoiceId").GetString());
    }

    [Fact]
    public async Task DeleteDetail_Removes()
    {
        await using var app = await CreateApp();
        var invoiceId = await CreateInvoice(app);

        var createResponse = await app.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lines",
            new { Description = "To Delete", Amount = 5.0 });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var lineId = created.RootElement.GetProperty("id").GetString()!;

        var deleteResponse = await app.Client.DeleteAsync($"/api/invoices/{invoiceId}/lines/{lineId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify it's gone
        var getResponse = await app.Client.GetAsync($"/api/invoices/{invoiceId}/lines/{lineId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task BatchUpsert_ReplacesAllDetails()
    {
        await using var app = await CreateApp();
        var invoiceId = await CreateInvoice(app);

        // Create initial lines
        await app.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lines",
            new { Description = "Old 1", Amount = 1.0 });
        await app.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lines",
            new { Description = "Old 2", Amount = 2.0 });

        // Batch upsert replaces all with new set
        var batchResponse = await app.Client.PutAsJsonAsync($"/api/invoices/{invoiceId}/lines/batch",
            new[]
            {
                new { Description = "New A", Amount = 100.0 },
                new { Description = "New B", Amount = 200.0 },
                new { Description = "New C", Amount = 300.0 }
            });
        Assert.Equal(HttpStatusCode.OK, batchResponse.StatusCode);

        var batchDoc = JsonDocument.Parse(await batchResponse.Content.ReadAsStringAsync());
        Assert.Equal(3, batchDoc.RootElement.GetArrayLength());

        // Verify old lines are replaced
        var listResponse = await app.Client.GetAsync($"/api/invoices/{invoiceId}/lines");
        var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Equal(3, listDoc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ListDetails_MasterNotFound_Returns404()
    {
        await using var app = await CreateApp();

        var response = await app.Client.GetAsync("/api/invoices/nonexistent/lines");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
