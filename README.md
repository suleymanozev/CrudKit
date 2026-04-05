# CrudKit

A convention-based CRUD framework for .NET 10. Define entities, get endpoints.

---

## Features

- Auto-mapped REST endpoints (List, Get, Create, Update, Delete)
- Soft delete with cascade and restore
- Multi-tenant isolation
- Audit logging
- Optimistic concurrency
- Lifecycle hooks (`ICrudHooks<T>`)
- Validation (FluentValidation priority, DataAnnotation fallback)
- State machine transitions (`IStateMachine<TState>`)
- Master-detail relationships (fluent `.WithDetail()`)
- Idempotency via request header
- Bulk operations (`/bulk-count`, `/bulk-delete`, `/bulk-update`)
- ReadOnly entities (List + Get only)
- Source generation — DTOs, mappers, endpoint mapping, hook stubs
- `Optional<T>` for partial updates (distinguishes null from missing)
- Property attributes: `[Hashed]`, `[SkipResponse]`, `[SkipUpdate]`, `[Protected]`, `[Unique]`, `[Searchable]`
- Document numbering (e.g. `ORD-2026-00001`) with tenant-scoped sequences
- Modular monolith support (`IModule` with assembly scan)
- Multi-database dialect (SQLite, PostgreSQL, SQL Server) — auto-detected
- Enum properties stored as strings automatically
- Domain events (`IEventBus`) — publish from hooks
- Structured error responses (409 concurrency, dev/prod stack trace toggle)

---

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opts => opts.UseSqlite("Data Source=app.db"));
builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.DefaultPageSize = 25;
    opts.MaxPageSize = 100;
});

var app = builder.Build();
app.UseCrudKit();

app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>("products");

app.Run();
```

`AddCrudKit<TContext>` registers the EF Core repository, validation, JSON options, and startup validation in a single call. `UseCrudKit()` activates any registered `IModule` instances.

---

## Entity Definition

```csharp
[CrudEntity(Table = "products")]
public class Product : IEntity
{
    public string Id { get; set; } = string.Empty;

    [Required, MaxLength(200), Searchable]
    public string Name { get; set; } = string.Empty;

    [Range(0.01, 999_999.99)]
    public decimal Price { get; set; }

    public string? Description { get; set; }

