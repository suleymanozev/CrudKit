---
sidebar_position: 3
title: Authorization
---

# Authorization

CrudKit supports entity-level and fluent authorization, covering role-based, permission-based, and per-operation restrictions.

## Entity-Level (Recommended)

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

Valid operations for `[AuthorizeOperation]`: `"Read"`, `"Create"`, `"Update"`, `"Delete"`.

## Fluent API

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

## Behavior

| Scenario | Response |
|----------|----------|
| No auth configured | Endpoints are public |
| Auth configured, no token | `401 Unauthorized` |
| Authenticated, wrong role | `403 Forbidden` |
| Entity + fluent both restrict | Both must be satisfied |

## Attribute Reference

| Attribute | Scope | Effect |
|-----------|-------|--------|
| `[RequireAuth]` | Entity | All endpoints require authenticated user |
| `[RequireRole("role")]` | Entity | All endpoints require specified role |
| `[RequirePermissions]` | Entity | Derives permission names from table (e.g. `products:read`) |
| `[AuthorizeOperation("Op", "role")]` | Entity | Restricts one operation to a specific role |

## ICurrentUser

Implement `ICurrentUser` in your application to integrate with your auth system:

```csharp
public interface ICurrentUser
{
    string? Id { get; }
    string? Username { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<Permission> Permissions { get; }
    bool IsAuthenticated { get; }
    bool HasRole(string role);
    bool HasPermission(string entity, string action);
    bool HasPermission(string entity, string action, PermScope scope);
    IReadOnlyList<string>? AccessibleTenants { get; }
}
```

`AddCrudKit()` automatically registers `AnonymousCurrentUser` as the fallback if no other `ICurrentUser` implementation is found in DI.
