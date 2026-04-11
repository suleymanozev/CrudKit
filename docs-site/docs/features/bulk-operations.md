---
sidebar_position: 9
title: Bulk Operations
---

# Bulk Operations

Enable bulk endpoints per entity via `[CrudEntity]` attributes.

## Entity Setup

```csharp
[CrudEntity(EnableBulkDelete = true, EnableBulkUpdate = true, BulkLimit = 500)]
public class Product : AuditableEntity { }
```

## Generated Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/products/bulk-count` | Count matching records by filter |
| POST | `/api/products/bulk-delete` | Delete multiple records by IDs |
| POST | `/api/products/bulk-update` | Update multiple records by IDs |

## Limits

The global bulk limit is `CrudKitApiOptions.BulkLimit` (default: 10,000). Override per entity with `[CrudEntity(BulkLimit = N)]`.

```csharp
builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.BulkLimit = 5_000;  // global default
});

// Entity-level override
[CrudEntity(EnableBulkDelete = true, BulkLimit = 500)]
public class Product : AuditableEntity { }
```

## Concurrency Warning

Bulk updates bypass optimistic concurrency. CrudKit logs a warning at startup if `IConcurrent` and `EnableBulkUpdate` are both configured on the same entity.

If concurrency is critical for an entity, consider disabling `EnableBulkUpdate` or removing `IConcurrent`.