    [Unique]
    public string Sku { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

All entities must implement `IEntity` (`Id`, `CreatedAt`, `UpdatedAt`). `CrudKitDbContext` sets `CreatedAt`/`UpdatedAt` automatically on save.

### `[CrudEntity]` options

| Property | Type | Description |
|---|---|---|
| `Table` | `string` | Database table name |
| `SoftDelete` | `bool` | Set `DeletedAt` instead of deleting rows |
| `Audit` | `bool` | Write all changes to the AuditLog table |
| `MultiTenant` | `bool` | Filter queries by tenant from `ICurrentUser` |
| `ReadOnly` | `bool` | Generate List + Get only, no write endpoints |
| `EnableCreate/Update/Delete` | `bool` | Fine-grained endpoint control (default: true) |
| `EnableBulkDelete/Update` | `bool` | Enable bulk operation endpoints |
| `BulkLimit` | `int` | Override global bulk limit for this entity |
| `OwnerField` | `string` | Property holding the owner user ID for row-level security |
| `NumberingPrefix` | `string` | Auto-generate sequence numbers (e.g. `ORD-2026-0001`) |
| `NumberingYearlyReset` | `bool` | Reset counter each year (default: true) |

---

## DTOs

```csharp
// Create DTO — standard record with validation attributes
public record CreateProduct(
    [Required, MaxLength(200)] string Name,
    [Range(0.01, 999_999.99)] decimal Price,
    string? Description = null,
    string Sku = "");

// Update DTO — Optional<T> on every field
public record UpdateProduct
{
    public Optional<string?> Name { get; init; }
    public Optional<decimal?> Price { get; init; }
    public Optional<string?> Description { get; init; }
}
```

`Optional<T>` distinguishes between a field that was absent from the JSON body (`HasValue = false`, skip it) and a field that was explicitly sent — including `null` (`HasValue = true`, apply it). Fields not present in the request body are left unchanged on the entity.

---

## Endpoints Generated

For `app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>("products")`:

| Method | Route | Description |
|---|---|---|
| GET | `/api/products` | List (paginated, filtered, sorted) |
| GET | `/api/products/{id}` | Get by ID |
| POST | `/api/products` | Create |
| PUT | `/api/products/{id}` | Update (partial via `Optional<T>`) |
| DELETE | `/api/products/{id}` | Delete (or soft-delete if `ISoftDeletable`) |
| POST | `/api/products/{id}/restore` | Restore soft-deleted record (`ISoftDeletable` only) |
| POST | `/api/products/{id}/transition/{action}` | State transition (`IStateMachine<TState>` only) |

---

## Features

### Soft Delete

Implement `ISoftDeletable` and set `SoftDelete = true` on `[CrudEntity]`. DELETE sets `DeletedAt` instead of removing the row. Soft-deleted records are excluded from all queries automatically.

```csharp
[CrudEntity(Table = "categories", SoftDelete = true)]
public class Category : IEntity, ISoftDeletable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

The restore endpoint is mapped automatically: `POST /api/categories/{id}/restore`.

To cascade soft-delete to child entities, decorate the navigation property with `[CascadeSoftDelete]`.

---

### Lifecycle Hooks

Register a service implementing `ICrudHooks<T>` to intercept create, update, delete, and restore operations.

```csharp
public class ProductHooks : ICrudHooks<Product>
{
    public Task BeforeCreate(Product entity, AppContext ctx)
    {
        entity.Sku = entity.Sku.ToUpperInvariant();
        return Task.CompletedTask;
    }

    public Task AfterCreate(Product entity, AppContext ctx)
    {
        // send event, invalidate cache, etc.
        return Task.CompletedTask;
    }
}

// Register in DI
builder.Services.AddScoped<ICrudHooks<Product>, ProductHooks>();
```

Execution order: `Validate` → `Before*` → DB operation → `After*`

Override only the hooks you need — all methods have empty default implementations. Use `ApplyScope` for row-level security and `ApplyIncludes` for custom `ThenInclude` scenarios.

---

### ReadOnly Entities

Use the single-type overload for entities that should never be mutated via API:

```csharp
// Only GET /api/units and GET /api/units/{id} are mapped
app.MapCrudEndpoints<Unit>("units");
```

Or set `ReadOnly = true` on `[CrudEntity]` to enforce this at the attribute level.

---

### Master-Detail

Chain `.WithDetail<TDetail, TCreateDetail>()` to map nested endpoints scoped under a master resource:

```csharp
app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>("orders")
    .WithDetail<OrderLine, CreateOrderLine>("lines", "OrderId");
```

This maps the following additional endpoints:

| Method | Route |
|---|---|
| GET | `/api/orders/{masterId}/lines` |
| GET | `/api/orders/{masterId}/lines/{id}` |
| POST | `/api/orders/{masterId}/lines` |
| DELETE | `/api/orders/{masterId}/lines/{id}` |
| PUT | `/api/orders/{masterId}/lines/batch` |

The `batch` endpoint replaces all detail records for a master in a single transaction.

---

### State Machine

Implement `IStateMachine<TState>` to add transition endpoints. Define valid state changes as a static list of `(From, To, Action)` tuples.

```csharp
public enum OrderStatus { Pending, Processing, Completed, Cancelled }

[CrudEntity(Table = "orders", SoftDelete = true)]
public class Order : IEntity, ISoftDeletable, IStateMachine<OrderStatus>
{
    public string Id { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal Total { get; set; }

    [Protected]
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public static IReadOnlyList<(OrderStatus From, OrderStatus To, string Action)> Transitions =>
    [
        (OrderStatus.Pending,    OrderStatus.Processing, "process"),
        (OrderStatus.Processing, OrderStatus.Completed,  "complete"),
        (OrderStatus.Pending,    OrderStatus.Cancelled,  "cancel"),
        (OrderStatus.Processing, OrderStatus.Cancelled,  "cancel"),
    ];
}
```

`POST /api/orders/{id}/transition/process` moves the order from `Pending` to `Processing`. Invalid transitions return `400`. The `[Protected]` attribute prevents `Status` from being set directly through the update DTO.

---

### Validation

FluentValidation validators are resolved from DI and run first. If no FluentValidation validator is registered for a DTO, DataAnnotation attributes are evaluated instead. Validation errors return `400` with a structured body:

```json
{
  "status": 400,
  "code": "VALIDATION_ERROR",
  "errors": [
    { "field": "Name", "message": "The Name field is required." }
  ]
}
```

Register a FluentValidation validator:

```csharp
public class CreateProductValidator : AbstractValidator<CreateProduct>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().Matches("^[A-Z0-9-]+$");
    }
}

builder.Services.AddScoped<IValidator<CreateProduct>, CreateProductValidator>();
```

---

### Auth

Use the fluent extension methods on the returned `CrudEndpointGroup<T>` or directly on `RouteGroupBuilder`:

```csharp
// Require authentication on all endpoints for this resource
app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>("orders")
    .RequireAuth();

// Require a role
app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>("products")
    .RequireRole("admin");

// Require a specific permission
app.MapCrudEndpoints<Category, CreateCategory, UpdateCategory>("categories")
    .RequirePermission("Category", "write");
```

Unauthenticated requests return `401`. Forbidden requests return `403`.

---

### Query Features

All List endpoints support filtering, sorting, pagination, and eager loading via query string.

**Filtering**

```
GET /api/products?name=like:phone
GET /api/products?price=gte:100
GET /api/products?status=in:active,pending
GET /api/products?deletedAt=null
```

Supported operators: `eq`, `neq`, `gt`, `gte`, `lt`, `lte`, `like`, `starts`, `in`, `null`, `notnull`

**Sorting**

```
GET /api/products?sort=price          # ascending
GET /api/products?sort=-created_at    # descending
```

**Pagination**

```
GET /api/products?page=2&per_page=25
```

Response includes `total`, `page`, `per_page`, and `data`.

**Includes**

```
GET /api/orders?include=lines
```

Loads related navigation properties. Configure defaults with `[DefaultInclude]` on the entity.

---

### Optional\<T\> Partial Updates

`Optional<T>` solves the partial update problem without requiring PATCH semantics. Use it in Update DTOs for every field you want to be optionally settable:

```csharp
public record UpdateProduct
{
    public Optional<string?> Name { get; init; }
    public Optional<decimal?> Price { get; init; }
    public Optional<string?> Description { get; init; }
}
```

Sending `{"price": 9.99}` updates only `Price`. Sending `{"description": null}` explicitly clears `Description`. Fields absent from the body are not touched.

---

### Bulk Operations

Enable bulk endpoints on an entity:

```csharp
[CrudEntity(Table = "products", EnableBulkDelete = true, EnableBulkUpdate = true, BulkLimit = 500)]
public class Product : IEntity { ... }
```

| Method | Route | Description |
|---|---|---|
| POST | `/api/products/bulk-count` | Count matching records by filter |
| POST | `/api/products/bulk-delete` | Delete multiple records by IDs |
| POST | `/api/products/bulk-update` | Update multiple records by IDs |

The global default bulk limit is set via `CrudKitApiOptions.BulkLimit` (default: 10,000). Override per entity with `BulkLimit`.

---

### Property Attributes

| Attribute | Target | Effect |
|---|---|---|
| `[Hashed]` | Property | BCrypt-hashes the value on Create and Update (e.g. passwords) |
| `[SkipResponse]` | Property | Excluded from JSON responses (e.g. password hashes, internal tokens) |
| `[SkipUpdate]` | Property | Set only on Create, ignored on Update (e.g. immutable SKU) |
| `[Protected]` | Property | Cannot be set via Update DTO — managed by workflow or hooks only |
| `[Unique]` | Property | Creates a partial unique index (soft-delete compatible) |
| `[Searchable]` | Property | Included in global search queries |
| `[DefaultInclude]` | Class | Auto-includes navigation property in queries (with `IncludeScope.All` or `DetailOnly`) |

```csharp
[CrudEntity(Table = "users", SoftDelete = true)]
public class User : IEntity, ISoftDeletable
{
    public string Id { get; set; } = string.Empty;

