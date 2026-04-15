---
sidebar_position: 14
title: Optimistic Concurrency
---

# Optimistic Concurrency

Implement `IConcurrent` to enable automatic concurrency conflict detection.

## Entity Setup

```csharp
[CrudEntity]
public class Product : AuditableEntity, IConcurrent
{
    public string Name { get; set; } = string.Empty;
    public uint RowVersion { get; set; }  // auto-managed by EF Core
}
```

`CrudKitDbContext` configures `RowVersion` as a concurrency token using the active database dialect. On every `SaveChanges`, CrudKit auto-increments the `RowVersion` value. When two requests update the same entity simultaneously, the second receives `409 Conflict`.

## IConcurrent Interface

```csharp
public interface IConcurrent
{
    uint RowVersion { get; set; }
}
```

## Dialect-Based Configuration

The concurrency token strategy is handled automatically based on the configured database provider:

| Dialect | Strategy |
|---------|----------|
| SQLite | `uint` auto-incremented by CrudKit |
| PostgreSQL | `uint` auto-incremented by CrudKit |
| SQL Server | `uint` auto-incremented by CrudKit |

No additional configuration is needed — CrudKit detects the provider at startup and applies the correct strategy.

## Conflict Response

```json
{
  "status": 409,
  "code": "CONFLICT",
  "message": "The record was modified by another request. Reload and retry."
}
```

## Client-Side Usage

**1. Get the entity (note the `rowVersion`):**

```http
GET /api/products/abc-123
```
```json
{
  "id": "abc-123",
  "name": "Widget",
  "price": 29.90,
  "rowVersion": 3
}
```

**2. Update with `rowVersion`:**

```http
PUT /api/products/abc-123
Content-Type: application/json

{ "name": "Widget Pro", "rowVersion": 3 }
```

**Success** — returns updated entity with incremented `rowVersion`:
```json
{ "id": "abc-123", "name": "Widget Pro", "price": 29.90, "rowVersion": 4 }
```

**3. Concurrent update with stale `rowVersion`:**

```http
PUT /api/products/abc-123
Content-Type: application/json

{ "name": "Widget Ultra", "rowVersion": 3 }
```

```json
// 409 Conflict
{
  "status": 409,
  "code": "CONFLICT",
  "message": "The record was modified by another request. Reload and retry."
}
```

## Handling 409 in Client Code

```csharp
async Task UpdateWithRetry(Guid id, Action<Product> modify, int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        var product = await httpClient.GetFromJsonAsync<Product>($"/api/products/{id}");
        modify(product!);

        var response = await httpClient.PutAsJsonAsync($"/api/products/{id}", product);
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode != HttpStatusCode.Conflict) throw new Exception("Unexpected error");
        // Conflict — retry with fresh data
    }
    throw new Exception("Max retries exceeded — concurrent modification conflict.");
}
```

## Notes

- `RowVersion` is auto-incremented by CrudKit on every update. Do not set it manually.
- The client must include the current `RowVersion` in the update request. If the value has changed since the record was loaded, the update is rejected.
- Bulk updates bypass optimistic concurrency. CrudKit logs a warning at startup if `IConcurrent` and `EnableBulkUpdate` are both configured on the same entity.
