---
sidebar_position: 4
title: Configuration Options
---

# Configuration Options

## CrudKitApiOptions

All options are set via `AddCrudKit<TContext>(opts => ...)`.

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultPageSize` | `int` | `20` | Default page size for List endpoints |
| `MinPageSize` | `int` | `1` | Minimum allowed page size (clamped via `Math.Clamp`) |
| `MaxPageSize` | `int` | `100` | Maximum allowed page size (clamped via `Math.Clamp`) |
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

### Fluent Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `UseAuditTrail()` | `AuditTrailOptions` | Enable audit trail |
| `UseAuditTrail<T>()` | `AuditTrailOptions` | Enable with custom `IAuditWriter` |
| `UseExport()` | `CrudKitApiOptions` | Enable CSV export globally |
| `UseImport()` | `CrudKitApiOptions` | Enable CSV import globally |
| `UseEnumAsString()` | `CrudKitApiOptions` | Store enums as strings in DB |
| `UseMultiTenancy()` | `MultiTenancyOptions` | Enable multi-tenancy |
| `UseGlobalHook<T>()` | `CrudKitApiOptions` | Register a global `IGlobalCrudHook` |

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

## Full Configuration Example

```csharp
builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.DefaultPageSize = 25;
    opts.MaxPageSize = 100;
    opts.ApiPrefix = "/api";
    opts.BulkLimit = 10_000;
    opts.EnableIdempotency = true;

    opts.UseAuditTrail()
        .EnableAuditFailedOperations();

    opts.UseExport();
    opts.MaxExportRows = 50_000;
    opts.UseImport();
    opts.MaxImportFileSize = 10 * 1024 * 1024;
    opts.UseEnumAsString();

    opts.UseMultiTenancy()
        .ResolveTenantFromHeader("X-Tenant-Id")
        .RejectUnresolvedTenant()
        .CrossTenantPolicy(p =>
        {
            p.Allow("superadmin");
            p.AllowReadOnly("support");
        });

    opts.UseGlobalHook<SearchIndexHook>();
    opts.UseGlobalHook<CacheInvalidationHook>();

    opts.ScanModulesFromAssembly = typeof(Program).Assembly;
});
```
