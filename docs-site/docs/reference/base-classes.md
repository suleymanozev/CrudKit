---
sidebar_position: 3
title: Base Classes
---

# Base Classes

## Entity Base Class Hierarchy

All entities must derive from one of CrudKit's base classes.

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

## Automatic Field Management

`CreatedById`, `UpdatedById`, and `DeletedById` are set automatically from `ICurrentUser.Id` in `SaveChanges`:

- `CreatedById` — set on Create, **never overwritten** on Update
- `UpdatedById` — updated on every Update
- `DeletedById` — set on soft delete via `ISoftDeletable`

Key type conversion from `string` → `Guid`/`int`/`long` is handled automatically.

## CrudKitDbContext

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

Constructor signature: `CrudKitDbContext(DbContextOptions, ICurrentUser, TimeProvider? timeProvider = null)`

`CrudKitDbContext` automatically:
- Applies global query filters for `ISoftDeletable` and `IMultiTenant` entities
- Sets audit fields in `SaveChanges`
- Configures `IConcurrent.RowVersion` as a concurrency token
- Defines internal tables: `__crud_audit_logs`, `__crud_sequences`
- Uses `TimeProvider` for all timestamps (defaults to `TimeProvider.System`)
