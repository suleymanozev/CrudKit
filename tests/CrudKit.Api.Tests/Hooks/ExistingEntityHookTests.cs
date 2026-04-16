using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Interfaces;

using Xunit;
using AppContext = CrudKit.Core.Context.AppContext;

namespace CrudKit.Api.Tests.Hooks;

public class ExistingEntityHookTests
{
    /// <summary>
    /// Global hook implementation that captures the existing and current entity on BeforeUpdate.
    /// </summary>
    public class ExistingEntityTracker : IGlobalCrudHook
    {
        public object? CapturedExisting;
        public object? CapturedCurrent;

        public Task BeforeUpdate(object entity, object? existingEntity, AppContext ctx)
        {
            CapturedCurrent = entity;
            CapturedExisting = existingEntity;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task BeforeUpdate_ReceivesExistingEntity_WithOldValues()
    {
        var tracker = new ExistingEntityTracker();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc => svc.AddScoped<IGlobalCrudHook>(_ => tracker),
            configureEndpoints: web =>
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var createResp = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Original", Price = 10.0 });
        var created = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        await app.Client.PutAsJsonAsync($"/api/products/{id}", new { Name = "Updated", Price = 20.0 });

        Assert.NotNull(tracker.CapturedExisting);
        var existing = (ProductEntity)tracker.CapturedExisting;
        Assert.Equal("Original", existing.Name);
        Assert.Equal(10.0m, existing.Price);

        var current = (ProductEntity)tracker.CapturedCurrent!;
        Assert.Equal("Updated", current.Name);
        Assert.Equal(20.0m, current.Price);
    }

    /// <summary>
    /// Typed hook implementation that captures the existing entity on BeforeUpdate.
    /// </summary>
    public class TypedExistingEntityTracker : ICrudHooks<ProductEntity>
    {
        public ProductEntity? CapturedExisting;

        public Task BeforeUpdate(ProductEntity entity, ProductEntity? existingEntity, AppContext ctx)
        {
            CapturedExisting = existingEntity;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TypedHook_BeforeUpdate_ReceivesExistingEntity()
    {
        var tracker = new TypedExistingEntityTracker();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc =>
                svc.AddScoped<ICrudHooks<ProductEntity>>(_ => tracker),
            configureEndpoints: web =>
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var createResp = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Before", Price = 5.0 });
        var created = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        await app.Client.PutAsJsonAsync($"/api/products/{id}", new { Name = "After" });

        Assert.NotNull(tracker.CapturedExisting);
        Assert.Equal("Before", tracker.CapturedExisting!.Name);
    }
}
