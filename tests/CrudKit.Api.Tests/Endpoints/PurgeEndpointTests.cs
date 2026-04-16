using System.Net;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace CrudKit.Api.Tests.Endpoints;

/// <summary>
/// Tests for the two purge endpoints:
///   DELETE /api/{route}/{id}/purge  — single item hard delete
///   DELETE /api/{route}/purge?olderThan=N — bulk hard delete
/// </summary>
public class PurgeEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // ------------------------------------------------------------------ //
    // Single-item purge: DELETE /api/soft-products/{id}/purge             //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task SinglePurge_Returns204_ForSoftDeletedRecord()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<SoftProductEntity, CreateSoftProductDto, CreateSoftProductDto>("soft-products");
        });

        // Create entity
        var createResp = await app.Client.PostAsJsonAsync("/api/soft-products", new { Name = "ToHardDelete" });
        var id = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        // Soft-delete it first
        var softDeleteResp = await app.Client.DeleteAsync($"/api/soft-products/{id}");
        Assert.Equal(HttpStatusCode.NoContent, softDeleteResp.StatusCode);

        // Hard-delete (purge)
        var purgeResp = await app.Client.DeleteAsync($"/api/soft-products/{id}/purge");
        Assert.Equal(HttpStatusCode.NoContent, purgeResp.StatusCode);

        // Verify the record is completely gone — even IgnoreQueryFilters won't find it
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiTestDbContext>();
        var exists = await db.SoftProducts.IgnoreQueryFilters().AnyAsync(e => e.Id == Guid.Parse(id));
        Assert.False(exists, "Entity must be permanently removed from the database.");
    }

    [Fact]
    public async Task SinglePurge_Returns400_ForActiveRecord()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<SoftProductEntity, CreateSoftProductDto, CreateSoftProductDto>("soft-products");
        });

        // Create entity but do NOT soft-delete it
        var createResp = await app.Client.PostAsJsonAsync("/api/soft-products", new { Name = "Active" });
        var id = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        // Purge should fail because the entity is not soft-deleted
        var purgeResp = await app.Client.DeleteAsync($"/api/soft-products/{id}/purge");
        Assert.Equal(HttpStatusCode.BadRequest, purgeResp.StatusCode);
    }

    [Fact]
    public async Task SinglePurge_Returns404_ForNonExistentId()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<SoftProductEntity, CreateSoftProductDto, CreateSoftProductDto>("soft-products");
        });

        var randomId = Guid.NewGuid();
        var purgeResp = await app.Client.DeleteAsync($"/api/soft-products/{randomId}/purge");
        Assert.Equal(HttpStatusCode.NotFound, purgeResp.StatusCode);
    }

    [Fact]
    public async Task SinglePurge_NotAvailable_ForNonSoftDeletableEntity()
    {
        // ProductEntity does not implement ISoftDeletable — the purge route must not be registered
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        var randomId = Guid.NewGuid();
        var purgeResp = await app.Client.DeleteAsync($"/api/products/{randomId}/purge");

        // 404 (no route) or 405 (method not allowed) both indicate the endpoint does not exist
        Assert.True(
            purgeResp.StatusCode == HttpStatusCode.NotFound ||
            purgeResp.StatusCode == HttpStatusCode.MethodNotAllowed,
            $"Expected 404 or 405, got {purgeResp.StatusCode}");
    }

    // ------------------------------------------------------------------ //
    // Bulk purge: DELETE /api/soft-products/purge?olderThan=N             //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task BulkPurge_DeletesOldRecords()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<SoftProductEntity, CreateSoftProductDto, CreateSoftProductDto>("soft-products");
        });

        // Create and soft-delete two entities
        var r1 = await app.Client.PostAsJsonAsync("/api/soft-products", new { Name = "Old1" });
        var id1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        var r2 = await app.Client.PostAsJsonAsync("/api/soft-products", new { Name = "Old2" });
        var id2 = JsonDocument.Parse(await r2.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        await app.Client.DeleteAsync($"/api/soft-products/{id1}");
        await app.Client.DeleteAsync($"/api/soft-products/{id2}");

        // Backdate DeletedAt to 60 days ago so the bulk purge cutoff picks them up
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiTestDbContext>();
            var cutoff = DateTime.UtcNow.AddDays(-60);
            await db.SoftProducts.IgnoreQueryFilters()
                .Where(e => e.Id == Guid.Parse(id1) || e.Id == Guid.Parse(id2))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.DeletedAt, cutoff));
        }

        // Purge records deleted more than 30 days ago — both should be removed
        var purgeResp = await app.Client.DeleteAsync("/api/soft-products/purge?olderThan=30");
        Assert.Equal(HttpStatusCode.OK, purgeResp.StatusCode);

        var doc = JsonDocument.Parse(await purgeResp.Content.ReadAsStringAsync());
        Assert.Equal(2, doc.RootElement.GetProperty("purged").GetInt32());

        // Verify records are completely gone
        using var verifyScope = app.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApiTestDbContext>();
        var remaining = await verifyDb.SoftProducts.IgnoreQueryFilters()
            .Where(e => e.Id == Guid.Parse(id1) || e.Id == Guid.Parse(id2))
            .CountAsync();
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task BulkPurge_KeepsRecentRecords()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<SoftProductEntity, CreateSoftProductDto, CreateSoftProductDto>("soft-products");
        });

        // Create and soft-delete an entity — DeletedAt will be "just now"
        var r1 = await app.Client.PostAsJsonAsync("/api/soft-products", new { Name = "Recent" });
        var id1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        await app.Client.DeleteAsync($"/api/soft-products/{id1}");

        // Purge with olderThan=30 — the record was deleted moments ago, so it must NOT be removed
        var purgeResp = await app.Client.DeleteAsync("/api/soft-products/purge?olderThan=30");
        Assert.Equal(HttpStatusCode.OK, purgeResp.StatusCode);

        var doc = JsonDocument.Parse(await purgeResp.Content.ReadAsStringAsync());
        Assert.Equal(0, doc.RootElement.GetProperty("purged").GetInt32());

        // Confirm the record is still present (soft-deleted but not hard-deleted)
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiTestDbContext>();
        var exists = await db.SoftProducts.IgnoreQueryFilters().AnyAsync(e => e.Id == Guid.Parse(id1));
        Assert.True(exists, "Recently soft-deleted record must not be purged.");
    }

    [Fact]
    public async Task BulkPurge_Returns400_WhenOlderThanIsZero()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<SoftProductEntity, CreateSoftProductDto, CreateSoftProductDto>("soft-products");
        });

        // olderThan=0 is below the minimum of 1, must return 400
        var response = await app.Client.DeleteAsync("/api/soft-products/purge?olderThan=0");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
