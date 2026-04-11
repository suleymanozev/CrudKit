<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/banner-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="assets/banner-light.png">
  <img alt="CrudKit" src="assets/banner-light.png" width="300">
</picture>

A convention-based CRUD framework for .NET 10. Define entities, get endpoints.

---

## Features

- Auto-mapped REST endpoints (List, Get, Create, Update, Delete)
- Soft delete with cascade and restore
- Multi-tenant isolation (`ITenantContext` + 5 built-in resolvers + 3-layer cross-tenant protection + `IDataFilter<T>` for runtime filter control)
- Audit trail (`[Audited]` + `UseAuditTrail()` opt-in, `[AuditIgnore]` per-property, `[Hashed]` auto-masked)
- Per-operation authorization (`Authorize()` builder — role-based, permission-based, convention)
- Custom endpoints via `.WithCustomEndpoints()` under same route group
- Optimistic concurrency
- Lifecycle hooks (`ICrudHooks<T>`) + global hooks (`IGlobalCrudHook`) for cross-cutting concerns
- Validation (FluentValidation priority, DataAnnotation fallback)
- State machine transitions (`IStateMachine<TState>`)
- Master-child relationships (fluent `.WithChild()` or declarative `[ChildOf]`)
- Idempotency via request header
- Bulk operations (`/bulk-count`, `/bulk-delete`, `/bulk-update`)
- ReadOnly entities (List + Get only)
- CSV import/export (`[Exportable]`, `[Importable]`, per-property control)
- Source generation — DTOs, mappers, endpoint mapping, hook stubs (naming templates via `[assembly: CrudKit(...)]`)
- `[CreateDtoFor]` / `[UpdateDtoFor]` — manual DTOs that suppress SourceGen for those types
- `Optional<T>` for partial updates (distinguishes null from missing)
- Property attributes: `[Hashed]`, `[SkipResponse]`, `[SkipUpdate]`, `[Protected]`, `[Unique]`, `[Searchable]`
- Filter/sort control per entity and property (`[NotFilterable]`, `[NotSortable]`)
- Modular monolith support (`IModule` with assembly scan, multi-DbContext auto-resolution)
- Multi-database dialect (SQLite, PostgreSQL, SQL Server) — auto-detected
- Entity base class hierarchy (`Entity`, `AuditableEntity`, `FullAuditableEntity` — with `<TUser>` variants)
- Enum properties stored as strings automatically
- Structured error responses (409 concurrency, dev/prod stack trace toggle)

---

## Quick Start

**1. Register services**

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite("Data Source=app.db"));

builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.DefaultPageSize = 25;
    opts.MaxPageSize = 100;
    opts.UseAuditTrail();
    opts.UseMultiTenancy().ResolveTenantFromHeader("X-Tenant-Id");
});
```

**2. Map endpoints**

```csharp
var app = builder.Build();
app.UseCrudKit();

// Route auto-derived from entity name: "products"
app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>();

// Or with SourceGen — maps all [CrudEntity] types in one call
app.MapAllCrudEndpoints();

app.Run();
```

**3. Define an entity**

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

    [Hashed, SkipResponse]
    public string InternalToken { get; set; } = string.Empty;
}
```

**Generated endpoints**

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/products` | List (paginated, filtered, sorted) |
| GET | `/api/products/{id}` | Get by ID |
| POST | `/api/products` | Create |
| PUT | `/api/products/{id}` | Update (partial via `Optional<T>`) |
| DELETE | `/api/products/{id}` | Soft-delete (`ISoftDeletable`) |
| DELETE | `/api/products/{id}/purge` | Permanently delete a single soft-deleted record |
| DELETE | `/api/products/purge?olderThan=30` | Bulk purge soft-deleted records older than N days |
| POST | `/api/products/{id}/restore` | Restore soft-deleted record |
| POST | `/api/products/{id}/transition/{action}` | State transition (`IStateMachine<TState>` only) |

**Mappers are optional.** Without them, CrudKit uses reflection for DTO ↔ entity mapping and serializes entities directly for responses. Register `IResponseMapper<T, TResponse>` for custom response shapes, or use SourceGen to eliminate reflection entirely.

---

## Entity Hierarchy

| Class | Provides |
|-------|----------|
| `Entity` | `Guid Id` |
| `Entity<TKey>` | Custom key type (e.g. `long`, `int`) |
| `AuditableEntity` | `Id` + `CreatedAt`, `UpdatedAt` |
| `AuditableEntityWithUser<TUser>` | + `CreatedById`, `UpdatedById` (auto-set from `ICurrentUser`) + navigations |
| `FullAuditableEntity` | `AuditableEntity` + `DeletedAt` (implements `ISoftDeletable`) |
| `FullAuditableEntityWithUser<TUser>` | + `CreatedById`, `UpdatedById`, `DeletedById` (auto-set) + navigations |

```csharp
// Lookup table — Guid Id only
public class Currency : Entity { }

