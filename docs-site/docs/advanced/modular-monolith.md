---
sidebar_position: 1
title: Modular Monolith
---

# Modular Monolith

CrudKit supports organizing large applications as modular monoliths using `IModule`. Each bounded context self-registers its services and endpoints.

## Defining a Module

```csharp
public class OrderModule : IModule
{
    public string Name => "Orders";

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<ICrudHooks<Order>, OrderHooks>();
    }

    public void MapEndpoints(WebApplication app)
    {
        app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>()
           .WithChild<OrderLine, CreateOrderLine>("lines", "OrderId");
    }
}
```

## IModule Interface

```csharp
public interface IModule
{
    string Name { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
    void MapEndpoints(WebApplication app);
}
```

## Assembly Scan

```csharp
// Automatically discovers all IModule implementations
opts.ScanModulesFromAssembly = typeof(Program).Assembly;
```

Or register manually:

```csharp
builder.Services.AddCrudKitModule<OrderModule>();
```

`UseCrudKit()` calls `MapEndpoints` on all discovered modules.

## Multiple DbContexts

Each module can own its DbContext. CrudKit automatically resolves the correct context per entity by scanning `DbSet<>` properties.

Use `CrudKitDbContextDependencies` to simplify constructors — especially useful when you have many DbContexts:

```csharp
public class OrderDbContext : CrudKitDbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public OrderDbContext(DbContextOptions<OrderDbContext> options, CrudKitDbContextDependencies deps)
        : base(options, deps) { }
}

public class InventoryDbContext : CrudKitDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    public InventoryDbContext(DbContextOptions<InventoryDbContext> options, CrudKitDbContextDependencies deps)
        : base(options, deps) { }
}

// Register both
builder.Services.AddDbContext<OrderDbContext>(opts => opts.UseNpgsql("..."));
builder.Services.AddDbContext<InventoryDbContext>(opts => opts.UseNpgsql("..."));
builder.Services.AddCrudKit<OrderDbContext>();
builder.Services.AddCrudKit<InventoryDbContext>();
```

`EfRepo<Order>` resolves `OrderDbContext`; `EfRepo<Product>` resolves `InventoryDbContext`. No extra configuration needed — the resolution is automatic via `CrudKitContextRegistry`.

## IModule with Own DbContext (Recommended)

The cleanest pattern — each module registers its own DbContext inside `RegisterServices`:

```csharp
public class OrderModule : IModule
{
    public string Name => "Orders";

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<OrderDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("Orders")));
        services.AddCrudKitEf<OrderDbContext>();
        services.AddScoped<ICrudHooks<Order>, OrderHooks>();
    }

    public void MapEndpoints(WebApplication app)
    {
        app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>();
    }
}

public class InventoryModule : IModule
{
    public string Name => "Inventory";

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<InventoryDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("Inventory")));
        services.AddCrudKitEf<InventoryDbContext>();
    }

    public void MapEndpoints(WebApplication app)
    {
        app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>();
    }
}
```

Program.cs stays minimal:

```csharp
builder.Services.AddCrudKit<SharedDbContext>(opts =>
{
    opts.ScanModulesFromAssembly = typeof(Program).Assembly;
});

var app = builder.Build();
app.UseCrudKit();  // discovers modules, calls RegisterServices + MapEndpoints
app.Run();
```

Each module is self-contained: own DbContext, own connection string, own hooks. Modules can even use different database providers (PostgreSQL for orders, SQL Server for inventory).

## Schema Isolation

Use `UseModuleSchema` instead of `HasDefaultSchema` for cross-provider compatibility:

```csharp
protected override void OnModelCreatingCustom(ModelBuilder modelBuilder)
{
    UseModuleSchema(modelBuilder, "finance");
}
```

| Provider | Behavior |
|----------|----------|
| PostgreSQL | Creates schema: `finance.Invoices` |
| SQL Server | Creates schema: `finance.Invoices` |
| MySQL | Skipped — use separate databases for isolation |
| SQLite | Skipped — no schema support |

## SQLite Schema Validation

When using SQLite, CrudKit performs schema validation at startup. If the database schema does not match the entity model (e.g. missing columns or tables), the application throws immediately rather than failing on the first query. This ensures configuration errors are caught early in development.
