---
sidebar_position: 11
title: Optimistic Concurrency
---

# Optimistic Concurrency

Implement `IConcurrent` to enable automatic concurrency conflict detection.

## Entity Setup

```csharp
[CrudEntity(Table = "products")]
public class Product : AuditableEntity, IConcurrent
{
    public string Name { get; set; } = string.Empty;
    public uint RowVersion { get; set; }  // auto-managed by EF Core
}
```

`CrudKitDbContext` configures `RowVersion` as a concurrency token. When two requests update the same entity simultaneously, the second receives `409 Conflict`.

## IConcurrent Interface

```csharp
public interface IConcurrent
{
    uint RowVersion { get; set; }
}
```

## Conflict Response

```json
{
  "status": 409,
  "code": "CONFLICT",
  "message": "The record was modified by another request. Reload and retry."
}
```

## Notes

- `RowVersion` is auto-incremented by EF Core on every update.
- The client must include the current `RowVersion` in the update request. If the value has changed since the record was loaded, the update is rejected.
- Bulk updates bypass optimistic concurrency. CrudKit logs a warning at startup if `IConcurrent` and `EnableBulkUpdate` are both configured on the same entity.
