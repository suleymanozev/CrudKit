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
