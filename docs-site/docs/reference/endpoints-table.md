---
sidebar_position: 5
title: Endpoints Table
---

# Endpoints Table

## Standard CRUD Endpoints

For an entity named `Product` (route: `/api/products`):

| Method | Route | Description | Condition |
|--------|-------|-------------|-----------|
| GET | `/api/products` | List (paginated, filtered, sorted) | Always |
| GET | `/api/products/{id}` | Get by ID | Always |
| POST | `/api/products` | Create | `EnableCreate = true` (default) |
| PUT | `/api/products/{id}` | Update (partial via `Optional<T>`) | `EnableUpdate = true` (default) |
| DELETE | `/api/products/{id}` | Delete (soft-delete if `ISoftDeletable`) | `EnableDelete = true` (default) |
| POST | `/api/products/{id}/restore` | Restore soft-deleted record | `ISoftDeletable` only |
| POST | `/api/products/{id}/transition/{action}` | State transition | `IStateMachine<TState>` only |

## Optional Endpoints

| Method | Route | Description | Condition |
|--------|-------|-------------|-----------|
| GET | `/api/products/export` | CSV export | `[Exportable]` or `UseExport()` |
| POST | `/api/products/import` | CSV import | `[Importable]` or `UseImport()` |
| POST | `/api/products/bulk-count` | Count by filter | `EnableBulkDelete` or `EnableBulkUpdate` |
| POST | `/api/products/bulk-delete` | Delete multiple by IDs | `EnableBulkDelete = true` |
| POST | `/api/products/bulk-update` | Update multiple by IDs | `EnableBulkUpdate = true` |

## Master-Child Endpoints

For `.WithChild<OrderLine, CreateOrderLine>("lines", "OrderId")`:

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/orders/{masterId}/lines` | List lines for an order |
| GET | `/api/orders/{masterId}/lines/{id}` | Get a specific line |
| POST | `/api/orders/{masterId}/lines` | Add a line to an order |
| DELETE | `/api/orders/{masterId}/lines/{id}` | Remove a line |
| PUT | `/api/orders/{masterId}/lines/batch` | Replace all lines in a transaction |

## Response Format

### List Response

```json
{
  "total": 143,
  "page": 1,
  "per_page": 25,
  "data": [
    { "id": "...", "name": "Phone", "price": 499.99 }
  ]
}
```

### Error Response

```json
{
  "status": 404,
  "code": "NOT_FOUND",
  "message": "Product with ID 'abc' was not found."
}
```

### Validation Error Response

```json
{
  "status": 400,
  "code": "VALIDATION_ERROR",
  "errors": [
    { "field": "Name", "message": "The Name field is required." }
  ]
}
```

## Query Parameters

| Parameter | Description | Example |
|-----------|-------------|---------|
| `page` | Page number (1-based) | `?page=2` |
| `per_page` | Items per page | `?per_page=50` |
| `sort` | Sort field, prefix `-` for descending | `?sort=-created_at` |
| `search` | Full-text search across `[Searchable]` fields | `?search=phone` |
| `include` | Navigation properties to include | `?include=lines` |
| `{field}={op}:{value}` | Filter by field | `?price=gte:100` |
