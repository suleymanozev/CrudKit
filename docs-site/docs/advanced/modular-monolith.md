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
           .WithDetail<OrderLine, CreateOrderLine>("lines", "OrderId");
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

```csharp
public class OrderDbContext : CrudKitDbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
}

public class InventoryDbContext : CrudKitDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
}

// Register both
builder.Services.AddDbContext<OrderDbContext>(opts => opts.UseNpgsql("..."));
builder.Services.AddDbContext<InventoryDbContext>(opts => opts.UseNpgsql("..."));
builder.Services.AddCrudKit<OrderDbContext>();
builder.Services.AddCrudKit<InventoryDbContext>();
```

`EfRepo<Order>` resolves `OrderDbContext`; `EfRepo<Product>` resolves `InventoryDbContext`. No extra configuration needed — the resolution is automatic via `CrudKitContextRegistry`.
