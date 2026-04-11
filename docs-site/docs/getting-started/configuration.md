---
sidebar_position: 3
title: Configuration
---

# Configuration

All CrudKit options are configured via `AddCrudKit<TContext>(opts => ...)`.

## Full Example

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
    opts.UseImport();                    // All entities importable (opt-out with [NotImportable])

    // Enum storage
    opts.UseEnumAsString();              // Store enums as strings in DB

    // Multi-tenancy
    opts.UseMultiTenancy()
        .ResolveTenantFromHeader("X-Tenant-Id")
        .RejectUnresolvedTenant()
        .CrossTenantPolicy(p => p.Allow("superadmin"));

    // Domain events
    opts.UseDomainEvents();
    // or with assembly scanning:
    opts.UseDomainEvents(cfg => cfg.ScanHandlersFromAssembly(typeof(Program).Assembly));
    // or with custom dispatcher:
    opts.UseDomainEvents<MyCustomDispatcher>();

    // Global hooks
    opts.UseGlobalHook<SearchIndexHook>();

    // Module discovery
    opts.ScanModulesFromAssembly = typeof(Program).Assembly;
});
```

## CrudKitApiOptions Properties

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
| `ImportEnabled` | `bool` | `false` | Set via `UseImport()` |
| `EnumAsStringEnabled` | `bool` | `false` | Set via `UseEnumAsString()` |

## Fluent Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `UseAuditTrail()` | `AuditTrailOptions` | Enable audit trail, opt entities in with `[Audited]` |
| `UseAuditTrail<T>()` | `AuditTrailOptions` | Same, with custom `IAuditWriter` implementation |
| `UseExport()` | `CrudKitApiOptions` | Enable CSV export globally |
| `UseImport()` | `CrudKitApiOptions` | Enable CSV import globally |
| `UseEnumAsString()` | `CrudKitApiOptions` | Store all enum properties as strings |
| `UseMultiTenancy()` | `MultiTenancyOptions` | Enable multi-tenancy, chain resolver method |
| `UseGlobalHook<T>()` | `CrudKitApiOptions` | Register a global `IGlobalCrudHook` |
| `UseDomainEvents()` | `CrudKitApiOptions` | Enable domain event dispatching after `SaveChanges` |
| `UseDomainEvents(Action<DomainEventOptions>)` | `CrudKitApiOptions` | Same, with handler assembly scanning config |
| `UseDomainEvents<TDispatcher>()` | `CrudKitApiOptions` | Same, with custom `IDomainEventDispatcher` |

## AuditTrailOptions

| Method | Description |
|--------|-------------|
| `EnableAuditFailedOperations()` | Also log failed SaveChanges with `Failed` prefix on action |

## MultiTenancyOptions

| Method | Description |
|--------|-------------|
| `ResolveTenantFromHeader(name)` | Read tenant ID from HTTP header |
| `ResolveTenantFromClaim(type)` | Read tenant ID from JWT claim |
| `ResolveTenantFromSubdomain()` | Extract subdomain from host (e.g. `acme.app.com` → `"acme"`) |
| `ResolveTenantFromRoute(param)` | Read from route parameter |
| `ResolveTenantFromQuery(param)` | Read from query string parameter |
| `RejectUnresolvedTenant()` | Return `400` when no tenant can be resolved |
| `CrossTenantPolicy(configure)` | Configure which roles can access multiple tenants |

## CrudKitDbContextDependencies

Instead of injecting multiple services into your DbContext constructor, use `CrudKitDbContextDependencies`:

```csharp
public class AppDbContext : CrudKitDbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        CrudKitDbContextDependencies dependencies)
        : base(options, dependencies) { }
}
```

This is especially useful in [modular monolith](../advanced/modular-monolith) setups where multiple DbContexts share the same dependency set.

## [CrudEntity] Attribute

`[CrudEntity]` is **required** on all entities used with `MapCrudEndpoints`. The `Resource` property (formerly `Table`) controls the URL route segment:

```csharp
[CrudEntity]
public class Product : FullAuditableEntity { }
// → /api/products  (auto-derived: "Product" → "products")
```

The entity name is kebab-cased and pluralized automatically. Specify `Resource` only to override the default:

```csharp
[CrudEntity(Resource = "admin-settings")]
public class AdminSetting : AuditableEntity { }
// → /api/admin-settings  (overrides default "admin-settings")
```

## Startup Validation

`CrudKitStartupValidator` runs as an `IHostedService` at startup and validates entity metadata before the first request:

- `[CrudEntity(OwnerField = "X")]` — verifies property `X` exists on the entity. Throws if missing.
- `IConcurrent` + `EnableBulkUpdate` — logs a warning (bulk updates bypass optimistic concurrency).
