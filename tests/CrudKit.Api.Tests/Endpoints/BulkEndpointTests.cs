using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Configuration;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.Api.Tests.Endpoints;

public class BulkEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task BulkCount_WithFilters_ReturnsFilteredCount()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Alpha", Price = 10.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Beta", Price = 20.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Gamma", Price = 30.0 });

        // Count all
        var allResponse = await app.Client.GetAsync("/api/products/bulk-count");
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        var allDoc = JsonDocument.Parse(await allResponse.Content.ReadAsStringAsync());
        Assert.Equal(3, allDoc.RootElement.GetProperty("count").GetInt64());

        // Count with filter
        var filteredResponse = await app.Client.GetAsync("/api/products/bulk-count?name=eq:Alpha");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        var filteredDoc = JsonDocument.Parse(await filteredResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, filteredDoc.RootElement.GetProperty("count").GetInt64());
    }

    [Fact]
    public async Task BulkDelete_DeletesMatchingRecords()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Keep", Price = 10.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Remove", Price = 20.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Remove", Price = 30.0 });

        var deleteResponse = await app.Client.PostAsJsonAsync("/api/products/bulk-delete",
            new BulkDeleteRequest { Filters = new() { ["name"] = "eq:Remove" } });
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var deleteDoc = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, deleteDoc.RootElement.GetProperty("affected").GetInt32());

        // Verify only "Keep" remains
        var listResponse = await app.Client.GetAsync("/api/products");
        var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, listDoc.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task BulkUpdate_UpdatesMatchingRecords()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Old", Price = 10.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Old", Price = 20.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Other", Price = 30.0 });

        var updateResponse = await app.Client.PostAsJsonAsync("/api/products/bulk-update",
            new BulkUpdateRequest
            {
                Filters = new() { ["name"] = "eq:Old" },
                Values = new() { ["name"] = "New" }
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updateDoc = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, updateDoc.RootElement.GetProperty("affected").GetInt32());

        // Verify updated records
        var countResponse = await app.Client.GetAsync("/api/products/bulk-count?name=eq:New");
        var countDoc = JsonDocument.Parse(await countResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, countDoc.RootElement.GetProperty("count").GetInt64());
    }

    [Fact]
    public async Task BulkDelete_ExceedsBulkLimit_Returns400()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureServices: services =>
            {
                // Override options with a very low bulk limit
                var opts = services.FirstOrDefault(s => s.ServiceType == typeof(Configuration.CrudKitApiOptions));
                if (opts != null) services.Remove(opts);
                services.AddSingleton(new Configuration.CrudKitApiOptions { BulkLimit = 1 });
            },
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
            });

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "A", Price = 1.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "B", Price = 2.0 });

        // Try to bulk-delete all — should exceed limit of 1
        var deleteResponse = await app.Client.PostAsJsonAsync("/api/products/bulk-delete",
            new BulkDeleteRequest { Filters = new() });
        Assert.Equal(HttpStatusCode.BadRequest, deleteResponse.StatusCode);
    }
}
