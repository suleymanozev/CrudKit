using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AppContext = CrudKit.Core.Context.AppContext;

namespace CrudKit.Api.Tests.Hooks;

// ---------------------------------------------------------------------------
// Shared test hook implementations
// ---------------------------------------------------------------------------

/// <summary>
/// Simple hook that tracks call counts and received entity types.
/// Thread-local state via a shared tracker instance.
/// </summary>
public class TrackingGlobalHook : IGlobalCrudHook
{
    public int BeforeCreateCount;
    public int AfterCreateCount;
    public int BeforeUpdateCount;
    public int AfterUpdateCount;
    public int BeforeDeleteCount;
    public int AfterDeleteCount;
    public List<Type> ReceivedEntityTypes { get; } = new();

    public Task BeforeCreate(object entity, AppContext ctx)
    {
        BeforeCreateCount++;
        ReceivedEntityTypes.Add(entity.GetType());
        return Task.CompletedTask;
    }

    public Task AfterCreate(object entity, AppContext ctx)
    {
        AfterCreateCount++;
        return Task.CompletedTask;
    }

    public Task BeforeUpdate(object entity, AppContext ctx)
    {
        BeforeUpdateCount++;
        return Task.CompletedTask;
    }

    public Task AfterUpdate(object entity, AppContext ctx)
    {
        AfterUpdateCount++;
        return Task.CompletedTask;
    }

    public Task BeforeDelete(object entity, AppContext ctx)
    {
        BeforeDeleteCount++;
        return Task.CompletedTask;
    }

