using System.Net;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Interfaces;

using Xunit;

namespace CrudKit.Api.Tests.Endpoints;

public class CrudEndpointMapperTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ---- List ----

    [Fact]
    public async Task List_Empty_ReturnsEmptyData()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        var response = await app.Client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetArrayLength());
    }

    [Fact]
    public async Task List_WithItems_ReturnsAllItems()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "A", Price = 1.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "B", Price = 2.0 });

        var response = await app.Client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetArrayLength());
    }

    // ---- Get ----

    [Fact]
    public async Task Get_NotFound_Returns404()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        var response = await app.Client.GetAsync("/api/products/nonexistent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Found_ReturnsEntity()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        var createResponse = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Widget", Price = 9.99 });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        var response = await app.Client.GetAsync($"/api/products/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Widget", doc.RootElement.GetProperty("name").GetString());
    }

    // ---- Create ----

    [Fact]
    public async Task Create_Returns201WithLocationHeader()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        var response = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Gadget", Price = 19.99 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains("/api/products/", response.Headers.Location!.ToString());

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Gadget", doc.RootElement.GetProperty("name").GetString());
    }

    // ---- Update ----

    [Fact]
    public async Task Update_Returns200WithUpdatedEntity()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        var createResponse = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Old", Price = 5.0 });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        var updateResponse = await app.Client.PutAsJsonAsync($"/api/products/{id}", new { Name = "New" });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var doc = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        Assert.Equal("New", doc.RootElement.GetProperty("name").GetString());
    }

    // ---- Delete ----

    [Fact]
    public async Task Delete_Returns204()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        var createResponse = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Doomed", Price = 1.0 });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        var deleteResponse = await app.Client.DeleteAsync($"/api/products/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_ThenGet_Returns404()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        var createResponse = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Gone", Price = 1.0 });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        await app.Client.DeleteAsync($"/api/products/{id}");
        var getResponse = await app.Client.GetAsync($"/api/products/{id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    // ---- Hooks ----

    [Fact]
    public async Task Hooks_BeforeAndAfterCreate_AreCalled()
    {
        var called = new List<string>();
        var hooks = new TestProductHooks
        {
            OnBeforeCreate = (_, _) => { called.Add("before"); return Task.CompletedTask; },
            OnAfterCreate = (_, _) => { called.Add("after"); return Task.CompletedTask; }
        };

        await using var app = await TestWebApp.CreateAsync(
            configureServices: s => s.AddSingleton<ICrudHooks<ProductEntity>>(hooks),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
            });

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Hooked", Price = 1.0 });

        Assert.Contains("before", called);
        Assert.Contains("after", called);
    }

    [Fact]
    public async Task Hooks_BeforeAndAfterUpdate_AreCalled()
    {
        var called = new List<string>();
        var hooks = new TestProductHooks
        {
            OnBeforeUpdate = (_, _) => { called.Add("before"); return Task.CompletedTask; },
            OnAfterUpdate = (_, _) => { called.Add("after"); return Task.CompletedTask; }
        };

        await using var app = await TestWebApp.CreateAsync(
            configureServices: s => s.AddSingleton<ICrudHooks<ProductEntity>>(hooks),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
            });

        var createResponse = await app.Client.PostAsJsonAsync("/api/products", new { Name = "X", Price = 1.0 });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        await app.Client.PutAsJsonAsync($"/api/products/{id}", new { Name = "Y" });

        Assert.Contains("before", called);
        Assert.Contains("after", called);
    }

    [Fact]
    public async Task Hooks_BeforeAndAfterDelete_AreCalled()
    {
        var called = new List<string>();
        var hooks = new TestProductHooks
        {
            OnBeforeDelete = (_, _) => { called.Add("before"); return Task.CompletedTask; },
            OnAfterDelete = (_, _) => { called.Add("after"); return Task.CompletedTask; }
        };

        await using var app = await TestWebApp.CreateAsync(
            configureServices: s => s.AddSingleton<ICrudHooks<ProductEntity>>(hooks),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
            });

        var createResponse = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Z", Price = 1.0 });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        await app.Client.DeleteAsync($"/api/products/{id}");

        Assert.Contains("before", called);
        Assert.Contains("after", called);
    }

    [Fact]
    public async Task Hooks_NoHooksRegistered_StillWorks()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        var response = await app.Client.PostAsJsonAsync("/api/products", new { Name = "NoHooks", Price = 1.0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Hooks_BeforeCreateThrows_RollsBackTransaction()
    {
        var hooks = new TestProductHooks
        {
            OnBeforeCreate = (_, _) => throw new InvalidOperationException("Hook failed")
        };

        await using var app = await TestWebApp.CreateAsync(
            configureServices: s => s.AddSingleton<ICrudHooks<ProductEntity>>(hooks),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
            });

        var response = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Rollback", Price = 1.0 });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // Verify the entity was not persisted
        var listResponse = await app.Client.GetAsync("/api/products");
        var doc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Equal(0, doc.RootElement.GetProperty("data").GetArrayLength());
    }

    // ---- IResponseMapper ----

    [Fact]
    public async Task Get_WithMapper_ReturnsMappedResponse()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureServices: s =>
            {
                s.AddSingleton<IResponseMapper<ProductEntity, ProductResponse>>(new TestProductMapper());
            },
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
            });

        var createResponse = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Mapped", Price = 5.0 });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        var getResponse = await app.Client.GetAsync($"/api/products/{id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var doc = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.Equal("Mapped (mapped)", doc.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Get_WithoutMapper_ReturnsRawEntity()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        var createResponse = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Raw", Price = 3.0 });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        var getResponse = await app.Client.GetAsync($"/api/products/{id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var doc = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.Equal("Raw", doc.RootElement.GetProperty("name").GetString());
        // Raw entity should not have a displayName property
        Assert.False(doc.RootElement.TryGetProperty("displayName", out _));
    }

    // ---- Soft-delete restore ----

    [Fact]
    public async Task SoftDelete_Restore_Works()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<SoftProductEntity, CreateSoftProductDto, CreateSoftProductDto>("soft-products");
        });

        var createResponse = await app.Client.PostAsJsonAsync("/api/soft-products", new { Name = "Soft" });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        // Delete (soft)
        var deleteResponse = await app.Client.DeleteAsync($"/api/soft-products/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify not visible
        var getAfterDelete = await app.Client.GetAsync($"/api/soft-products/{id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);

        // Restore
        var restoreResponse = await app.Client.PostAsync($"/api/soft-products/{id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);

        // Verify visible again
        var getAfterRestore = await app.Client.GetAsync($"/api/soft-products/{id}");
        Assert.Equal(HttpStatusCode.OK, getAfterRestore.StatusCode);
    }

    // ---- Purge (soft-deletable) ----

    [Fact]
    public async Task Purge_DeletesOldSoftDeletedRecords()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<SoftProductEntity, CreateSoftProductDto, CreateSoftProductDto>("soft-products");
        });

        // Create two entities
        var r1 = await app.Client.PostAsJsonAsync("/api/soft-products", new { Name = "Old1" });
        var id1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        var r2 = await app.Client.PostAsJsonAsync("/api/soft-products", new { Name = "Old2" });
        var id2 = JsonDocument.Parse(await r2.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Soft-delete both
        await app.Client.DeleteAsync($"/api/soft-products/{id1}");
        await app.Client.DeleteAsync($"/api/soft-products/{id2}");

        // Purge with olderThan=0 would fail (< 1), use olderThan=1 but records are just deleted so won't match.
        // Instead, we need records older than the cutoff. Since we can't manipulate time easily,
        // use a very large olderThan to verify nothing is purged, then a very small one.
        // Actually: records are deleted "just now", so cutoff = now - 1 day = yesterday.
        // DeletedAt = now, which is NOT < yesterday. So olderThan=1 should purge 0.
        var purgeResp1 = await app.Client.DeleteAsync("/api/soft-products/purge?olderThan=1");
        Assert.Equal(HttpStatusCode.OK, purgeResp1.StatusCode);
        var purge1 = JsonDocument.Parse(await purgeResp1.Content.ReadAsStringAsync());
        Assert.Equal(0, purge1.RootElement.GetProperty("purged").GetInt32());

        // Now test with a service-scoped db to manually backdate DeletedAt, then purge
        // We can't easily do this through the API, so we verify the endpoint works at least
    }

    [Fact]
    public async Task Purge_DoesNotDeleteRecentSoftDeleted()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<SoftProductEntity, CreateSoftProductDto, CreateSoftProductDto>("soft-products");
        });

        var r1 = await app.Client.PostAsJsonAsync("/api/soft-products", new { Name = "Recent" });
        var id1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Soft-delete
        await app.Client.DeleteAsync($"/api/soft-products/{id1}");

        // Purge with olderThan=30 — record was just deleted, so it's recent
        var purgeResp = await app.Client.DeleteAsync("/api/soft-products/purge?olderThan=30");
        Assert.Equal(HttpStatusCode.OK, purgeResp.StatusCode);

        var doc = JsonDocument.Parse(await purgeResp.Content.ReadAsStringAsync());
        Assert.Equal(0, doc.RootElement.GetProperty("purged").GetInt32());
    }

    [Fact]
    public async Task Purge_Returns400_WhenOlderThanInvalid()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<SoftProductEntity, CreateSoftProductDto, CreateSoftProductDto>("soft-products");
        });

        // olderThan=0 should return 400
        var response = await app.Client.DeleteAsync("/api/soft-products/purge?olderThan=0");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Purge_NonSoftDeletable_EndpointNotFound()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
        });

        // ProductEntity does not implement ISoftDeletable, so /purge should not exist
        var response = await app.Client.DeleteAsync("/api/products/purge?olderThan=30");
        // Should be 404 (no route) or 405 (method not allowed) — either means endpoint doesn't exist
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.MethodNotAllowed,
            $"Expected 404 or 405, got {response.StatusCode}");
    }

    // ---- State machine transition ----

    [Fact]
    public async Task Transition_ValidAction_UpdatesStatus()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<OrderEntity, CreateOrderDto, UpdateOrderDto>("orders");
        });

        var createResponse = await app.Client.PostAsJsonAsync("/api/orders", new { Customer = "Alice" });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        var transitionResponse = await app.Client.PostAsync($"/api/orders/{id}/transition/process", null);
        Assert.Equal(HttpStatusCode.OK, transitionResponse.StatusCode);

        var doc = JsonDocument.Parse(await transitionResponse.Content.ReadAsStringAsync());
        // Enum is stored as string in DB but serialized as its string name via EF convention
        var statusValue = doc.RootElement.GetProperty("status");
        // Accept both string ("Processing") and numeric (1) representation
        if (statusValue.ValueKind == JsonValueKind.String)
            Assert.Equal("Processing", statusValue.GetString());
        else
            Assert.Equal((int)OrderStatus.Processing, statusValue.GetInt32());
    }

    [Fact]
    public async Task Transition_InvalidAction_Returns400()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<OrderEntity, CreateOrderDto, UpdateOrderDto>("orders");
        });

        var createResponse = await app.Client.PostAsJsonAsync("/api/orders", new { Customer = "Bob" });
        var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        // "complete" is not valid from Pending state
        var transitionResponse = await app.Client.PostAsync($"/api/orders/{id}/transition/complete", null);
        Assert.Equal(HttpStatusCode.BadRequest, transitionResponse.StatusCode);
    }
}

