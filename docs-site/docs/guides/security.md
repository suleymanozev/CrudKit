---
sidebar_position: 5
title: Security
---

# Security

CrudKit includes several built-in security mechanisms that protect your API without additional configuration.

## Mass Assignment Protection

CrudKit prevents mass assignment attacks by only mapping properties that exist on the **Create** or **Update DTO**. Properties not present in the DTO are never touched — even if the client sends extra JSON fields.

When using **entity-as-DTO** (no separate DTOs), properties marked with `[SkipUpdate]` or `[Protected]` are excluded from update mapping.

```csharp
public class Product : FullAuditableEntity
{
    public string Name { get; set; } = "";

    [Protected]           // never settable via API
    public decimal Cost { get; set; }

    [SkipUpdate]          // settable on create, immutable after
    public string Sku { get; set; } = "";
}
```

## System Field Protection

System-managed fields are **never writable** through the API:

| Field | Protection |
|-------|-----------|
| `Id` | Auto-generated, ignored in create/update |
| `CreatedAt`, `UpdatedAt` | Set by `ProcessBeforeSave` |
| `DeletedAt`, `IsDeleted` | Set by soft-delete logic |
| `DeleteBatchId` | Set by cascade soft-delete |
| `TenantId` | Set from `ITenantContext` |
| `RowVersion` | Managed by EF Core |
| `CreatedById`, `UpdatedById`, `DeletedById` | Set from `ICurrentUser` |
| `DomainEvents` | Internal collection, hidden from JSON |
| `Status` (on `IStateMachine`) | Changed only via `/transition/{action}` |

### Bulk Update Protection

Bulk update operations enforce the same protection. Attempting to set system fields via `bulk-update` returns **400 Bad Request**:

```json
POST /api/products/bulk-update
{
  "ids": ["..."],
  "patch": { "tenantId": "attacker-tenant" }
}
// → 400: "Cannot update protected field: TenantId"
```

## Tenant Isolation

For `IMultiTenant` entities, CrudKit enforces tenant isolation at three levels:

1. **Middleware** — `TenantResolverMiddleware` rejects requests without a resolved tenant (when `RejectUnresolvedTenant` is enabled)
2. **Query filter** — EF Core global query filter ensures only current tenant's data is returned
3. **Repository guard** — `EfRepo` validates `TenantId` is set before save

Cross-tenant access is controlled by `CrossTenantPolicy`:

```csharp
opts.UseMultiTenancy()
    .ResolveTenantFromHeader("X-Tenant-Id")
    .RejectUnresolvedTenant()
    .CrossTenantPolicy(p => p.Allow("superadmin"));
```

## Query Parameter Limits

CrudKit limits query parameter values to prevent abuse:

- **Filter values** — maximum 200 characters per filter value
- **Page size** — clamped between `MinPageSize` (default: 1) and `MaxPageSize` (default: 100)
- **Sort fields** — only properties marked with `[Sortable]` or not marked with `[NotSortable]` are accepted
- **Filter fields** — only properties marked with `[Filterable]` or not marked with `[NotFilterable]` are accepted

```csharp
builder.Services.AddCrudKitEf<AppDbContext>();
builder.Services.AddCrudKit(opts =>
{
    opts.MinPageSize = 5;     // minimum items per page
    opts.MaxPageSize = 100;   // maximum items per page
});
```

## Response Sanitization

System fields are automatically excluded from JSON responses:

- `TenantId` — hidden via `[JsonIgnore]` applied at startup
- `DeleteBatchId` — hidden from response serialization
- `DomainEvents` — internal collection, never serialized

Use `[SkipResponse]` to hide additional properties:

```csharp
public class User : FullAuditableEntity
{
    public string Email { get; set; } = "";

    [SkipResponse]
    public string PasswordHash { get; set; } = "";

    [Hashed]              // stored as SHA-256 hash, never readable
    public string ApiKey { get; set; } = "";
}
```

## Error Response Safety

CrudKit returns structured error responses that **never leak internal details** in production:

```json
// Development
{ "error": "NullReferenceException", "detail": "Object reference...", "stackTrace": "..." }

// Production
{ "error": "An unexpected error occurred." }
```

Stack traces and exception details are only included when `ASPNETCORE_ENVIRONMENT=Development`.

## Authorization

See the [Authorization guide](../features/auth.md) for per-operation role and permission-based access control.
