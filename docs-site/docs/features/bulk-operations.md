---
sidebar_position: 11
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

The global bulk limit is `CrudKitApiOptions.BulkLimit` (default: 1,000). Override per entity with `[CrudEntity(BulkLimit = N)]`.

```csharp
builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.BulkLimit = 5_000;  // global default
});

// Entity-level override
[CrudEntity(EnableBulkDelete = true, BulkLimit = 500)]
public class Product : AuditableEntity { }
```

## Hook-Aware

Bulk operations load entities into the EF Core change tracker and call `SaveChangesAsync`. This means all lifecycle hooks run:

- **ProcessBeforeSave** — timestamps (`UpdatedAt`), soft-delete interception, cascade soft-delete, audit trail entries
- **Domain events** — dispatched after `SaveChanges`
- **EF interceptors** — any registered `SaveChangesInterceptor` will fire

:::caution Performance Note
Because entities are loaded into memory, bulk operations consume more memory than raw SQL. For very large datasets, narrow your filters. The bulk limit (default: 1,000) prevents accidental full-table operations.
:::

## Request/Response Examples

### Bulk Count

```http
POST /api/products/bulk-count
Content-Type: application/json

{ "filters": "price=gte:100" }
```
```json
{ "count": 42 }
```

### Bulk Delete

```http
POST /api/products/bulk-delete
Content-Type: application/json

{ "ids": ["id-1", "id-2", "id-3"] }
```
```json
{ "deleted": 3 }
```

### Bulk Update

```http
POST /api/products/bulk-update
Content-Type: application/json

{
  "ids": ["id-1", "id-2"],
  "patch": { "price": 49.90, "isActive": false }
}
```
```json
{ "updated": 2 }
```

:::caution Protected Fields
System fields (`TenantId`, `DeletedAt`, `Status`, `RowVersion`) cannot be set via bulk update. Attempting to do so returns `400 Bad Request`.
:::

## Concurrency Warning

Bulk updates bypass optimistic concurrency. CrudKit logs a warning at startup if `IConcurrent` and `EnableBulkUpdate` are both configured on the same entity.

If concurrency is critical for an entity, consider disabling `EnableBulkUpdate` or removing `IConcurrent`.