    public Task AfterDelete(object entity, AppContext ctx)
    {
        AfterDeleteCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Second hook used for multi-hook ordering tests.
/// </summary>
public class SecondTrackingGlobalHook : IGlobalCrudHook
{
    public int BeforeCreateCount;
    public int AfterCreateCount;

    public Task BeforeCreate(object entity, AppContext ctx)
    {
        BeforeCreateCount++;
        return Task.CompletedTask;
    }

    public Task AfterCreate(object entity, AppContext ctx)
    {
        AfterCreateCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Records call order across hooks so ordering assertions can be made.
/// </summary>
public static class CallOrderTracker
{
    public static readonly List<string> Log = new();

    public static void Reset() => Log.Clear();
}

public class OrderedFirstGlobalHook : IGlobalCrudHook
{
    public Task BeforeCreate(object entity, AppContext ctx)
    {
        CallOrderTracker.Log.Add("first:before");
        return Task.CompletedTask;
    }

    public Task AfterCreate(object entity, AppContext ctx)
    {
        CallOrderTracker.Log.Add("first:after");
        return Task.CompletedTask;
    }
}

public class OrderedSecondGlobalHook : IGlobalCrudHook
{
    public Task BeforeCreate(object entity, AppContext ctx)
    {
        CallOrderTracker.Log.Add("second:before");
        return Task.CompletedTask;
    }

    public Task AfterCreate(object entity, AppContext ctx)
    {
        CallOrderTracker.Log.Add("second:after");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Entity-specific hook that appends to the call order log.
/// </summary>
public class OrderedEntityHook : ICrudHooks<ProductEntity>
{
    public Task BeforeCreate(ProductEntity entity, AppContext ctx)
    {
        CallOrderTracker.Log.Add("entity:before");
        return Task.CompletedTask;
    }

    public Task AfterCreate(ProductEntity entity, AppContext ctx)
    {
        CallOrderTracker.Log.Add("entity:after");
        return Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public class GlobalHookTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // ------------------------------------------------------------------
    // Test 1: GlobalHook_CalledOnCreate
    // ------------------------------------------------------------------

    [Fact]
    public async Task GlobalHook_CalledOnCreate()
    {
        var tracker = new TrackingGlobalHook();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc => svc.AddScoped<IGlobalCrudHook>(_ => tracker),
            configureEndpoints: web =>
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var response = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Gadget", Price = 9.99 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, tracker.BeforeCreateCount);
        Assert.Equal(1, tracker.AfterCreateCount);
        Assert.Equal(0, tracker.BeforeUpdateCount);
        Assert.Equal(0, tracker.BeforeDeleteCount);
    }

    // ------------------------------------------------------------------
    // Test 2: GlobalHook_CalledOnUpdate
    // ------------------------------------------------------------------

    [Fact]
    public async Task GlobalHook_CalledOnUpdate()
    {
        var tracker = new TrackingGlobalHook();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc => svc.AddScoped<IGlobalCrudHook>(_ => tracker),
            configureEndpoints: web =>
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var createResp = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Widget", Price = 5.0 });
        var created = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        var updateResp = await app.Client.PutAsJsonAsync($"/api/products/{id}", new { Name = "Widget v2" });

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        Assert.Equal(1, tracker.BeforeUpdateCount);
        Assert.Equal(1, tracker.AfterUpdateCount);
        Assert.Equal(0, tracker.BeforeDeleteCount);
    }

    // ------------------------------------------------------------------
    // Test 3: GlobalHook_CalledOnDelete
    // ------------------------------------------------------------------

    [Fact]
    public async Task GlobalHook_CalledOnDelete()
    {
        var tracker = new TrackingGlobalHook();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc => svc.AddScoped<IGlobalCrudHook>(_ => tracker),
            configureEndpoints: web =>
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var createResp = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Doomed", Price = 1.0 });
        var created = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        var deleteResp = await app.Client.DeleteAsync($"/api/products/{id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
        Assert.Equal(1, tracker.BeforeDeleteCount);
        Assert.Equal(1, tracker.AfterDeleteCount);
    }

    // ------------------------------------------------------------------
    // Test 4: GlobalHook_CalledForAllEntityTypes
    // ------------------------------------------------------------------

    [Fact]
    public async Task GlobalHook_CalledForAllEntityTypes()
    {
        var tracker = new TrackingGlobalHook();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc => svc.AddScoped<IGlobalCrudHook>(_ => tracker),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
                web.MapCrudEndpoints<OrderEntity, CreateOrderDto, UpdateOrderDto>("orders");
            });

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "P1", Price = 1.0 });
        await app.Client.PostAsJsonAsync("/api/orders", new { Customer = "Alice" });

        Assert.Equal(2, tracker.BeforeCreateCount);
        Assert.Equal(2, tracker.AfterCreateCount);

        // Verify both entity types were received
        Assert.Contains(typeof(ProductEntity), tracker.ReceivedEntityTypes);
        Assert.Contains(typeof(OrderEntity), tracker.ReceivedEntityTypes);
    }

    // ------------------------------------------------------------------
    // Test 5: GlobalHook_RunsAlongsideEntityHook
    // ------------------------------------------------------------------

    [Fact]
    public async Task GlobalHook_RunsAlongsideEntityHook()
    {
        CallOrderTracker.Reset();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc =>
            {
                // Register global hook
                svc.AddScoped<IGlobalCrudHook, OrderedFirstGlobalHook>();
                // Register entity-specific hook
                svc.AddScoped<ICrudHooks<ProductEntity>, OrderedEntityHook>();
            },
            configureEndpoints: web =>
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var response = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Order test", Price = 1.0 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Expected order: global before → entity before → entity after → global after
        Assert.Equal(4, CallOrderTracker.Log.Count);
        Assert.Equal("first:before", CallOrderTracker.Log[0]);
        Assert.Equal("entity:before", CallOrderTracker.Log[1]);
        Assert.Equal("entity:after", CallOrderTracker.Log[2]);
        Assert.Equal("first:after", CallOrderTracker.Log[3]);
    }

    // ------------------------------------------------------------------
    // Test 6: MultipleGlobalHooks_AllCalled
    // ------------------------------------------------------------------

    [Fact]
    public async Task MultipleGlobalHooks_AllCalled()
    {
        CallOrderTracker.Reset();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc =>
            {
                svc.AddScoped<IGlobalCrudHook, OrderedFirstGlobalHook>();
                svc.AddScoped<IGlobalCrudHook, OrderedSecondGlobalHook>();
            },
            configureEndpoints: web =>
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var response = await app.Client.PostAsJsonAsync("/api/products", new { Name = "Multi hook", Price = 2.0 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Both hooks must be called in registration order (before and after)
        Assert.Equal(4, CallOrderTracker.Log.Count);
        Assert.Equal("first:before", CallOrderTracker.Log[0]);
        Assert.Equal("second:before", CallOrderTracker.Log[1]);
        Assert.Equal("first:after", CallOrderTracker.Log[2]);
        Assert.Equal("second:after", CallOrderTracker.Log[3]);
    }
}
