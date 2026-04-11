---
sidebar_position: 2
title: Entity Hierarchy
---

# Entity Hierarchy

All entities must derive from one of CrudKit's base classes. Pick the level that matches the data you need to track.

## Base Class Table

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

### Aggregate Root Hierarchy

Aggregate roots extend the entity hierarchy with domain event support (`IHasDomainEvents`). Use these when your entity needs to raise [domain events](../features/domain-events).

| Class | Key | Provides |
|-------|-----|----------|
| `AggregateRoot` | `Guid` | `Entity` + domain events |
| `AggregateRoot<TKey>` | Custom | same + custom key |
| `AuditableAggregateRoot` | `Guid` | `AuditableEntity` + domain events |
| `AuditableAggregateRoot<TKey>` | Custom | same + custom key |
| `AuditableAggregateRootWithUser<TUser>` | `Guid` | `AuditableEntityWithUser` + domain events |
| `AuditableAggregateRootWithUser<TKey, TUser, TUserKey>` | Custom | same with explicit key types |
| `FullAuditableAggregateRoot` | `Guid` | `FullAuditableEntity` + domain events |
| `FullAuditableAggregateRoot<TKey>` | Custom | same + custom key |
| `FullAuditableAggregateRootWithUser<TUser>` | `Guid` | `FullAuditableEntityWithUser` + domain events |
| `FullAuditableAggregateRootWithUser<TKey, TUser, TUserKey>` | Custom | same with explicit key types |

:::info
`[CrudEntity]` is **required** on all entities used with `MapCrudEndpoints`. The `IRepo<T>` constraint is `IEntity` — any entity (not just `IAuditableEntity`) can participate in CRUD.
:::

`CreatedById`, `UpdatedById`, and `DeletedById` are set automatically from `ICurrentUser.Id` in `SaveChanges`. `CreatedById` is preserved on updates (never overwritten). Key type conversion from `string` → `Guid`/`int`/`long` is handled automatically.

## Examples

```csharp
// Guid key (default)
public class Currency : Entity { }
public class Product : AuditableEntity { }
public class Order : FullAuditableEntity { }
public class Invoice : FullAuditableEntityWithUser<AppUser> { }

// Aggregate roots (with domain event support)
public class Invoice : FullAuditableAggregateRoot { }
public class Payment : AuditableAggregateRoot { }

// Custom key types
public class LegacyProduct : AuditableEntity<long> { }
public class LegacyOrder : FullAuditableEntityWithUser<long, AppUser, int> { }
```

## Choosing a Base Class

**Use `Entity`** — lookup/reference tables with no timestamps needed (e.g. `Currency`, `Country`).

**Use `AuditableEntity`** — any entity where you want to know when it was created/updated but do not need soft delete.

**Use `FullAuditableEntity`** — the most common choice. Provides timestamps + soft delete. Use this unless you have a reason not to.

**Use `*WithUser` variants** — when you need to know which user created, updated, or deleted a record. Requires `ICurrentUser` in DI.

**Use `*AggregateRoot` variants** — when your entity needs to raise [domain events](../features/domain-events). Same audit/soft-delete capabilities as the corresponding `Entity` variant, plus `AddDomainEvent()`.

**Use custom key variants (`<TKey>`)** — when integrating with legacy databases that use `int` or `long` primary keys.

## Soft Delete and ISoftDeletable

`FullAuditableEntity` implements `ISoftDeletable`:

```csharp
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
    Guid? DeleteBatchId { get; set; }
}
```

`DeleteBatchId` enables [smart cascade restore](../features/soft-delete#smart-cascade-restore) — see the soft delete page for details.

`CrudKitDbContext` applies a global query filter that excludes records where `DeletedAt != null`. Soft-deleted records are invisible to all queries unless explicitly included. The `DELETE` endpoint sets `DeletedAt` instead of removing the row. A `POST /{id}/restore` endpoint is auto-mapped.

## User Tracking

For `*WithUser` variants, implement `ICurrentUser` in your application:

```csharp
public class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public string? Id => _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? Username => _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);
    public IReadOnlyList<string> Roles => /* ... */;
    public bool IsAuthenticated => _accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
    // ...
}

builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
```