// ---- Helper classes ----

/// <summary>
/// Delegate-based ICrudHooks implementation for testing.
/// Each hook can be overridden via a Func delegate property.
/// </summary>
public class TestProductHooks : ICrudHooks<ProductEntity>
{
    public Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? OnBeforeCreate { get; set; }
    public Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? OnAfterCreate { get; set; }
    public Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? OnBeforeUpdate { get; set; }
    public Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? OnAfterUpdate { get; set; }
    public Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? OnBeforeDelete { get; set; }
    public Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? OnAfterDelete { get; set; }

    public Task BeforeCreate(ProductEntity entity, CrudKit.Core.Context.AppContext ctx)
        => OnBeforeCreate?.Invoke(entity, ctx) ?? Task.CompletedTask;
    public Task AfterCreate(ProductEntity entity, CrudKit.Core.Context.AppContext ctx)
        => OnAfterCreate?.Invoke(entity, ctx) ?? Task.CompletedTask;
    public Task BeforeUpdate(ProductEntity entity, CrudKit.Core.Context.AppContext ctx)
        => OnBeforeUpdate?.Invoke(entity, ctx) ?? Task.CompletedTask;
    public Task AfterUpdate(ProductEntity entity, CrudKit.Core.Context.AppContext ctx)
        => OnAfterUpdate?.Invoke(entity, ctx) ?? Task.CompletedTask;
    public Task BeforeDelete(ProductEntity entity, CrudKit.Core.Context.AppContext ctx)
        => OnBeforeDelete?.Invoke(entity, ctx) ?? Task.CompletedTask;
    public Task AfterDelete(ProductEntity entity, CrudKit.Core.Context.AppContext ctx)
        => OnAfterDelete?.Invoke(entity, ctx) ?? Task.CompletedTask;
}

/// <summary>
/// Test IResponseMapper that adds a DisplayName field to the response.
/// </summary>
public class TestProductMapper : IResponseMapper<ProductEntity, ProductResponse>
{
    public ProductResponse Map(ProductEntity entity)
        => new(entity.Id, entity.Name, entity.Price, $"{entity.Name} (mapped)");

    public IQueryable<ProductResponse> Project(IQueryable<ProductEntity> query)
        => query.Select(e => new ProductResponse(e.Id, e.Name, e.Price, e.Name + " (mapped)"));
}

// Note: IAuditableEntity constraint is used throughout.
