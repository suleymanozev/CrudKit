---
sidebar_position: 5
title: Error Handling
---

# Error Handling

`AppErrorFilter` catches all exceptions and returns structured JSON responses.

## Error Response Format

```json
{
  "status": 404,
  "code": "NOT_FOUND",
  "message": "Product with ID 'abc' was not found."
}
```

## Error Code Reference

| Exception | Status | Code |
|-----------|--------|------|
| `AppError.NotFound()` | 404 | `NOT_FOUND` |
| `AppError.BadRequest()` | 400 | `BAD_REQUEST` |
| `AppError.Validation()` | 400 | `VALIDATION_ERROR` |
| `AppError.Unauthorized()` | 401 | `UNAUTHORIZED` |
| `AppError.Forbidden()` | 403 | `FORBIDDEN` |
| `AppError.Conflict()` | 409 | `CONFLICT` |
| `DbUpdateConcurrencyException` | 409 | `CONFLICT` |
| Unhandled exception | 500 | `INTERNAL_ERROR` |

## Validation Error Format

Validation errors include a per-field breakdown:

```json
{
  "status": 400,
  "code": "VALIDATION_ERROR",
  "errors": [
    { "field": "Name", "message": "The Name field is required." },
    { "field": "Price", "message": "Price must be greater than 0." }
  ]
}
```

## Development vs Production

In Development, unhandled exceptions include the full stack trace in the response body. In Production, only a generic message is returned. This is controlled by the standard ASP.NET Core environment check.

## Throwing Errors from Hooks

Use `AppError` factory methods inside hooks or custom endpoints:

```csharp
public class OrderHooks : ICrudHooks<Order>
{
    public Task BeforeCreate(Order entity, AppContext ctx)
    {
        if (entity.Total <= 0)
            throw AppError.BadRequest("Total must be positive.");

        return Task.CompletedTask;
    }

    public Task BeforeDelete(Order entity, AppContext ctx)
    {
        if (entity.Status == OrderStatus.Completed)
            throw AppError.Conflict("Cannot delete a completed order.");

        return Task.CompletedTask;
    }
}
```
