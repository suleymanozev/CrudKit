using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using Xunit;

namespace CrudKit.Api.Tests.Endpoints;

public class ImportExportTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ---- Export ----

    [Fact]
    public async Task Export_ReturnsCsv_ForExportableEntity()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Widget", Price = 9.99 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Gadget", Price = 19.99 });

        var response = await app.Client.GetAsync("/api/products/export?format=csv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Header + 2 data rows
        Assert.True(lines.Length >= 3, $"Expected at least 3 lines, got {lines.Length}");

        // Header should contain Name and Price
        Assert.Contains("Name", lines[0]);
        Assert.Contains("Price", lines[0]);

        // Data rows should contain the product names
        var dataContent = string.Join("\n", lines.Skip(1));
        Assert.Contains("Widget", dataContent);
        Assert.Contains("Gadget", dataContent);
    }

    [Fact]
    public async Task Export_AppliesFilters()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Cheap", Price = 1.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Expensive", Price = 100.0 });

        var response = await app.Client.GetAsync("/api/products/export?format=csv&name=eq:Cheap");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Header + 1 filtered row
        Assert.Equal(2, lines.Length);
        Assert.Contains("Cheap", lines[1]);
        Assert.DoesNotContain("Expensive", csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last());
    }

    [Fact]
    public async Task Export_ExcludesNotExportableFields()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Test", Price = 5.0 });

        var response = await app.Client.GetAsync("/api/products/export?format=csv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var csv = await response.Content.ReadAsStringAsync();
        var header = csv.Split('\n')[0];

        // InternalNotes is marked [NotExportable] — should not appear in export
        Assert.DoesNotContain("InternalNotes", header);
        // Name should be present
        Assert.Contains("Name", header);
    }

    [Fact]
    public async Task Export_Returns404_ForNonExportableEntity()
    {
        // OrderEntity does not have [Exportable], so /export endpoint should not exist
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<OrderEntity, CreateOrderDto, UpdateOrderDto>("orders");
        });

        var response = await app.Client.GetAsync("/api/orders/export?format=csv");
        // The endpoint does not exist, so it should return 404
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Import ----

    [Fact]
    public async Task Import_CreatesEntities_FromCsv()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        var csv = "Name,Price\nAlpha,10.5\nBeta,20.0\n";
        var content = CreateMultipartCsvContent(csv);

        var response = await app.Client.PostAsync("/api/products/import", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, doc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("created").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("failed").GetInt32());

        // Verify entities were actually created
        var listResponse = await app.Client.GetAsync("/api/products");
        var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, listDoc.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Import_ReturnsErrors_ForInvalidRows()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        // "notanumber" cannot be converted to decimal for Price
        var csv = "Name,Price\nGood,10.0\nBad,notanumber\n";
        var content = CreateMultipartCsvContent(csv);

        var response = await app.Client.PostAsync("/api/products/import", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = doc.RootElement.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0, "Expected at least one error for invalid row");
    }

    [Fact]
    public async Task Import_Returns400_WhenNoFile()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        // Send multipart form with an empty file (zero bytes)
        var content = new MultipartFormDataContent();
        var emptyFile = new ByteArrayContent([]);
        emptyFile.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(emptyFile, "file", "empty.csv");
        var response = await app.Client.PostAsync("/api/products/import", content);

        // Should return 400 via AppError (file.Length == 0)
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Helper to create multipart form content with a CSV file attachment.
    /// </summary>
    private static MultipartFormDataContent CreateMultipartCsvContent(string csv)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "import.csv");
        return content;
    }
}