// Timestamps + soft delete
[CrudEntity]
[RequireAuth]
[AuthorizeOperation("Delete", "admin")]
public class Order : FullAuditableEntity
{
    public string CustomerName { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

// Timestamps + user tracking + soft delete
[CrudEntity]
[RequireRole("admin")]
public class Invoice : FullAuditableEntityWithUser<AppUser>
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

// Custom key type
public class LegacyProduct : AuditableEntity<long> { }
public class LegacyOrder : FullAuditableEntityWithUser<long, AppUser, int> { }
```

---

## Configuration

```csharp
builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    // Pagination
    opts.DefaultPageSize = 25;           // Default: 20
    opts.MaxPageSize = 100;              // Default: 100

    // Routing
    opts.ApiPrefix = "/api";             // Default: "/api"

    // Bulk operations
    opts.BulkLimit = 10_000;             // Default: 10,000

    // Idempotency
    opts.EnableIdempotency = true;       // Default: false

    // Audit trail — opt entities in with [Audited]
    opts.UseAuditTrail();
    // or with custom writer:
    opts.UseAuditTrail<ElasticAuditWriter>()
        .EnableAuditFailedOperations();

    // Import / Export
    opts.UseExport();                    // All entities exportable (opt-out with [NotExportable])
    opts.MaxExportRows = 50_000;         // Default: 50,000
    opts.UseImport();                    // All entities importable (opt-out with [NotImportable])
    opts.MaxImportFileSize = 10 * 1024 * 1024; // Default: 10 MB

    // Enum storage
    opts.UseEnumAsString();              // Store enums as strings in DB

    // Multi-tenancy
    opts.UseMultiTenancy()
        .ResolveTenantFromHeader("X-Tenant-Id")
        .RejectUnresolvedTenant()
        .CrossTenantPolicy(p => p.Allow("superadmin"));

    // Global hooks
    opts.UseGlobalHook<SearchIndexHook>();

    // Module discovery
    opts.ScanModulesFromAssembly = typeof(Program).Assembly;
});
```

| Method | Returns | Description |
|--------|---------|-------------|
| `UseAuditTrail()` | `AuditTrailOptions` | Enable audit trail, opt entities in with `[Audited]` |
| `UseAuditTrail<T>()` | `AuditTrailOptions` | Same, with custom `IAuditWriter` implementation |
| `UseExport()` | `CrudKitApiOptions` | Enable CSV export globally |
| `UseImport()` | `CrudKitApiOptions` | Enable CSV import globally |
| `UseEnumAsString()` | `CrudKitApiOptions` | Store all enum properties as strings |
| `UseMultiTenancy()` | `MultiTenancyOptions` | Enable multi-tenancy, chain resolver method |
| `UseGlobalHook<T>()` | `CrudKitApiOptions` | Register a global `IGlobalCrudHook` |

## Multi-Tenant Program.cs Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite("Data Source=app.db"));

builder.Services.AddScoped<ICurrentUser, JwtCurrentUser>();

builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.UseMultiTenancy()
        .ResolveTenantFromClaim("tenant_id")
        .RejectUnresolvedTenant()
        .CrossTenantPolicy(p => p.Allow("superadmin"));
});

var app = builder.Build();
app.UseCrudKit();
app.MapAllCrudEndpoints();
app.Run();
```

