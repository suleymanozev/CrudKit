---
sidebar_position: 3
title: Custom Endpoints
---

# Custom Endpoints

Add custom endpoints under the same route group as an entity using `.WithCustomEndpoints()` or define master-child relationships with `.WithChild()`.

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

## WithChild — Master-Child Endpoints

Chain nested endpoints under a parent resource:

```csharp
app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>()
    .WithChild<OrderLine, CreateOrderLine>("lines", "OrderId");
```

| Method | Route |
|--------|-------|
| GET | `/api/orders/{masterId}/lines` |
| GET | `/api/orders/{masterId}/lines/{id}` |
| POST | `/api/orders/{masterId}/lines` |
| DELETE | `/api/orders/{masterId}/lines/{id}` |
| PUT | `/api/orders/{masterId}/lines/batch` |

The `batch` endpoint replaces all child records for a master in a single transaction.

## Auto-Discovered Custom Endpoints

Implement `IEndpointConfigurer<T>` to add custom endpoints without manual registration. CrudKit discovers these automatically by scanning the entity's assembly.

```csharp
public class InvoiceEndpointConfigurer : IEndpointConfigurer<Invoice>
{
    public void Configure(CrudEndpointGroup<Invoice> group)
    {
        group.WithCustomEndpoints(g =>
        {
            g.MapPost("/from-quote/{quoteId}", async (Guid quoteId) =>
            {
                // Convert quote to invoice
            });
            
            g.MapGet("/summary", async () =>
            {
                // Return invoice summary
            });
        });
    }
}
```

No DI registration needed — just implement the interface. Works with both manual `MapCrudEndpoints` and auto-registration via `UseCrudKit()`.

## Endpoint Mapping Overloads

```csharp
// Full CRUD — route derived from entity name
app.MapCrudEndpoints<TEntity, TCreate, TUpdate>();

// ReadOnly — List + Get only
app.MapCrudEndpoints<TEntity>();

// With explicit route prefix
app.MapCrudEndpoints<TEntity, TCreate, TUpdate>("products");
app.MapCrudEndpoints<TEntity>("units");
```
