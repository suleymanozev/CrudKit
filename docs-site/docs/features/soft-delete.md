---
sidebar_position: 1
title: Soft Delete
---

# Soft Delete

Use `FullAuditableEntity` (or implement `ISoftDeletable` directly) for soft-delete behavior.

The `DELETE` endpoint sets `DeletedAt` instead of removing the row. Soft-deleted records are excluded from all queries automatically via a global EF Core query filter.

```csharp
[CrudEntity]
public class Category : FullAuditableEntity
{
    public string Name { get; set; } = string.Empty;
}
```

The restore endpoint is mapped automatically: `POST /api/categories/{id}/restore`.

## Cascade Soft Delete

When the parent is soft-deleted, all matching child records are soft-deleted in the same operation using a raw SQL `UPDATE` (no N+1 queries). A shared `DeleteBatchId` is assigned to the parent and all cascade-deleted children.

```csharp
[CrudEntity]
[CascadeSoftDelete(typeof(OrderLine), nameof(OrderLine.OrderId))]
public class Order : FullAuditableEntity { }
```

### Smart Cascade Restore {#smart-cascade-restore}

Restoring a parent only restores children that share the same `DeleteBatchId`. This means:

- Children deleted **individually** before the parent keep their own `DeleteBatchId` and are **not** restored when the parent is restored.
- Children cascade-deleted **with** the parent share the parent's `DeleteBatchId` and **are** restored together.

This prevents accidentally restoring records that were intentionally deleted before the parent.

## Restore with Unique Constraint Check

When restoring a soft-deleted entity, CrudKit checks all `[Unique]` properties against currently active records. If a conflict exists, the restore fails with `409 Conflict`.

## ISoftDeletable Interface

```csharp
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
    Guid? DeleteBatchId { get; set; }
}
```

`DeleteBatchId` is a `Guid` assigned on delete. When cascade soft-deleting, the parent and all cascade-deleted children receive the same `DeleteBatchId`. This enables [smart cascade restore](#smart-cascade-restore).

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

## IDataFilter — Temporarily Include Soft-Deleted Records

Inject `IDataFilter<T>` to disable the soft-delete query filter within a scoped block. The filter is automatically re-enabled when the block exits.

```csharp
public class ArchiveService
{
    private readonly IDataFilter<Product> _filter;
    public ArchiveService(IDataFilter<Product> filter) => _filter = filter;

    public async Task<List<Product>> GetAllIncludingDeletedAsync()
    {
        using (_filter.Disable<ISoftDeletable>())
            return await _repo.ListAsync();
    }
}
```

## Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| DELETE | `/api/{entity}/{id}` | Sets `DeletedAt`, record remains in DB |
| POST | `/api/{entity}/{id}/restore` | Clears `DeletedAt`, record visible again |
| DELETE | `/api/{entity}/purge?olderThan=N` | Permanently deletes soft-deleted records older than N days |
