---
sidebar_position: 13
title: Query Features
---

# Query Features

All List endpoints support filtering, sorting, pagination, and eager loading via query string.

## Filtering

```
GET /api/products?name=like:phone
GET /api/products?price=gte:100
GET /api/products?status=in:active,pending
GET /api/products?deletedAt=null
GET /api/products?price=gt:10&price=lt:500
```

| Operator | Meaning |
|----------|---------|
| `eq` | Equals (default) |
| `neq` | Not equals |
| `gt` | Greater than |
| `gte` | Greater than or equal |
| `lt` | Less than |
| `lte` | Less than or equal |
| `like` | Contains (case-insensitive) |
| `starts` | Starts with |
| `in` | In list (comma-separated) |
| `null` | Is null |
| `notnull` | Is not null |

## Sorting

```
GET /api/products?sort=price          # ascending
GET /api/products?sort=-created_at    # descending (prefix with -)
```

## Pagination

```
GET /api/products?page=2&per_page=25
```

Response envelope:

```json
{
  "total": 143,
  "page": 2,
  "per_page": 25,
  "data": [ ... ]
}
```

## Includes

```
GET /api/orders?include=lines
```

Loads navigation properties by name. Configure defaults with `[DefaultInclude]` on the entity class:

```csharp
[DefaultInclude(nameof(Order.Lines), IncludeScope.All)]
[DefaultInclude(nameof(Order.Customer), IncludeScope.DetailOnly)]
public class Order : FullAuditableEntity { ... }
```

`IncludeScope.All` — included on both List and Get endpoints.  
`IncludeScope.DetailOnly` — included only on the Get by ID endpoint.

For complex includes (e.g. `ThenInclude`), use `ApplyIncludes` in an `ICrudHooks<T>` implementation:

```csharp
public class OrderHooks : ICrudHooks<Order>
{
    public IQueryable<Order> ApplyIncludes(IQueryable<Order> query)
        => query.Include(o => o.Lines).ThenInclude(l => l.Product);
}
```

## Searchable Properties

Mark string properties with `[Searchable]` to include them in global full-text search queries:

```csharp
[Required, MaxLength(200), Searchable]
public string Name { get; set; } = string.Empty;
```

Full-text search is triggered via the `search` query parameter:

```
GET /api/products?search=phone
```

## Filter & Sort Control

By default, all entity fields are filterable and sortable. Use attributes to control this behavior per-field or per-entity.

### Attribute Reference

- `[Filterable]` — Force-enable filtering on a property, overriding entity-level `[NotFilterable]`
- `[NotFilterable]` — Disable filtering; filter queries for this field are silently skipped
- `[Sortable]` — Force-enable sorting on a property, overriding entity-level `[NotSortable]`
- `[NotSortable]` — Disable sorting; sort requests for this field are silently skipped

### Usage Example

```csharp
// Property-level control
public class Order : FullAuditableEntity
{
    public string CustomerName { get; set; }    // filterable + sortable (default)

    [NotFilterable]
    public string InternalNotes { get; set; }    // NOT filterable

    [NotSortable]
    public decimal Total { get; set; }           // NOT sortable
}

// Entity-level + property override
[NotFilterable]
public class SecureEntity : AuditableEntity
{
    [Filterable]                                  // override entity default
    public string PublicField { get; set; }

    public string SecretField { get; set; }       // NOT filterable (entity default)
}
```
