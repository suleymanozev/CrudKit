---
sidebar_position: 2
title: Multi-Tenancy
---

# Multi-Tenancy

CrudKit provides built-in multi-tenant isolation with automatic query filtering, tenant injection on create, and cross-tenant access control.

## Mark Entities as Tenant-Scoped

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

`CrudKitDbContext` automatically applies `WHERE TenantId = X` as a global query filter and sets `TenantId` on create.

## Configure Tenant Resolution

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

## Cross-Tenant Protection

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

## AccessibleTenants

`ICurrentUser.AccessibleTenants` controls which tenants a user can see:

- `null` — all tenants (superadmin)
- `["acme", "globex"]` — only listed tenants
- `[]` — no cross-tenant access (tenant-scoped only)
