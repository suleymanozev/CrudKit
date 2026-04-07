---
sidebar_position: 1
title: Soft Delete
---

# Soft Delete

Use `FullAuditableEntity` (or implement `ISoftDeletable` directly) for soft-delete behavior.

The `DELETE` endpoint sets `DeletedAt` instead of removing the row. Soft-deleted records are excluded from all queries automatically via a global EF Core query filter.

```csharp
[CrudEntity(Table = "categories")]
public class Category : FullAuditableEntity
{
    public string Name { get; set; } = string.Empty;
}
```

The restore endpoint is mapped automatically: `POST /api/categories/{id}/restore`.

## Cascade Soft Delete

When the parent is soft-deleted, all matching child records are soft-deleted in the same operation using a raw SQL `UPDATE` (no N+1 queries). Restore also cascades — restoring the parent restores all its children.

```csharp
[CrudEntity(Table = "orders")]
[CascadeSoftDelete(typeof(OrderLine), nameof(OrderLine.OrderId))]
public class Order : FullAuditableEntity { }
```

## Restore with Unique Constraint Check

When restoring a soft-deleted entity, CrudKit checks all `[Unique]` properties against currently active records. If a conflict exists, the restore fails with `409 Conflict`.

## ISoftDeletable Interface

```csharp
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
}
```

You can implement `ISoftDeletable` directly on any entity without using `FullAuditableEntity`, though the base class is the recommended approach.

## Purge Endpoint

`DELETE /api/{entity}/purge?olderThan=N` permanently hard-deletes all soft-deleted records for an `ISoftDeletable` entity that were deleted more than N days ago.

```http
DELETE /api/products/purge?olderThan=30
→ 200 OK
{ "purged": 15 }
```

- `olderThan` is required (minimum 1 day). Missing or invalid values return `400`.
- Uses `ExecuteDeleteAsync` — bypasses EF change tracking and soft-delete interception (real hard delete, no hooks fired).
- Respects tenant isolation: only records in the current tenant are purged for `IMultiTenant` entities.

## Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| DELETE | `/api/{entity}/{id}` | Sets `DeletedAt`, record remains in DB |
| POST | `/api/{entity}/{id}/restore` | Clears `DeletedAt`, record visible again |
| DELETE | `/api/{entity}/purge?olderThan=N` | Permanently deletes soft-deleted records older than N days |
