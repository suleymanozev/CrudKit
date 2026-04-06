using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Extensions;
using CrudKit.EntityFrameworkCore.Repository;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests;

// ---------------------------------------------------------------------------
// Test entities and contexts for multi-DbContext scenario
// ---------------------------------------------------------------------------

public class OrderEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ProductEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class OrderDbContext : CrudKitDbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
}

public class ProductDbContext : CrudKitDbContext
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<ProductEntity> Products => Set<ProductEntity>();
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public class MultiDbContextTests
{
    private static ServiceProvider BuildMultiContextProvider(
        out Microsoft.Data.Sqlite.SqliteConnection orderConn,
        out Microsoft.Data.Sqlite.SqliteConnection productConn)
    {
        // SQLite in-memory databases require the connection to stay open
        orderConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        orderConn.Open();
        productConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        productConn.Open();

        var oc = orderConn;
        var pc = productConn;

        var services = new ServiceCollection();
        services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser());

        services.AddDbContext<OrderDbContext>((_, opts) => opts.UseSqlite(oc));
        services.AddDbContext<ProductDbContext>((_, opts) => opts.UseSqlite(pc));

        // Register both with CrudKit — the key scenario
        services.AddCrudKitEf<OrderDbContext>();
        services.AddCrudKitEf<ProductDbContext>();

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildMultiContextProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser());
        services.AddDbContext<OrderDbContext>((_, opts) =>
            opts.UseSqlite($"Data Source=orders_{Guid.NewGuid()};Mode=Memory;Cache=Shared"));
        services.AddDbContext<ProductDbContext>((_, opts) =>
            opts.UseSqlite($"Data Source=products_{Guid.NewGuid()};Mode=Memory;Cache=Shared"));
        services.AddCrudKitEf<OrderDbContext>();
        services.AddCrudKitEf<ProductDbContext>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registry_ContainsBothContextTypes()
    {
        using var sp = BuildMultiContextProvider();
        var registry = sp.GetRequiredService<CrudKitContextRegistry>();

        Assert.Contains(typeof(OrderDbContext), registry.ContextTypes);
        Assert.Contains(typeof(ProductDbContext), registry.ContextTypes);
    }

    [Fact]
    public void Registry_FindsCorrectContextForOrder()
    {
        using var sp = BuildMultiContextProvider();
        var registry = sp.GetRequiredService<CrudKitContextRegistry>();

        var contextType = registry.FindContextForEntity(typeof(OrderEntity));
        Assert.Equal(typeof(OrderDbContext), contextType);
    }

    [Fact]
    public void Registry_FindsCorrectContextForProduct()
    {
        using var sp = BuildMultiContextProvider();
        var registry = sp.GetRequiredService<CrudKitContextRegistry>();

        var contextType = registry.FindContextForEntity(typeof(ProductEntity));
        Assert.Equal(typeof(ProductDbContext), contextType);
    }

    [Fact]
    public void Registry_ReturnsNullForUnknownEntity()
    {
        using var sp = BuildMultiContextProvider();
        var registry = sp.GetRequiredService<CrudKitContextRegistry>();

        // PersonEntity is not in either context
        var contextType = registry.FindContextForEntity(typeof(PersonEntity));
        Assert.Null(contextType);
    }

    [Fact]
    public void ResolveFor_ReturnsOrderDbContext_ForOrderEntity()
    {
        using var sp = BuildMultiContextProvider();
        using var scope = sp.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<CrudKitContextRegistry>();

        var db = registry.ResolveFor<OrderEntity>(scope.ServiceProvider);
        Assert.IsType<OrderDbContext>(db);
    }

    [Fact]
    public void ResolveFor_ReturnsProductDbContext_ForProductEntity()
    {
        using var sp = BuildMultiContextProvider();
        using var scope = sp.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<CrudKitContextRegistry>();

        var db = registry.ResolveFor<ProductEntity>(scope.ServiceProvider);
        Assert.IsType<ProductDbContext>(db);
    }

    [Fact]
    public void EfRepo_ResolvesCorrectContext_ForEachEntity()
    {
        using var sp = BuildMultiContextProvider();
        using var scope = sp.CreateScope();

        // IRepo<OrderEntity> should resolve via OrderDbContext
        var orderRepo = scope.ServiceProvider.GetRequiredService<IRepo<OrderEntity>>();
        Assert.IsType<EfRepo<OrderEntity>>(orderRepo);

        // IRepo<ProductEntity> should resolve via ProductDbContext
        var productRepo = scope.ServiceProvider.GetRequiredService<IRepo<ProductEntity>>();
        Assert.IsType<EfRepo<ProductEntity>>(productRepo);
    }

    [Fact]
    public async Task EfRepo_CanCreateAndRead_InSeparateContexts()
    {
        using var sp = BuildMultiContextProvider(out var orderConn, out var productConn);
        try
        {
            using var scope = sp.CreateScope();

            // Ensure databases are created
            var orderDb = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            await orderDb.Database.EnsureCreatedAsync();
            var productDb = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            await productDb.Database.EnsureCreatedAsync();

            var orderRepo = scope.ServiceProvider.GetRequiredService<IRepo<OrderEntity>>();
            var productRepo = scope.ServiceProvider.GetRequiredService<IRepo<ProductEntity>>();

            // Create an order
            var order = await orderRepo.Create(new { CustomerName = "Alice" });
            Assert.Equal("Alice", order.CustomerName);

            // Create a product
            var product = await productRepo.Create(new { ProductName = "Widget" });
            Assert.Equal("Widget", product.ProductName);

            // Verify they are in separate databases
            Assert.True(await orderDb.Orders.AnyAsync(o => o.Id == order.Id));
            Assert.True(await productDb.Products.AnyAsync(p => p.Id == product.Id));
        }
        finally
        {
            orderConn.Dispose();
            productConn.Dispose();
        }
    }
}