    [Required, MaxLength(50), Searchable, Unique]
    public string Username { get; set; } = string.Empty;

    [Required, Hashed, SkipResponse]
    public string PasswordHash { get; set; } = string.Empty;

    [SkipUpdate]
    public string RegistrationSource { get; set; } = "web";

    [Protected]
    public string Role { get; set; } = "user";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

`[Hashed]` applies BCrypt on both Create and Update — if the user sends a new password, it gets hashed. `[SkipResponse]` ensures `PasswordHash` never appears in API responses.

---

### Document Numbering

Auto-generate sequential document numbers scoped by entity type, tenant, and year:

```csharp
[CrudEntity(Table = "invoices", NumberingPrefix = "INV", NumberingYearlyReset = true)]
public class Invoice : IEntity, IDocumentNumbering
{
    public string Id { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty; // INV-2026-00001
    // ...
    public static string Prefix => "INV";
    public static bool YearlyReset => true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

The `SequenceGenerator` uses optimistic concurrency with retry to handle concurrent numbering safely. Sequences are tenant-scoped in multi-tenant applications.

---

### Modular Monolith

Implement `IModule` to self-register services and endpoints per domain:

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
        app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>("orders")
           .WithDetail<OrderLine, CreateOrderLine>("lines", "OrderId")
           .RequireAuth();
    }
}
```

Modules are discovered automatically via assembly scan:

```csharp
builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.ScanModulesFromAssembly = typeof(Program).Assembly;
});
```

Or register manually:

```csharp
builder.Services.AddCrudKitModule<OrderModule>();
```

`UseCrudKit()` calls `MapEndpoints` on all discovered modules.

---

### Database Dialect

CrudKit auto-detects the database provider and adapts SQL generation accordingly. Supported providers:

| Provider | Dialect | LIKE behavior |
|---|---|---|
| SQLite | `SqliteDialect` | `LIKE` (case-insensitive by default) |
| PostgreSQL | `PostgresDialect` | `ILIKE` (case-insensitive) |
| SQL Server | `SqlServerDialect` | `EF.Functions.Like` |
| Other | `GenericDialect` | `LIKE` fallback |

No configuration needed — the dialect is detected from the EF Core provider name at startup.

---

### Enum Storage

All enum properties on any entity are automatically stored as strings in the database. No `HasConversion` configuration needed — `CrudKitDbContext` handles this in `OnModelCreating`.

```csharp
public OrderStatus Status { get; set; } = OrderStatus.Pending;
// Stored in DB as "Pending", not 0
```

---

### Domain Events

CrudKit defines an `IEventBus` interface and event types. The framework does **not** publish events automatically — you publish from hooks using your preferred infrastructure (MediatR, MassTransit, etc.):

```csharp
public class OrderHooks : ICrudHooks<Order>
{
    private readonly IEventBus _eventBus;
    public OrderHooks(IEventBus eventBus) => _eventBus = eventBus;

    public async Task AfterCreate(Order entity, AppContext ctx)
    {
        await _eventBus.Publish(new EntityCreatedEvent
        {
            EntityType = nameof(Order),
            EntityId = entity.Id
        });
    }
}
```

---

### Error Handling

`AppErrorFilter` catches all exceptions and returns structured JSON:

| Exception | Status | Code |
|---|---|---|
| `AppError.NotFound()` | 404 | `NOT_FOUND` |
| `AppError.BadRequest()` | 400 | `BAD_REQUEST` |
| `AppError.Validation()` | 400 | `VALIDATION_ERROR` |
| `AppError.Unauthorized()` | 401 | `UNAUTHORIZED` |
| `AppError.Forbidden()` | 403 | `FORBIDDEN` |
| `AppError.Conflict()` | 409 | `CONFLICT` |
| `DbUpdateConcurrencyException` | 409 | `CONFLICT` |
| Unhandled exception | 500 | `INTERNAL_ERROR` |

In Development, unhandled exceptions include the full stack trace. In Production, only a generic message is returned.

---

### Configuration Options

```csharp
builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.DefaultPageSize = 25;           // Default: 20
    opts.MaxPageSize = 100;              // Default: 100
    opts.ApiPrefix = "/api";             // Default: "/api"
    opts.BulkLimit = 10_000;             // Default: 10,000
    opts.EnableIdempotency = true;       // Default: false
    opts.ScanModulesFromAssembly = typeof(Program).Assembly;
});
```

When `EnableIdempotency` is true, clients can send an `Idempotency-Key` header on mutating requests. The response is cached and replayed on duplicate requests. Expired records are cleaned up hourly by a background service.

---

### Source Generation

Add the `CrudKit.SourceGen` package to your project. The Roslyn source generator scans for all classes decorated with `[CrudEntity]` at compile time and generates:

- `{Entity}CreateDto.g.cs` — Create DTO record
- `{Entity}UpdateDto.g.cs` — Update DTO record with `Optional<T>` fields
- `{Entity}ResponseDto.g.cs` — Response DTO
- `{Entity}Mapper.g.cs` — `ICrudMapper` implementation
- `{Entity}Hooks.g.cs` — Partial hook stub to extend
- `CrudKitEndpoints.g.cs` — `MapAllCrudEndpoints()` extension method
- `CrudKitMappers.g.cs` — DI registration for all mappers

Generated endpoint mapping:

```csharp
// Single call maps all entities
app.MapAllCrudEndpoints();
```

Use the generated hook stub as a starting point:

```csharp
// Extend the generated partial class
public partial class ProductHooks
{
    public override Task BeforeCreate(Product entity, AppContext ctx)
    {
        // your logic
        return Task.CompletedTask;
    }
}
```

---

## Project Structure

```
src/
├── CrudKit.Core/               # Attributes, interfaces, models
├── CrudKit.EntityFrameworkCore/ # EF Core integration, repository, query
├── CrudKit.Api/                # Minimal API layer, endpoint mapping, filters
└── CrudKit.SourceGen/          # Roslyn source generator
tests/
├── CrudKit.Core.Tests/
├── CrudKit.EntityFrameworkCore.Tests/
├── CrudKit.Api.Tests/
└── CrudKit.SourceGen.Tests/
samples/
└── CrudKit.Sample.Api/         # Working sample with Product, Category, Order, Unit
```

---

## License

MIT
