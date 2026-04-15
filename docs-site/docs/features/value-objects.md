---
sidebar_position: 12
title: Value Objects
---

# Value Objects

Flatten value object properties into DTOs for a clean API surface.

## Define a Value Object

```csharp
[ValueObject]
public class Money
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TRY";
}
```

## Use in Entity

```csharp
[CrudEntity]
public class Product : FullAuditableAggregateRoot
{
    [Flatten]
    public Money Price { get; set; } = new();
    // DTO: PriceAmount, PriceCurrency (flat)

    public Money Tax { get; set; } = new();
    // DTO: Money Tax (nested JSON)
}
```

## API

With `[Flatten]`:
```json
{ "priceAmount": 29.90, "priceCurrency": "USD" }
```

Without `[Flatten]` (default):
```json
{ "tax": { "amount": 5.0, "currency": "TRY" } }
```

## Full Request/Response Example

**Create:**
```http
POST /api/products
Content-Type: application/json

{
  "name": "Widget",
  "priceAmount": 29.90,
  "priceCurrency": "USD",
  "tax": { "amount": 5.0, "currency": "TRY" }
}
```

**Response:**
```json
{
  "id": "abc-123",
  "name": "Widget",
  "priceAmount": 29.90,
  "priceCurrency": "USD",
  "tax": { "amount": 5.0, "currency": "TRY" }
}
```

## Partial Update

Only send the fields you want to change:
```json
{ "priceAmount": 49.90 }
```
`priceCurrency` is preserved.

## EF Core Configuration

Value objects are configured as owned types. CrudKit handles this automatically, but if you need custom configuration:

```csharp
protected override void OnModelCreatingCustom(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Product>(b =>
    {
        b.OwnsOne(e => e.Price);
        b.OwnsOne(e => e.Tax);
    });
}
```
