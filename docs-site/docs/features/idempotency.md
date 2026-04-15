---
sidebar_position: 13
title: Idempotency
---

# Idempotency

CrudKit supports idempotency keys to prevent duplicate processing of retried requests (e.g. from network failures or client retries).

## Enabling Idempotency

```csharp
builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.EnableIdempotency = true;
});
```

## How It Works

When idempotency is enabled, clients send an idempotency key in the request header. CrudKit stores the response for that key. If the same key is sent again, the cached response is returned without re-executing the operation.

```http
POST /api/orders
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
Content-Type: application/json

{ "customerName": "Alice", "total": 99.99 }
```

Subsequent requests with the same key return the original response, regardless of the request body.

## Example: Retry-Safe Order Creation

```http
POST /api/orders
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
Content-Type: application/json

{ "customerName": "Alice", "total": 99.99 }
```

**First request** — order is created, response cached:
```json
// 201 Created
{ "id": "abc-123", "customerName": "Alice", "total": 99.99 }
```

**Retry with same key** — cached response returned, no duplicate:
```json
// 200 OK (from cache)
{ "id": "abc-123", "customerName": "Alice", "total": 99.99 }
```

## Client-Side Usage

Generate a unique key per **intended** operation, not per HTTP attempt:

```csharp
// C# HttpClient example
var idempotencyKey = Guid.NewGuid().ToString();

async Task<HttpResponseMessage> CreateOrderWithRetry(Order order, int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Add("Idempotency-Key", idempotencyKey); // same key on retry
        request.Content = JsonContent.Create(order);

        var response = await httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode) return response;
        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500) return response; // don't retry 4xx
    }
    throw new Exception("Max retries exceeded");
}
```

## Storage

Idempotency records are stored in `__crud_idempotency` table with a TTL. Expired records are cleaned up automatically. In multi-tenant mode, keys are scoped per tenant.

## Notes

- Idempotency is applied to write operations (POST, PUT, DELETE).
- Idempotency keys are stored per endpoint — the same key can be used for different endpoints.
- The key must be unique per intended operation. Using a GUID per request is the recommended approach.
- Keys are tenant-scoped for `IMultiTenant` entities.
