---
sidebar_position: 14
title: Child Entities
---

# Child Entities

CrudKit supports master-child (parent-child) relationships with nested REST endpoints. You can declare the relationship declaratively with `[ChildOf]` or fluently with `.WithChild()`.

## Declarative: [ChildOf]

Annotate the child entity with `[ChildOf(typeof(TParent))]`. CrudKit generates nested endpoints automatically when the parent is registered — no fluent call needed.

```csharp
[ChildOf(typeof(Order))]
public class OrderLine : AuditableEntity
{
    public Guid OrderId { get; set; }           // FK: convention {ParentType}Id
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
```

Assuming `Order` has `[CrudEntity(Table = "orders")]`, the following endpoints are generated:

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/orders/{id}/order-lines` | List child records for a parent |
| GET | `/api/orders/{id}/order-lines/{lineId}` | Get a single child record |
| DELETE | `/api/orders/{id}/order-lines/{lineId}` | Delete a child record |
| POST | `/api/orders/{id}/order-lines` | Create a child record (requires `[CreateDtoFor]` on child) |

### Custom Route and FK

```csharp
[ChildOf(typeof(Order), Route = "items", ForeignKey = "ParentOrderId")]
public class OrderItem : AuditableEntity
{
    public Guid ParentOrderId { get; set; }
    public string Sku { get; set; } = string.Empty;
}
// → GET /api/orders/{id}/items, DELETE /api/orders/{id}/items/{itemId}, etc.
```

### [ChildOf] Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ParentType` | `Type` | — | The parent entity type (first constructor argument) |
| `Route` | `string` | pluralized child name | URL segment appended to the parent route |
| `ForeignKey` | `string` | `"{ParentType}Id"` | Name of the FK property on the child entity |

## Fluent: .WithChild()

For explicit control, or when you need the `batch` replace endpoint, use the fluent API:

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

The `batch` endpoint replaces all child records for a given master ID in a single transaction.

## Combining with [CreateDtoFor]

Child endpoints that create records require a create DTO. Use `[CreateDtoFor]` on a manual DTO or let SourceGen generate one:

```csharp
[CreateDtoFor(typeof(OrderLine))]
public record CreateOrderLine(string ProductName, int Quantity);
```

When `[CreateDtoFor]` is present for the child type, `[ChildOf]` automatically maps the `POST` endpoint.
