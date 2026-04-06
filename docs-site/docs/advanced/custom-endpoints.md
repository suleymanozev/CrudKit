---
sidebar_position: 3
title: Custom Endpoints
---

# Custom Endpoints

Add custom endpoints under the same route group as an entity using `.WithCustomEndpoints()` or define master-detail relationships with `.WithDetail()`.

## WithCustomEndpoints

```csharp
app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>()
    .WithCustomEndpoints(group =>
    {
        group.MapPost("/{id}/approve", OrderEndpoints.Approve)
             .AddEndpointFilter(new RequireRoleFilter("manager"));
    });
```

Custom endpoints inherit the route prefix from the entity (e.g. `/api/orders/{id}/approve`).

## WithDetail — Master-Detail Endpoints

Chain nested endpoints under a parent resource:

```csharp
app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>()
    .WithDetail<OrderLine, CreateOrderLine>("lines", "OrderId");
```

| Method | Route |
|--------|-------|
| GET | `/api/orders/{masterId}/lines` |
| GET | `/api/orders/{masterId}/lines/{id}` |
| POST | `/api/orders/{masterId}/lines` |
| DELETE | `/api/orders/{masterId}/lines/{id}` |
| PUT | `/api/orders/{masterId}/lines/batch` |

The `batch` endpoint replaces all detail records for a master in a single transaction.

## Endpoint Mapping Overloads

```csharp
// Full CRUD — route derived from [CrudEntity(Table = ...)]
app.MapCrudEndpoints<TEntity, TCreate, TUpdate>();

// ReadOnly — List + Get only
app.MapCrudEndpoints<TEntity>();

// With explicit route prefix
app.MapCrudEndpoints<TEntity, TCreate, TUpdate>("products");
app.MapCrudEndpoints<TEntity>("units");
```
