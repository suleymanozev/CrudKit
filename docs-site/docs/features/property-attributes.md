---
sidebar_position: 16
title: Property Attributes
---

# Property Attributes

CrudKit provides property-level attributes to control how fields behave across endpoints, serialization, and storage.

## Security & Visibility

### [Protected]

Field is never writable via API — not in Create, Update, or BulkUpdate. Use for computed or system-managed fields.

```csharp
public class Invoice : FullAuditableEntity
{
    [Protected]
    public decimal GrandTotal { get; set; }  // calculated server-side

    [Protected]
    public InvoiceStatus Status { get; set; } // changed only via /transition
}
```

### [SkipResponse]

Field is never included in API responses. Stored in DB, accepted in Create/Update, but hidden from GET.

```csharp
public class User : FullAuditableEntity
{
    public string Email { get; set; } = "";

    [SkipResponse]
    public string PasswordHash { get; set; } = "";  // never returned to client
}
```

### [SkipUpdate]

Field is settable on Create but immutable after. Update requests ignore this field.

```csharp
public class Product : FullAuditableEntity
{
    [SkipUpdate]
    public string Sku { get; set; } = "";  // set once, never changed

    public string Name { get; set; } = "";
}
```

### [Hashed]

Value is stored as a SHA-256 hash. Original value is never readable. Useful for tokens and secrets.

```csharp
public class ApiClient : AuditableEntity
{
    [Hashed, SkipResponse]
    public string ApiKey { get; set; } = "";  // stored as hash, never returned
}
```

In audit logs, hashed fields are recorded as `"***"` instead of the actual value.

## Querying

### [Searchable]

Includes the field in global full-text search via `?search=` query parameter.

```csharp
public class Product : FullAuditableEntity
{
    [Searchable]
    public string Name { get; set; } = "";

    [Searchable]
    public string Description { get; set; } = "";

    public decimal Price { get; set; }  // not searchable
}
```

```
GET /api/products?search=widget
→ searches Name and Description fields
```

### [NotFilterable] / [NotSortable]

Prevents a field from being used in filter or sort queries.

```csharp
public class Order : FullAuditableEntity
{
    public string CustomerName { get; set; } = "";

    [NotFilterable]
    public string InternalNotes { get; set; } = "";  // can't filter by this

    [NotSortable]
    public string Description { get; set; } = "";  // can't sort by this
}
```

## Data Integrity

### [Unique]

Creates a unique index. For `IMultiTenant` entities, `TenantId` is automatically included — ensuring uniqueness per tenant.

```csharp
public class Product : FullAuditableEntity, IMultiTenant
{
    [Unique]
    public string Code { get; set; } = "";  // unique per tenant
    public string TenantId { get; set; } = "";
}
```

For soft-deletable entities, the unique index is partial (excludes deleted records).

## Combining Attributes

Attributes can be stacked on the same property:

```csharp
[CrudEntity]
[Audited]
public class User : FullAuditableEntity
{
    [Required, MaxLength(100), Searchable]
    public string Name { get; set; } = "";

    [Required, Unique]
    public string Email { get; set; } = "";

    [Hashed, SkipResponse, AuditIgnore]
    public string Password { get; set; } = "";

    [SkipUpdate, Unique]
    public string Username { get; set; } = "";

    [Protected]
    public DateTime LastLoginAt { get; set; }
}
```

## Quick Reference

| Attribute | Create | Update | Response | Filter | Sort | Audit |
|-----------|--------|--------|----------|--------|------|-------|
| (none) | Yes | Yes | Yes | Yes | Yes | Yes |
| `[Protected]` | No | No | Yes | Yes | Yes | Yes |
| `[SkipResponse]` | Yes | Yes | No | Yes | Yes | Yes |
| `[SkipUpdate]` | Yes | No | Yes | Yes | Yes | Yes |
| `[Hashed]` | Yes | Yes | Hashed | No | No | Masked |
| `[Searchable]` | Yes | Yes | Yes | Yes | Yes | Yes |
| `[NotFilterable]` | Yes | Yes | Yes | No | Yes | Yes |
| `[NotSortable]` | Yes | Yes | Yes | Yes | No | Yes |
| `[AuditIgnore]` | Yes | Yes | Yes | Yes | Yes | No |
