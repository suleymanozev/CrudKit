---
sidebar_position: 1
title: Quick Start
---

# Quick Start

This page walks you through setting up CrudKit in a new .NET 10 Minimal API project.

## Installation

```bash
dotnet add package CrudKit.Api
dotnet add package CrudKit.EntityFrameworkCore
```

## Step 1 — Define your DbContext

Your DbContext must inherit from `CrudKitDbContext`:

```csharp
public class AppDbContext : CrudKitDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentUser currentUser,
        TimeProvider? timeProvider = null)
        : base(options, currentUser, timeProvider) { }
}
```

## Step 2 — Register Services

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite("Data Source=app.db"));

builder.Services.AddCrudKitEf<AppDbContext>();
builder.Services.AddCrudKit(opts =>
{
    opts.DefaultPageSize = 25;
    opts.MaxPageSize = 100;
    opts.UseAuditTrail();
    opts.UseMultiTenancy().ResolveTenantFromHeader("X-Tenant-Id");
});
```

## Step 3 — Map Endpoints

```csharp
var app = builder.Build();
app.UseCrudKit(); // auto-registers all [CrudEntity] types

// Or register manually for full control:
// app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>();

app.Run();
```

## Step 4 — Define an Entity

```csharp
[CrudEntity]
[Audited]
[RequirePermissions]  // auto-convention: products:read, products:create, ...
public class Product : FullAuditableEntity
{
    [Required, MaxLength(200), Searchable]
    public string Name { get; set; } = string.Empty;

    [Range(0.01, 999_999.99)]
    public decimal Price { get; set; }

    [Unique, SkipUpdate]
    public string Sku { get; set; } = string.Empty;
}
```

## Step 5 — Create DTOs

Define DTOs manually, or skip this step to use entity-as-DTO mode:

```csharp
public record CreateProduct(
    string Name,
    decimal Price,
    string Sku
);

public record UpdateProduct(
    Optional<string> Name,
    Optional<decimal> Price
);
```

`Optional<T>` distinguishes between a field being absent (not sent) and explicitly set to `null`.

## Step 6 — Apply Migrations

```bash
dotnet ef migrations add InitialCreate -c AppDbContext
dotnet ef database update -c AppDbContext
```

## Generated Endpoints

For a `Product` entity (route: `/api/products`):

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/products` | List (paginated, filtered, sorted) |
| GET | `/api/products/{id}` | Get by ID |
| POST | `/api/products` | Create |
| PUT | `/api/products/{id}` | Update (partial via `Optional<T>`) |
| DELETE | `/api/products/{id}` | Soft-delete |
| DELETE | `/api/products/{id}/purge` | Permanently delete a soft-deleted record |
| DELETE | `/api/products/purge?olderThan=N` | Bulk purge records deleted more than N days ago |
| POST | `/api/products/{id}/restore` | Restore soft-deleted record |

## Next Steps

- [Entity Hierarchy](entity-hierarchy) — pick the right base class
- [Configuration](configuration) — all global options
- [Soft Delete](../features/soft-delete) — how delete and restore work
- [Authorization](../features/auth) — securing endpoints