Entities implementing `IMultiTenant` are automatically scoped to the resolved tenant. Inject `IDataFilter<ISoftDeletable>` to temporarily disable the soft-delete filter within a request:

```csharp
public class OrderService
{
    private readonly IDataFilter<ISoftDeletable> _softDeleteFilter;
    private readonly IRepo<Order> _repo;

    public async Task<List<Order>> GetDeletedOrdersAsync()
    {
        using (_softDeleteFilter.Disable())
        {
            return (await _repo.List(new ListParams(), default)).Data;
        }
    }
}
```

---

## Project Structure

```
src/
├── CrudKit.Core/                # Attributes, interfaces, models
├── CrudKit.EntityFrameworkCore/ # EF Core integration, repository, query
├── CrudKit.Api/                 # Minimal API layer, endpoint mapping, filters
├── CrudKit.Identity/            # ASP.NET Identity integration (CrudKitIdentityDbContext)
└── CrudKit.SourceGen/           # Roslyn source generator
tests/
├── CrudKit.Core.Tests/
├── CrudKit.EntityFrameworkCore.Tests/
├── CrudKit.Api.Tests/
└── CrudKit.SourceGen.Tests/
samples/
└── CrudKit.Sample.Api/          # Working sample with Product, Category, Order, Unit
docs/
├── API-REFERENCE.md             # Full feature reference
└── specs/                       # Internal design specifications
```

---

## Master-Child Relationships

Declare child entities with `[ChildOf]` and CrudKit generates nested endpoints automatically under the parent route.

```csharp
[ChildOf(typeof(Order))]
public class OrderLine : AuditableEntity
{
    public Guid OrderId { get; set; }           // FK convention: {ParentType}Id
    public string ProductName { get; set; } = string.Empty;
}

// Auto-generated: GET/DELETE /api/orders/{id}/order-lines, GET/POST /api/orders/{id}/order-lines/{id}
// Custom route + FK:
[ChildOf(typeof(Order), Route = "items", ForeignKey = "ParentOrderId")]
```

For explicit control use the fluent API: `app.MapCrudEndpoints<Order, ...>().WithChild<OrderLine, CreateOrderLine>("items", "OrderId");`

---

## Manual DTOs with [CreateDtoFor] / [UpdateDtoFor]

When a DTO is annotated with `[CreateDtoFor(typeof(T))]` or `[UpdateDtoFor(typeof(T))]`, SourceGen skips generating the corresponding DTO for that entity. The `ResponseDto` and mapper are still generated.

```csharp
[CreateDtoFor(typeof(Order))]
public record CreateOrder([Required] string CustomerName, decimal Total = 0);

[UpdateDtoFor(typeof(Order))]
public record UpdateOrder
{
    public Optional<string?> CustomerName { get; init; }
}
```

## Naming Templates

Customize SourceGen naming conventions at assembly level:

```csharp
[assembly: CrudKit(
    CreateDtoNamingTemplate = "{Name}CreateRequest",   // default: "Create{Name}"
    UpdateDtoNamingTemplate = "{Name}UpdateRequest",   // default: "Update{Name}"
    ResponseDtoNamingTemplate = "{Name}Dto")]          // default: "{Name}Response"
```

`{Name}` placeholder is required — empty or missing placeholder causes a compile error.

---

## Purge Endpoints

For `ISoftDeletable` entities, CrudKit exposes two purge endpoints for permanent (hard) deletion:

```
// Single item — must be soft-deleted first
DELETE /api/products/{id}/purge
// Response: 204 No Content

// Bulk — deletes all soft-deleted records older than N days
DELETE /api/products/purge?olderThan=30
// Response: { "purged": 15 }
```

Single purge requires the record to be soft-deleted already (returns 400 if active). Bulk purge requires `olderThan` (minimum 1 day). Both use `ExecuteDeleteAsync` — bypasses soft-delete interception. Respects tenant isolation for `IMultiTenant` entities.

---

## Documentation

Full API reference: [docs/API-REFERENCE.md](docs/API-REFERENCE.md)

---

## License

MIT
