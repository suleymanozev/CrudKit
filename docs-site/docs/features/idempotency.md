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

## Notes

- Idempotency is applied to write operations (POST, PUT, DELETE).
- Idempotency keys are stored per endpoint — the same key can be used for different endpoints.
- The key must be unique per intended operation. Using a GUID per request is the recommended approach.
