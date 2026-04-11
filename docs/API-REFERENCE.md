# CrudKit API Reference

## Table of Contents

1. [Entity Base Classes](#entity-base-classes)
2. [Attributes](#attributes)
   - [Entity-Level Attributes](#entity-level-attributes)
   - [Property-Level Attributes](#property-level-attributes)
3. [Interfaces](#interfaces)
4. [Endpoint Mapping](#endpoint-mapping)
5. [Authorization](#authorization)
6. [Multi-Tenancy](#multi-tenancy)
7. [Soft Delete](#soft-delete)
8. [Audit Trail](#audit-trail)
9. [Lifecycle Hooks](#lifecycle-hooks)
10. [Validation](#validation)
11. [Query Features](#query-features)
12. [Import / Export](#import--export)
13. [Bulk Operations](#bulk-operations)
14. [State Machine](#state-machine)
15. [Optimistic Concurrency](#optimistic-concurrency)
17. [Modular Monolith](#modular-monolith)
18. [Source Generation](#source-generation)
19. [Configuration Reference](#configuration-reference)
20. [Error Handling](#error-handling)
21. [Database Dialect](#database-dialect)
22. [TimeProvider](#timeprovider)
23. [Testing](#testing)
24. [ASP.NET Identity Integration](#aspnet-identity-integration)
25. [Migrations](#migrations)

---

## Entity Base Classes

All entities must derive from one of CrudKit's base classes. Pick the level that matches the data you need to track.

| Class | Key | Provides |
|-------|-----|----------|
| `Entity` | `Guid` | `Id` only |
| `Entity<TKey>` | Custom | `Id` with any non-null key type |
| `AuditableEntity` | `Guid` | `Id`, `CreatedAt`, `UpdatedAt` |
| `AuditableEntity<TKey>` | Custom | same + custom key |
| `AuditableEntityWithUser<TUser>` | `Guid` | + `CreatedById`, `UpdatedById`, `CreatedBy`, `UpdatedBy` navigations |
| `AuditableEntityWithUser<TKey, TUser, TUserKey>` | Custom | same with explicit key types |
| `FullAuditableEntity` | `Guid` | `AuditableEntity` + `DeletedAt` (implements `ISoftDeletable`) |
| `FullAuditableEntity<TKey>` | Custom | same + custom key |
| `FullAuditableEntityWithUser<TUser>` | `Guid` | + `DeletedById`, `DeletedBy` navigation |
| `FullAuditableEntityWithUser<TKey, TUser, TUserKey>` | Custom | same with explicit key types |

`CreatedById`, `UpdatedById`, and `DeletedById` are set automatically from `ICurrentUser.Id` in `SaveChanges`. `CreatedById` is preserved on updates (never overwritten). Key type conversion from `string` → `Guid`/`int`/`long` is handled automatically.

```csharp
// Guid key (default)
public class Currency : Entity { }
public class Product : AuditableEntity { }
public class Order : FullAuditableEntity { }
public class Invoice : FullAuditableEntityWithUser<AppUser> { }

// Custom key types
public class LegacyProduct : AuditableEntity<long> { }
public class LegacyOrder : FullAuditableEntityWithUser<long, AppUser, int> { }
```

---

## Attributes

### Entity-Level Attributes

#### `[CrudEntity]`

Required on every entity managed by CrudKit. Controls route generation, endpoint availability, and document numbering.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Resource` | `string` | entity name kebab-cased + "s" | API resource name used as the URL route segment (e.g. `"products"` → `/api/products`) |
| `ReadOnly` | `bool` | `false` | Generate List + Get only; no write endpoints |
| `EnableCreate` | `bool` | `true` | Generate POST endpoint |
| `EnableUpdate` | `bool` | `true` | Generate PUT endpoint |
| `EnableDelete` | `bool` | `true` | Generate DELETE endpoint |
| `EnableBulkDelete` | `bool` | `false` | Generate POST `/bulk-delete` endpoint |
| `EnableBulkUpdate` | `bool` | `false` | Generate POST `/bulk-update` endpoint |
| `BulkLimit` | `int` | global | Override global `BulkLimit` for this entity |
| `OwnerField` | `string` | — | Property holding the owner user ID for row-level security |

```csharp
[CrudEntity(
    Resource = "orders",
    EnableBulkDelete = true,
    BulkLimit = 500)]
public class Order : FullAuditableEntity { }
```

#### `[Audited]` / `[NotAudited]`

- `[Audited]` — opt this entity into the audit trail. Requires `UseAuditTrail()` globally. Changes are written to `__crud_audit_logs`.
- `[NotAudited]` — opt this entity out when `UseAuditTrail()` is enabled globally.

#### `[Exportable]` / `[NotExportable]`

- `[Exportable]` — add `GET /export` endpoint for CSV download, regardless of global flag.
- `[NotExportable]` — suppress export endpoint even when `UseExport()` is globally enabled.

#### `[Importable]` / `[NotImportable]`

- `[Importable]` — add `POST /import` endpoint for CSV upload, regardless of global flag.
- `[NotImportable]` — suppress import endpoint even when `UseImport()` is globally enabled.

#### `[RequireAuth]`

All endpoints on this entity require an authenticated user. Unauthenticated requests return `401`.

```csharp
[CrudEntity(Resource = "orders")]
[RequireAuth]
public class Order : FullAuditableEntity { }
```

#### `[RequireRole("role")]`

All endpoints require membership in the specified role.

```csharp
[CrudEntity(Resource = "admin-settings")]
[RequireRole("admin")]
public class AdminSetting : AuditableEntity { }
```

#### `[RequirePermissions]`

Auto-derives convention-based permission names from the resource name. For `Resource = "products"`, requires:
`products:read`, `products:create`, `products:update`, `products:delete`.

```csharp
[CrudEntity(Resource = "products")]
[RequirePermissions]
public class Product : AuditableEntity { }
```

#### `[AuthorizeOperation("Operation", "role")]`

Applies a role restriction to a specific operation only. Operations: `"Read"`, `"Create"`, `"Update"`, `"Delete"`.

```csharp
[CrudEntity(Resource = "invoices")]
[RequireAuth]
[AuthorizeOperation("Create", "manager")]
[AuthorizeOperation("Delete", "admin")]
public class Invoice : FullAuditableEntity { }
```

#### `[CascadeSoftDelete(typeof(TChild), nameof(TChild.ForeignKey))]`

When the parent is soft-deleted, all matching child records are soft-deleted in the same operation using a raw SQL `UPDATE` (no N+1 queries). Restore also cascades — restoring the parent restores all its children.

```csharp
[CrudEntity(Resource = "orders")]
[CascadeSoftDelete(typeof(OrderLine), nameof(OrderLine.OrderId))]
public class Order : FullAuditableEntity { }
```

#### `[ChildOf(typeof(TParent))]`

Declares a child entity and its parent. CrudKit generates nested REST endpoints under the parent route automatically — no manual `.WithChild()` call needed. The foreign key is resolved by convention (`{ParentType}Id`), or specified explicitly.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ParentType` | `Type` | — | The parent entity type |
| `Route` | `string` | pluralized child name | Route segment under the parent (e.g. `"items"`) |
| `ForeignKey` | `string` | `"{ParentType}Id"` | Name of the FK property on the child entity |

```csharp
[ChildOf(typeof(Order))]
public class OrderLine : AuditableEntity
{
    public Guid OrderId { get; set; }           // FK convention
    public string ProductName { get; set; } = string.Empty;
}

// Custom route + FK
[ChildOf(typeof(Order), Route = "items", ForeignKey = "ParentOrderId")]
public class OrderItem : AuditableEntity { }
```

#### `[CreateDtoFor(typeof(TEntity))]` / `[UpdateDtoFor(typeof(TEntity))]`

Applied to a manually written DTO record or class. Signals SourceGen to skip generating the corresponding DTO for the target entity. `ResponseDto` and mapper are still generated.

```csharp
[CreateDtoFor(typeof(Order))]
public record CreateOrder([Required] string CustomerName, decimal Total = 0);

[UpdateDtoFor(typeof(Order))]
public record UpdateOrder
{
    public Optional<string?> CustomerName { get; init; }
}
```

---

### Property-Level Attributes

| Attribute | Target | Effect |
|-----------|--------|--------|
| `[Hashed]` | Property | BCrypt-hashes the value on Create and Update. Masked as `"***"` in audit trail. |
| `[SkipResponse]` | Property | Excluded from API response JSON (e.g. password hashes). Still audited unless also `[AuditIgnore]`. |
| `[SkipUpdate]` | Property | Set only on Create; ignored on Update (e.g. immutable SKU). |
| `[Protected]` | Property | Cannot be set through the Update DTO. Managed by workflow, hooks, or state machine only. |
| `[Unique]` | Property | Creates a partial unique index (soft-delete compatible). Checked on restore — conflicts return `409`. |
| `[Searchable]` | Property | Included in global full-text search queries. |
| `[AuditIgnore]` | Property | Completely excluded from audit trail. Field never appears in old/new change values. |
| `[NotExportable]` | Property | Excluded from CSV export output. |
| `[NotImportable]` | Property | Ignored during CSV import. |
| `[Filterable]` | Class / Property | Force-enable filtering (overrides entity-level `[NotFilterable]`) |
| `[NotFilterable]` | Class / Property | Disable filtering — queries with this field are silently skipped |
| `[Sortable]` | Class / Property | Force-enable sorting (overrides entity-level `[NotSortable]`) |
| `[NotSortable]` | Class / Property | Disable sorting — sort by this field is silently skipped |
| `[DefaultInclude]` | Class | Auto-includes a navigation property in queries. Supports `IncludeScope.All` or `IncludeScope.DetailOnly`. |

```csharp
[CrudEntity(Resource = "users")]
[Audited]
public class User : FullAuditableEntity
{
    [Required, MaxLength(50), Searchable, Unique]
    public string Username { get; set; } = string.Empty;

    [Required, Hashed, SkipResponse]
    public string PasswordHash { get; set; } = string.Empty;

    [SkipUpdate]
    public string RegistrationSource { get; set; } = "web";

    [Protected]
    public string Role { get; set; } = "user";

    [AuditIgnore]
    public string SecurityStamp { get; set; } = string.Empty;

    [NotExportable]
    public string InternalToken { get; set; } = string.Empty;
}
```

#### Filter & Sort Control

Default: all fields are filterable and sortable. Use `[NotFilterable]`, `[NotSortable]`, `[Filterable]`, and `[Sortable]` to override behavior per-property or per-entity.

```csharp
// Property-level control
public class Order : FullAuditableEntity
{
    public string CustomerName { get; set; }    // filterable + sortable (default)

    [NotFilterable]
    public string InternalNotes { get; set; }    // NOT filterable

    [NotSortable]
    public decimal Total { get; set; }           // NOT sortable
}

// Entity-level + property override
[NotFilterable]
public class SecureEntity : AuditableEntity
{
    [Filterable]                                  // override entity default
    public string PublicField { get; set; }

    public string SecretField { get; set; }       // NOT filterable (entity default)
}
```

---

## Interfaces

### `ISoftDeletable`

Marks an entity as soft-deletable. `CrudKitDbContext` applies a global query filter excluding records where `DeletedAt != null`. Implemented by `FullAuditableEntity` and its variants.

```csharp
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
}
```

### `IMultiTenant` / `IMultiTenant<TTenant>` / `IMultiTenant<TTenant, TTenantKey>`

Marks an entity as tenant-scoped. `CrudKitDbContext` automatically applies `WHERE TenantId = X` and sets `TenantId` on create.

```csharp
// Basic — string TenantId
public class Order : FullAuditableEntity, IMultiTenant
{
    public string TenantId { get; set; } = string.Empty;
}

// With navigation property
public class Order : FullAuditableEntity, IMultiTenant<Tenant>
{
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }
}

// With typed tenant key
public class Order : FullAuditableEntity, IMultiTenant<Tenant, Guid>
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
}
```

### `IConcurrent`

Enables optimistic concurrency detection via `RowVersion`. `CrudKitDbContext` configures `RowVersion` as a concurrency token. Conflicts return `409`.

```csharp
public interface IConcurrent
{
    uint RowVersion { get; set; }
}
```

### `IStateMachine<TState>`

Adds state machine behavior to an entity. Defines valid `(From, To, Action)` transitions. CrudKit maps `POST /{id}/transition/{action}` automatically.

```csharp
public interface IStateMachine<TState> where TState : struct, Enum
{
    TState Status { get; set; }
    static abstract IReadOnlyList<(TState From, TState To, string Action)> Transitions { get; }
}
```

### `ICrudHooks<T>`

Per-entity lifecycle hooks. All methods have empty default implementations — override only what you need.

```csharp
public interface ICrudHooks<T> where T : class, IEntity
{
    Task BeforeCreate(T entity, AppContext ctx);
    Task AfterCreate(T entity, AppContext ctx);
    Task BeforeUpdate(T entity, AppContext ctx);
    Task AfterUpdate(T entity, AppContext ctx);
    Task BeforeDelete(T entity, AppContext ctx);
    Task AfterDelete(T entity, AppContext ctx);
    Task BeforeRestore(T entity, AppContext ctx);
    Task AfterRestore(T entity, AppContext ctx);
    IQueryable<T> ApplyScope(IQueryable<T> query, AppContext ctx);
    IQueryable<T> ApplyIncludes(IQueryable<T> query);
}
```

### `IGlobalCrudHook`

Runs for all entities on every CRUD operation. Register via `opts.UseGlobalHook<T>()`. Multiple global hooks run in registration order.

```csharp
public interface IGlobalCrudHook
{
    Task BeforeCreate(object entity, AppContext ctx);
    Task AfterCreate(object entity, AppContext ctx);
    Task BeforeUpdate(object entity, AppContext ctx);
    Task AfterUpdate(object entity, AppContext ctx);
    Task BeforeDelete(object entity, AppContext ctx);
    Task AfterDelete(object entity, AppContext ctx);
}
```

### `IAuditWriter`

Custom audit log destination. Implement this to write audit entries to any storage backend.

```csharp
public interface IAuditWriter
{
    Task WriteAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct);
}
```

### `ITenantContext`

Provides the resolved tenant ID for the current request. Populated by the configured tenant resolver. Separate from `ICurrentUser` — a request can be tenant-scoped without authentication.

### `ICurrentUser`

Represents the authenticated user for the current request. Must be implemented by the application layer.

```csharp
public interface ICurrentUser
{
    string? Id { get; }
    string? Username { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsAuthenticated { get; }
    bool HasRole(string role);
    bool HasPermission(string entity, string action);
    IReadOnlyList<string>? AccessibleTenants { get; }  // null = all tenants
}
```

### `IResponseMapper`, `ICreateMapper`, `IUpdateMapper`, `ICrudMapper`

Mapper interfaces for converting between entities and DTOs. **All mappers are optional** — the framework works without them.

**Without mappers (default):**
- **Create/Update** — reflection-based DTO → entity mapping. Respects `[Protected]`, `[SkipUpdate]`, `[Hashed]`, `Optional<T>` automatically.
- **Response** — entity is serialized directly. `[SkipResponse]` fields are set to null and excluded from JSON.

**With mappers (when registered in DI):**
- **Create** — `ICreateMapper<T, TCreate>.FromCreateDto()` replaces reflection
- **Update** — `IUpdateMapper<T, TUpdate>.ApplyUpdate()` replaces reflection
- **Response** — `IResponseMapper<T, TResponse>.Map()` returns a custom-shaped DTO

```csharp
// No mapper needed — this works out of the box
app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>();

// Optional: register a mapper for custom response shape
public class ProductMapper : IResponseMapper<Product, ProductResponse>
{
    public ProductResponse Map(Product entity)
        => new(entity.Id, entity.Name, entity.Price, $"{entity.Name} ({entity.Sku})");

    public IQueryable<ProductResponse> Project(IQueryable<Product> query)
        => query.Select(e => new ProductResponse(e.Id, e.Name, e.Price, e.Name + " (" + e.Sku + ")"));
}

builder.Services.AddScoped<IResponseMapper<Product, ProductResponse>, ProductMapper>();
```

SourceGen generates `ICrudMapper` implementations automatically — combining all three interfaces. When registered via `AddAllCrudMappers()`, reflection is fully bypassed.

- `IResponseMapper<T, TResponse>` — entity → response DTO
- `ICreateMapper<T, TCreate>` — create DTO → entity
- `IUpdateMapper<T, TUpdate>` — apply update DTO to existing entity
- `ICrudMapper<T, TCreate, TUpdate, TResponse>` — combines all three

### `IDataFilter<T>`

Runtime toggle for global query filters (soft-delete and tenant filters) on a specific entity. Inject as a scoped service and call `Disable<TFilter>()` inside a `using` block — the filter is re-enabled when the block exits.

```csharp
// Temporarily include soft-deleted records
using (_dataFilter.Disable<ISoftDeletable>())
{
    var all = await _repo.ListAsync(); // includes deleted rows
}

// Re-enabled automatically after the using block
```

`Disable<ITenantFilter>()` is deliberately not supported — the tenant filter is always active and cannot be disabled at runtime. Use `CrossTenantPolicy` in configuration to grant cross-tenant access to specific roles.

### `IModule`

Self-registers services and endpoints for one bounded context. Discovered via assembly scan or manual registration.

```csharp
public interface IModule
{
    string Name { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
    void MapEndpoints(WebApplication app);
}
```

---

## Endpoint Mapping

### Overloads

```csharp
// Full CRUD — route derived from [CrudEntity(Resource = ...)]
app.MapCrudEndpoints<TEntity, TCreate, TUpdate>();

// ReadOnly — List + Get only
app.MapCrudEndpoints<TEntity>();

// With explicit route prefix
app.MapCrudEndpoints<TEntity, TCreate, TUpdate>("products");
app.MapCrudEndpoints<TEntity>("units");
```

### Generated Endpoints

For an entity with `[CrudEntity(Resource = "products")]`:

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/products` | List (paginated, filtered, sorted) |
| GET | `/api/products/{id}` | Get by ID |
| POST | `/api/products` | Create |
| PUT | `/api/products/{id}` | Update (partial via `Optional<T>`) |
| DELETE | `/api/products/{id}` | Delete (soft-delete if `ISoftDeletable`) |
| POST | `/api/products/{id}/restore` | Restore (`ISoftDeletable` only) |
| DELETE | `/api/products/purge?olderThan=N` | Permanent hard-delete of soft-deleted records (`ISoftDeletable` only) |
| POST | `/api/products/{id}/transition/{action}` | State transition (`IStateMachine<TState>` only) |
| GET | `/api/products/export` | CSV export (`[Exportable]` or `UseExport()`) |
| POST | `/api/products/import` | CSV import (`[Importable]` or `UseImport()`) |
| POST | `/api/products/bulk-count` | Count by filter (`EnableBulkDelete` or `EnableBulkUpdate`) |
| POST | `/api/products/bulk-delete` | Delete by IDs (`EnableBulkDelete = true`) |
| POST | `/api/products/bulk-update` | Update by IDs (`EnableBulkUpdate = true`) |

### `[ChildOf]` — Declarative Child Endpoints

Annotate a child entity with `[ChildOf(typeof(TParent))]` to generate nested endpoints automatically when the parent is registered. No fluent call required.

```csharp
[ChildOf(typeof(Order))]
public class OrderLine : AuditableEntity
{
    public Guid OrderId { get; set; }
    public string ProductName { get; set; } = string.Empty;
}

// Auto-generated when Order is mapped:
// GET    /api/orders/{id}/order-lines
// GET    /api/orders/{id}/order-lines/{id}
// DELETE /api/orders/{id}/order-lines/{id}
// POST   /api/orders/{id}/order-lines  (if [CreateDtoFor(typeof(OrderLine))] exists)
```

### `.WithChild<TDetail, TCreate>()`

Fluent alternative for explicit child endpoint registration. Overrides or supplements `[ChildOf]`.

```csharp
app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>()
    .WithChild<OrderLine, CreateOrderLine>("lines", "OrderId");
```

| Method | Route |
|--------|-------|
| GET | `/api/orders/{masterId}/lines` |
| GET | `/api/orders/{masterId}/lines/{id}` |
| POST | `/api/orders/{masterId}/lines` |
| DELETE | `/api/orders/{masterId}/lines/{id}` |
| PUT | `/api/orders/{masterId}/lines/batch` |

The `batch` endpoint replaces all child records for a master in a single transaction.

### `.WithCustomEndpoints()`

Add custom endpoints under the same route group as the entity.

```csharp
app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>()
    .WithCustomEndpoints(group =>
    {
        group.MapPost("/{id}/approve", OrderEndpoints.Approve)
             .AddEndpointFilter(new RequireRoleFilter("manager"));
    });
```

### `MapAllCrudEndpoints()` (SourceGen)

Generated by `CrudKit.SourceGen`. Maps all `[CrudEntity]`-decorated types in a single call.

```csharp
app.MapAllCrudEndpoints();
```

---

## Authorization

### Entity-Level (Recommended)

Auth declared on the entity travels with it across all registrations.

```csharp
// Require any authenticated user
[RequireAuth]
public class Order : FullAuditableEntity { }

// Require a specific role for all operations
[RequireRole("admin")]
public class AdminSetting : AuditableEntity { }

// Convention permissions: table:read, table:create, table:update, table:delete
[RequirePermissions]
public class Product : AuditableEntity { }

// Per-operation role overrides
[RequireAuth]
[AuthorizeOperation("Create", "manager")]
[AuthorizeOperation("Delete", "admin")]
public class Invoice : FullAuditableEntity { }
```

### Fluent API

Stacks additional restrictions on top of entity-level auth. Does not replace entity attributes.

```csharp
app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>()
    .Authorize(auth =>
    {
        auth.Read.RequireRole("user");
        auth.Delete.RequireRole("admin");
    });

// Convention permissions via fluent
app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>()
    .Authorize(auth => auth.RequirePermissions());
```

### Behavior

| Scenario | Response |
|----------|----------|
| No auth configured | Endpoints are public |
| Auth configured, no token | `401 Unauthorized` |
| Authenticated, wrong role | `403 Forbidden` |
| Entity + fluent both restrict | Both must be satisfied |

---

## Multi-Tenancy

### Mark Entities

```csharp
public class Order : FullAuditableEntity, IMultiTenant
{
    public string TenantId { get; set; } = string.Empty;
}
```

`CrudKitDbContext` automatically applies `WHERE TenantId = X` as a global query filter and sets `TenantId` on create.

### Configure Tenant Resolution

```csharp
opts.UseMultiTenancy()
    .ResolveTenantFromHeader("X-Tenant-Id");   // HTTP header

opts.UseMultiTenancy()
    .ResolveTenantFromClaim("tenant_id");       // JWT claim

opts.UseMultiTenancy()
    .ResolveTenantFromSubdomain();              // acme.app.com → "acme"

opts.UseMultiTenancy()
    .ResolveTenantFromRoute("tenantId");        // /api/{tenantId}/products

opts.UseMultiTenancy()
    .ResolveTenantFromQuery("tenant");          // ?tenant=acme
```

### Cross-Tenant Protection

Three-layer enforcement:

```csharp
opts.UseMultiTenancy()
    .ResolveTenantFromHeader("X-Tenant-Id")
    .RejectUnresolvedTenant()                    // missing header → 400
    .CrossTenantPolicy(policy =>
    {
        policy.Allow("superadmin");              // full access to all tenants
        policy.AllowReadOnly("support");          // read-only across tenants
        policy.AllowReadOnly("auditor")
              .Only<Order, Invoice>();             // read-only, restricted to these types
    });
```

| Layer | Condition | Response |
|-------|-----------|----------|
| Middleware | Tenant not resolved + `RejectUnresolvedTenant()` | `400 TENANT_REQUIRED` |
| Middleware | Tenant not in `ICurrentUser.AccessibleTenants` | `403 TENANT_ACCESS_DENIED` |
| Middleware | ReadOnly role + mutation (POST/PUT/DELETE) | `403 CROSS_TENANT_READ_ONLY` |
| Startup | `IMultiTenant` entity exists but no resolver configured | Warning log |
| EfRepo | Null tenant + multi-tenant entity | `400` guard |

`ICurrentUser.AccessibleTenants`:
- `null` — all tenants (superadmin)
- `["acme", "globex"]` — only listed tenants
- `[]` — no cross-tenant access

---

## Soft Delete

Use `FullAuditableEntity` (or implement `ISoftDeletable` directly) for soft-delete behavior. `DELETE` sets `DeletedAt` instead of removing the row. Soft-deleted records are excluded from all queries automatically via a global query filter.

```csharp
[CrudEntity(Resource = "categories")]
public class Category : FullAuditableEntity
{
    public string Name { get; set; } = string.Empty;
}
```

The restore endpoint is mapped automatically: `POST /api/categories/{id}/restore`.

### Cascade Soft Delete

```csharp
[CrudEntity(Resource = "orders")]
[CascadeSoftDelete(typeof(OrderLine), nameof(OrderLine.OrderId))]
public class Order : FullAuditableEntity { }
```

Uses raw SQL `UPDATE` — no N+1 queries. Restore also cascades to all children.

### Restore with Unique Constraint Check

When restoring a soft-deleted entity, CrudKit checks all `[Unique]` properties against currently active records. If a conflict exists, the restore fails with `409 Conflict`.

### Purge Endpoint

`DELETE /api/{entity}/purge?olderThan=30` permanently removes all soft-deleted records for an `ISoftDeletable` entity that were deleted more than N days ago. Returns `{ "purged": <count> }`.

- `olderThan` is required (minimum 1).
- Uses `ExecuteDeleteAsync` — bypasses EF change tracking and soft-delete interception (real hard delete).
- Respects tenant isolation for `IMultiTenant` entities.

```http
DELETE /api/products/purge?olderThan=30
→ 200 { "purged": 15 }
```

---

## Audit Trail

Records Create, Update, and Delete operations to `__crud_audit_logs` with old/new property values.

### Setup

**1. Enable globally:**

```csharp
opts.UseAuditTrail();
```

**2. Opt entities in:**

```csharp
[CrudEntity(Resource = "orders")]
[Audited]
public class Order : FullAuditableEntity { }
```

**3. Control property visibility:**

| Attribute | Audit behavior |
|-----------|----------------|
| Normal property | Logged with old/new values |
| `[Hashed]` | Change recorded, value masked as `"***"` |
| `[AuditIgnore]` | Field completely excluded from audit log |

**4. Log failed operations (compliance):**

```csharp
opts.UseAuditTrail()
    .EnableAuditFailedOperations();
// Logs: FailedCreate, FailedUpdate, FailedDelete
```

**5. Correlation ID:**

Each `SaveChanges` call assigns a shared `CorrelationId` to all audit entries in that batch. Use it to group related changes (e.g. cascade soft-delete of Order + OrderLines).

**6. Custom audit writer:**

```csharp
opts.UseAuditTrail<ElasticAuditWriter>();

public class ElasticAuditWriter : IAuditWriter
{
    public async Task WriteAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct)
    {
        // Write to Elasticsearch, separate DB, file, etc.
    }
}
```

If `UseAuditTrail()` is not called, `[Audited]` is silently ignored and `__crud_audit_logs` is not created.

### Feature Flag Override (3 Levels)

```
Property attribute > Entity attribute > Global flag
```

| Level | Enable | Disable |
|-------|--------|---------|
| Global | `opts.UseAuditTrail()` | — (off by default) |
| Entity | `[Audited]` | `[NotAudited]` |
| Property | — | `[AuditIgnore]` |

The same 3-level override applies to Export and Import:

| Feature | Global | Entity On | Entity Off | Property Off |
|---------|--------|-----------|------------|--------------|
| Export | `UseExport()` | `[Exportable]` | `[NotExportable]` | `[NotExportable]` |
| Import | `UseImport()` | `[Importable]` | `[NotImportable]` | `[NotImportable]` |
| Audit | `UseAuditTrail()` | `[Audited]` | `[NotAudited]` | `[AuditIgnore]` |

---

## Lifecycle Hooks

### Per-Entity Hooks (`ICrudHooks<T>`)

Intercept Create, Update, Delete, and Restore for a specific entity type.

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

builder.Services.AddScoped<ICrudHooks<Product>, ProductHooks>();
```

All hook methods have empty default implementations. Override only what you need.

**Execution order:** `Validate` → `Before*` → DB operation → `After*`

### Row-Level Security with `ApplyScope`

```csharp
public class OrderHooks : ICrudHooks<Order>
{
    public IQueryable<Order> ApplyScope(IQueryable<Order> query, AppContext ctx)
    {
        if (!ctx.CurrentUser.HasRole("admin"))
            return query.Where(o => o.CreatedById == ctx.CurrentUser.Id);
        return query;
    }
}
```

`ApplyScope` is applied to `List`, `FindById`, and `FindByIdOrDefault`. A record outside the scope returns `404` on `FindById`.

### Custom Includes with `ApplyIncludes`

```csharp
public class OrderHooks : ICrudHooks<Order>
{
    public IQueryable<Order> ApplyIncludes(IQueryable<Order> query)
        => query.Include(o => o.Lines).ThenInclude(l => l.Product);
}
```

`ApplyIncludes` is applied before `[DefaultInclude]` attributes.

### Global Hooks (`IGlobalCrudHook`)

Run for all entities. Register via `opts.UseGlobalHook<T>()`.

```csharp
public class SearchIndexHook : IGlobalCrudHook
{
    private readonly ISearchService _search;
    public SearchIndexHook(ISearchService search) => _search = search;

    public async Task AfterCreate(object entity, AppContext ctx)
        => await _search.Index(entity);

    public async Task AfterUpdate(object entity, AppContext ctx)
        => await _search.Index(entity);

    public async Task AfterDelete(object entity, AppContext ctx)
        => await _search.Remove(entity);
}

// Register
builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.UseGlobalHook<SearchIndexHook>();
    opts.UseGlobalHook<CacheInvalidationHook>();
});
```

**Execution order:** `Global Before` → `Entity Before` → DB op → `Entity After` → `Global After`

Multiple global hooks run in registration order.

---

## Validation

FluentValidation validators are resolved from DI and run first. If none is registered for a DTO, DataAnnotation attributes are evaluated instead.

Validation errors return `400` with a structured response:

```json
{
  "status": 400,
  "code": "VALIDATION_ERROR",
  "errors": [
    { "field": "Name", "message": "The Name field is required." }
  ]
}
```

### Registering a FluentValidation Validator

```csharp
public class CreateProductValidator : AbstractValidator<CreateProduct>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().Matches("^[A-Z0-9-]+$");
        RuleFor(x => x.Price).GreaterThan(0);
    }
}

builder.Services.AddScoped<IValidator<CreateProduct>, CreateProductValidator>();
```

To register all validators in an assembly at once, use `AddValidatorsFromAssembly` — this is the application's responsibility, not CrudKit's:

```csharp
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
```

### Startup Validation

`CrudKitStartupValidator` runs as an `IHostedService` at startup. It validates entity metadata before the first request:

- `[CrudEntity(OwnerField = "X")]` — verifies property `X` exists on the entity. Throws if missing.
- `IConcurrent` + `EnableBulkUpdate` — logs a warning (bulk updates bypass optimistic concurrency).

---

## Query Features

All List endpoints support filtering, sorting, pagination, and eager loading via query string.

### Filtering

```
GET /api/products?name=like:phone
GET /api/products?price=gte:100
GET /api/products?status=in:active,pending
GET /api/products?deletedAt=null
GET /api/products?price=gt:10&price=lt:500
```

| Operator | Meaning |
|----------|---------|
| `eq` | Equals (default) |
| `neq` | Not equals |
| `gt` | Greater than |
| `gte` | Greater than or equal |
| `lt` | Less than |
| `lte` | Less than or equal |
| `like` | Contains (case-insensitive) |
| `starts` | Starts with |
| `in` | In list (comma-separated) |
| `null` | Is null |
| `notnull` | Is not null |

### Sorting

```
GET /api/products?sort=price          # ascending
GET /api/products?sort=-created_at    # descending (prefix with -)
```

### Pagination

```
GET /api/products?page=2&per_page=25
```

Response envelope:

```json
{
  "total": 143,
  "page": 2,
  "per_page": 25,
  "data": [ ... ]
}
```

### Includes

```
GET /api/orders?include=lines
```

Loads navigation properties. Configure defaults with `[DefaultInclude]` on the entity class. For complex includes (e.g. `ThenInclude`), use `ApplyIncludes` in an `ICrudHooks<T>` implementation.

---

## Import / Export

### Entity Setup

```csharp
[CrudEntity(Resource = "products")]
[Exportable]
[Importable]
public class Product : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    [NotExportable]
    public string InternalCode { get; set; } = string.Empty;

    [NotImportable]
    public string CalculatedField { get; set; } = string.Empty;
}
```

### Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/products/export?format=csv` | Export matching records as CSV |
| POST | `/api/products/import` | Import CSV (multipart form upload) |

Export supports the same filters and sort parameters as List:

```
GET /api/products/export?format=csv&price=gte:100&sort=-name
```

### Import Result

```json
{
  "total": 150,
  "created": 142,
  "failed": 8,
  "errors": [
    { "row": 3, "field": "Name", "message": "Name is required." },
    { "row": 7, "field": "Price", "message": "Cannot convert 'abc' to Decimal" }
  ]
}
```

System fields (`Id`, `CreatedAt`, `UpdatedAt`, etc.) are handled automatically during import and should not appear in the CSV.

### Limits

| Option | Default | Description |
|--------|---------|-------------|
| `MaxExportRows` | `50000` | Maximum rows returned per export request. Requests exceeding this limit return `400`. |
| `MaxImportFileSize` | `10485760` (10 MB) | Maximum allowed upload size for CSV import. Larger files return `400`. |

```csharp
opts.UseExport();
opts.MaxExportRows = 50_000;

opts.UseImport();
opts.MaxImportFileSize = 10 * 1024 * 1024;
```

---

## Bulk Operations

Enable bulk endpoints per entity:

```csharp
[CrudEntity(Resource = "products", EnableBulkDelete = true, EnableBulkUpdate = true, BulkLimit = 500)]
public class Product : AuditableEntity { }
```

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/products/bulk-count` | Count matching records by filter |
| POST | `/api/products/bulk-delete` | Delete multiple records by IDs |
| POST | `/api/products/bulk-update` | Update multiple records by IDs |

The global bulk limit is `CrudKitApiOptions.BulkLimit` (default: 10,000). Override per entity with `[CrudEntity(BulkLimit = N)]`.

**Note:** Bulk updates bypass optimistic concurrency. CrudKit logs a warning at startup if `IConcurrent` and `EnableBulkUpdate` are both configured on the same entity.

---

## State Machine

Implement `IStateMachine<TState>` to add state transition endpoints. CrudKit maps `POST /{id}/transition/{action}` automatically.

```csharp
public enum OrderStatus { Pending, Processing, Completed, Cancelled }

[CrudEntity(Resource = "orders")]
public class Order : FullAuditableEntity, IStateMachine<OrderStatus>
{
    public string CustomerName { get; set; } = string.Empty;
    public decimal Total { get; set; }

    [Protected]
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public static IReadOnlyList<(OrderStatus From, OrderStatus To, string Action)> Transitions =>
    [
        (OrderStatus.Pending,    OrderStatus.Processing, "process"),
        (OrderStatus.Processing, OrderStatus.Completed,  "complete"),
        (OrderStatus.Pending,    OrderStatus.Cancelled,  "cancel"),
        (OrderStatus.Processing, OrderStatus.Cancelled,  "cancel"),
    ];
}
```

`POST /api/orders/{id}/transition/process` — moves from `Pending` to `Processing`.

Invalid transitions return `400`. Use `[Protected]` on the `Status` field to prevent it from being set directly via the Update DTO.

---

## Optimistic Concurrency

Implement `IConcurrent` to enable automatic concurrency conflict detection.

```csharp
[CrudEntity(Resource = "products")]
public class Product : AuditableEntity, IConcurrent
{
    public string Name { get; set; } = string.Empty;
    public uint RowVersion { get; set; }  // auto-incremented on every update
}
```

`CrudKitDbContext` configures `RowVersion` as a concurrency token using the active database dialect. The dialect controls how the token is stored and incremented:

| Dialect | Strategy |
|---------|----------|
| SQLite / PostgreSQL / SQL Server | `uint` column auto-incremented by CrudKit on every `SaveChanges` |

When two requests update the same entity simultaneously, the second receives `409 Conflict`. The client must include the current `RowVersion` in the update request.

---

## Modular Monolith

### IModule

Each bounded context implements `IModule` to self-register its services and endpoints.

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

### Assembly Scan

```csharp
// Automatically discovers all IModule implementations
opts.ScanModulesFromAssembly = typeof(Program).Assembly;
```

Or register manually:

```csharp
builder.Services.AddCrudKitModule<OrderModule>();
```

`UseCrudKit()` calls `MapEndpoints` on all discovered modules.

### Multiple DbContexts (`CrudKitContextRegistry`)

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

`EfRepo<Order>` resolves `OrderDbContext`; `EfRepo<Product>` resolves `InventoryDbContext`. No extra configuration needed.

### IModule with Own DbContext

Each module can register its own DbContext inside `RegisterServices` — the recommended pattern for true modular monolith:

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
        app.MapCrudEndpoints<Category, CreateCategory, UpdateCategory>();
    }
}

// Program.cs — modules discovered automatically
builder.Services.AddCrudKit<SharedDbContext>(opts =>
{
    opts.ScanModulesFromAssembly = typeof(Program).Assembly;
});
```

Each module is self-contained: own DbContext, own connection string, own entity registrations. `CrudKitContextRegistry` tracks all contexts and `EfRepo<T>` resolves the correct one per entity.

---

## Source Generation

Add the `CrudKit.SourceGen` package. The Roslyn source generator scans for all `[CrudEntity]`-decorated classes at compile time and generates:

| Generated File | Description |
|----------------|-------------|
| `{Entity}CreateDto.g.cs` | Create DTO record |
| `{Entity}UpdateDto.g.cs` | Update DTO with `Optional<T>` fields |
| `{Entity}ResponseDto.g.cs` | Response DTO |
| `{Entity}Mapper.g.cs` | `ICrudMapper` implementation |
| `{Entity}Hooks.g.cs` | Partial hook stub to extend |
| `CrudKitEndpoints.g.cs` | `MapAllCrudEndpoints()` extension method |
| `CrudKitMappers.g.cs` | DI registration for all mappers (`AddAllCrudMappers()`) |

### Usage

```csharp
// Maps all entities in one call
app.MapAllCrudEndpoints();

// Registers all generated mappers
builder.Services.AddAllCrudMappers();
```

### Extending Generated Hook Stubs

```csharp
// Extend the generated partial class — only override what you need
public partial class ProductHooks
{
    public override Task BeforeCreate(Product entity, AppContext ctx)
    {
        entity.Sku = entity.Sku.ToUpperInvariant();
        return Task.CompletedTask;
    }
}
```

### Naming Templates (`[assembly: CrudKit(...)]`)

Override the naming convention for all generated types at the assembly level. Add to any `.cs` file (typically `GlobalUsings.cs` or `AssemblyInfo.cs`):

```csharp
[assembly: CrudKit(
    CreateDtoNamingTemplate   = "{Name}CreateRequest",   // default: "Create{Name}"
    UpdateDtoNamingTemplate   = "{Name}UpdateRequest",   // default: "Update{Name}"
    ResponseDtoNamingTemplate = "{Name}Dto",             // default: "{Name}Response"
    MapperNamingTemplate      = "{Name}Mapper",          // default: "{Name}Mapper"
    HooksNamingTemplate       = "{Name}Hooks")]          // default: "{Name}Hooks"
```

The `{Name}` placeholder is required in every template. An empty template or a template missing `{Name}` is a compile-time error.

### Manual DTOs — Suppressing SourceGen

Annotate a manually written DTO with `[CreateDtoFor(typeof(TEntity))]` or `[UpdateDtoFor(typeof(TEntity))]`. SourceGen detects these attributes and skips generating the corresponding DTO for that entity; `ResponseDto` and mapper are still generated.

```csharp
[CreateDtoFor(typeof(Order))]
public record CreateOrder([Required] string CustomerName, decimal Total = 0);

[UpdateDtoFor(typeof(Order))]
public record UpdateOrder
{
    public Optional<string?> CustomerName { get; init; }
}
```

---

## Configuration Reference

### `CrudKitApiOptions` Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultPageSize` | `int` | `20` | Default page size for List endpoints |
| `MaxPageSize` | `int` | `100` | Maximum allowed page size |
| `ApiPrefix` | `string` | `"/api"` | Route prefix for all generated endpoints |
| `BulkLimit` | `int` | `10000` | Maximum records per bulk operation |
| `EnableIdempotency` | `bool` | `false` | Enable idempotency key support via request header |
| `ScanModulesFromAssembly` | `Assembly?` | `null` | Assembly to scan for `IModule` implementations |
| `AuditTrailEnabled` | `bool` | `false` | Set via `UseAuditTrail()` |
| `ExportEnabled` | `bool` | `false` | Set via `UseExport()` |
| `MaxExportRows` | `int` | `50000` | Maximum rows per CSV export request |
| `ImportEnabled` | `bool` | `false` | Set via `UseImport()` |
| `MaxImportFileSize` | `int` | `10485760` | Maximum CSV upload size in bytes (10 MB) |
| `EnumAsStringEnabled` | `bool` | `false` | Set via `UseEnumAsString()` |

### `CrudKitApiOptions` Fluent Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `UseAuditTrail()` | `AuditTrailOptions` | Enable audit trail |
| `UseAuditTrail<T>()` | `AuditTrailOptions` | Enable with custom `IAuditWriter` |
| `UseExport()` | `CrudKitApiOptions` | Enable CSV export globally |
| `UseImport()` | `CrudKitApiOptions` | Enable CSV import globally |
| `UseEnumAsString()` | `CrudKitApiOptions` | Store enums as strings in DB |
| `UseMultiTenancy()` | `MultiTenancyOptions` | Enable multi-tenancy |
| `UseGlobalHook<T>()` | `CrudKitApiOptions` | Register a global `IGlobalCrudHook` |

### `AuditTrailOptions` Methods

| Method | Description |
|--------|-------------|
| `EnableAuditFailedOperations()` | Also log failed SaveChanges with `Failed` prefix on action |

### `MultiTenancyOptions` Methods

| Method | Description |
|--------|-------------|
| `ResolveTenantFromHeader(name)` | Read tenant ID from HTTP header |
| `ResolveTenantFromClaim(type)` | Read tenant ID from JWT claim |
| `ResolveTenantFromSubdomain()` | Extract subdomain from host (e.g. `acme.app.com` → `"acme"`) |
| `ResolveTenantFromRoute(param)` | Read from route parameter |
| `ResolveTenantFromQuery(param)` | Read from query string parameter |
| `RejectUnresolvedTenant()` | Return `400` when no tenant can be resolved |
| `CrossTenantPolicy(configure)` | Configure which roles can access multiple tenants |

---

## Error Handling

`AppErrorFilter` catches all exceptions and returns structured JSON.

```json
{
  "status": 404,
  "code": "NOT_FOUND",
  "message": "Product with ID 'abc' was not found."
}
```

| Exception | Status | Code |
|-----------|--------|------|
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

## Database Dialect

CrudKit auto-detects the database provider from the EF Core provider name at startup and adapts SQL generation (especially `LIKE` / `ILIKE` queries) accordingly.

| Provider | Dialect | LIKE behavior |
|----------|---------|---------------|
| SQLite | `SqliteDialect` | `LIKE` (case-insensitive by default) |
| PostgreSQL | `PostgresDialect` | `ILIKE` (case-insensitive) |
| SQL Server | `SqlServerDialect` | `EF.Functions.Like` |
| Other | `GenericDialect` | `LIKE` fallback |

No configuration is required. Register the EF Core provider normally and CrudKit handles the rest.

---

## TimeProvider

`CrudKitDbContext` accepts an optional `TimeProvider` for testable timestamps. All `CreatedAt`, `UpdatedAt`, `DeletedAt`, and audit log `Timestamp` values use it.

```csharp
// Production — no config needed, defaults to TimeProvider.System
builder.Services.AddDbContext<AppDbContext>(opts => opts.UseSqlite("Data Source=app.db"));

// Testing — inject a fake time provider
var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
var db = new AppDbContext(options, currentUser, fakeTime);

// Advance time between operations
fakeTime.Advance(TimeSpan.FromHours(5));
```

Constructor signature: `CrudKitDbContext(DbContextOptions, ICurrentUser, TimeProvider? timeProvider = null)`

A single timestamp is captured per `SaveChanges` call — `CreatedAt` and `UpdatedAt` on the same entity are always identical within one call.

---

## Testing

CrudKit provides two built-in `ICurrentUser` implementations for tests.

```csharp
// Authenticated admin with configurable roles and permissions
var user = new FakeCurrentUser
{
    Id = "user-123",
    Username = "testadmin",
    Roles = new List<string> { "admin" },
    AccessibleTenants = null  // null = all tenants (superadmin)
};

// Unauthenticated user (default fallback registered by AddCrudKit)
var anon = new AnonymousCurrentUser();
// IsAuthenticated = false, no roles, no permissions
```

`AddCrudKit()` automatically registers `AnonymousCurrentUser` as the `ICurrentUser` fallback if no other implementation is found in DI.

### TestWebApp Pattern

```csharp
var factory = new WebApplicationFactory<Program>()
    .WithWebHostBuilder(builder =>
    {
        builder.ConfigureServices(services =>
        {
            // Replace the real ICurrentUser with a test double
            services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser
            {
                Id = "test-user",
                Roles = ["admin"]
            });
        });
    });

var client = factory.CreateClient();
```

---

## ASP.NET Identity Integration

`CrudKit.Identity` provides `CrudKitIdentityDbContext` — a drop-in replacement for `IdentityDbContext` that includes all CrudKit behaviors (soft delete, audit trail, multi-tenancy, user tracking, etc.).

### Setup

```bash
dotnet add package CrudKit.Identity
```

```csharp
public class AppUser : IdentityUser { }

public class AppDbContext : CrudKitIdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<Product> Products => Set<Product>();
}
```

Register with Identity:

```csharp
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite("Data Source=app.db"));

builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.UseAuditTrail();
    opts.UseMultiTenancy().ResolveTenantFromClaim("tenant_id");
});

builder.Services
    .AddIdentity<AppUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();
```

### Class Overloads

Three class variants with increasing Identity customization. All share the same constructor signature.

| Class | Generic Params | Use Case |
|-------|---------------|----------|
| `CrudKitIdentityDbContext<TUser>` | 1 | Default `string` keys, `IdentityRole` — most common |
| `CrudKitIdentityDbContext<TUser, TRole, TKey>` | 3 | Custom key type (`int`, `Guid`) or custom role |
| `CrudKitIdentityDbContext<TUser, TRole, TKey, ...>` | 8 | Fully custom Identity entity types |

Inheritance chain: `1-param → 3-param → 8-param → IdentityDbContext`. All CrudKit logic lives in the 8-param base. The 1-param and 3-param are convenience shortcuts.

### Constructor Parameters

All overloads share the same constructor. Optional parameters are resolved via DI:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `options` | `DbContextOptions` | Yes | EF Core options |
| `currentUser` | `ICurrentUser` | Yes | Current authenticated user |
| `timeProvider` | `TimeProvider?` | No | Testable timestamps (default: `TimeProvider.System`) |
| `efOptions` | `CrudKitEfOptions?` | No | Audit trail, enum-as-string flags |
| `tenantContext` | `ITenantContext?` | No | Current tenant for multi-tenancy |
| `auditWriter` | `IAuditWriter?` | No | Custom audit writer |

---

## Migrations

CrudKit uses standard EF Core migrations. `CrudKitDbContext` defines internal tables (`__crud_audit_logs`) in `OnModelCreating` — they are included automatically when you run migrations.

```bash
# Initial migration includes CrudKit internal tables + your entities
dotnet ef migrations add InitialCreate -c AppDbContext

# Apply to database
dotnet ef database update -c AppDbContext

# After adding new entities or upgrading CrudKit
dotnet ef migrations add AddInvoiceEntity -c AppDbContext
```

CrudKit never calls `EnsureCreated` or `Migrate` automatically in production. In the sample project, `EnsureCreated` is used only for development convenience.
