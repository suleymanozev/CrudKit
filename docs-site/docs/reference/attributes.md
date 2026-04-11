---
sidebar_position: 1
title: Attributes
---

# Attributes

## Entity-Level Attributes

### [CrudEntity]

**Required** on every entity used with `MapCrudEndpoints`. Controls route generation, endpoint availability, and document numbering.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Resource` | `string` | entity name kebab-cased + "s" | API resource name used as the URL route segment (e.g. `"products"` → `/api/products`). Formerly named `Table`. |
| `ReadOnly` | `bool` | `false` | Generate List + Get only; no write endpoints |
| `EnableCreate` | `bool` | `true` | Generate POST endpoint |
| `EnableUpdate` | `bool` | `true` | Generate PUT endpoint |
| `EnableDelete` | `bool` | `true` | Generate DELETE endpoint |
| `EnableBulkDelete` | `bool` | `false` | Generate POST `/bulk-delete` endpoint |
| `EnableBulkUpdate` | `bool` | `false` | Generate POST `/bulk-update` endpoint |
| `BulkLimit` | `int` | global | Override global `BulkLimit` for this entity |
| `OwnerField` | `string` | — | Property holding the owner user ID for row-level security |

```csharp
[CrudEntity(EnableBulkDelete = true, BulkLimit = 500)]
public class Order : FullAuditableEntity { }
```

### [Audited] / [NotAudited]

- `[Audited]` — opt this entity into the audit trail. Requires `UseAuditTrail()` globally. Changes are written to `__crud_audit_logs`.
- `[NotAudited]` — opt this entity out when `UseAuditTrail()` is enabled globally.

### [Exportable] / [NotExportable]

- `[Exportable]` — add `GET /export` endpoint for CSV download, regardless of global flag.
- `[NotExportable]` — suppress export endpoint even when `UseExport()` is globally enabled.

### [Importable] / [NotImportable]

- `[Importable]` — add `POST /import` endpoint for CSV upload, regardless of global flag.
- `[NotImportable]` — suppress import endpoint even when `UseImport()` is globally enabled.

### [RequireAuth]

All endpoints on this entity require an authenticated user. Unauthenticated requests return `401`.

```csharp
[CrudEntity]
[RequireAuth]
public class Order : FullAuditableEntity { }
```

### [RequireRole("role")]

All endpoints require membership in the specified role.

```csharp
[CrudEntity(Resource = "admin_settings")]
[RequireRole("admin")]
public class AdminSetting : AuditableEntity { }
```

### [RequirePermissions]

Auto-derives convention-based permission names from the resource name. For a `Product` entity (resource: `"products"`), requires:
`products:read`, `products:create`, `products:update`, `products:delete`.

```csharp
[CrudEntity]
[RequirePermissions]
public class Product : AuditableEntity { }
```

### [AuthorizeOperation("Operation", "role")]

Applies a role restriction to a specific operation only. Operations: `"Read"`, `"Create"`, `"Update"`, `"Delete"`.

```csharp
[CrudEntity]
[RequireAuth]
[AuthorizeOperation("Create", "manager")]
[AuthorizeOperation("Delete", "admin")]
public class Invoice : FullAuditableEntity { }
```

### [CascadeSoftDelete(typeof(TChild), nameof(TChild.ForeignKey))]

When the parent is soft-deleted, all matching child records are soft-deleted in the same operation using a raw SQL `UPDATE` (no N+1 queries). Restore also cascades.

```csharp
[CrudEntity]
[CascadeSoftDelete(typeof(OrderLine), nameof(OrderLine.OrderId))]
public class Order : FullAuditableEntity { }
```

### [ChildOf(typeof(TParent))]

Declares a child entity and its parent. CrudKit generates nested REST endpoints automatically under the parent route when the parent is registered — no manual `.WithChild()` call required.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ParentType` | `Type` | — | The parent entity type |
| `Route` | `string` | pluralized child name | URL segment appended to the parent route |
| `ForeignKey` | `string` | `"{ParentType}Id"` | FK property name on the child entity |

```csharp
[ChildOf(typeof(Order))]
public class OrderLine : AuditableEntity
{
    public Guid OrderId { get; set; }           // resolved by convention
    public string ProductName { get; set; } = string.Empty;
}

// Explicit route and FK
[ChildOf(typeof(Order), Route = "items", ForeignKey = "ParentOrderId")]
public class OrderItem : AuditableEntity { }
```

Auto-generated endpoints (assuming an `Order` parent entity with route `/api/orders`):

| Method | Route |
|--------|-------|
| GET | `/api/orders/{id}/order-lines` |
| GET | `/api/orders/{id}/order-lines/{lineId}` |
| DELETE | `/api/orders/{id}/order-lines/{lineId}` |
| POST | `/api/orders/{id}/order-lines` (when `[CreateDtoFor]` exists for the child) |

### [CreateDtoFor(typeof(TEntity))] / [UpdateDtoFor(typeof(TEntity))]

Applied to a manually written DTO. Tells SourceGen to skip generating the corresponding DTO for `TEntity`. `ResponseDto` and mapper are still generated automatically.

```csharp
[CreateDtoFor(typeof(Order))]
public record CreateOrder([Required] string CustomerName, decimal Total = 0);

[UpdateDtoFor(typeof(Order))]
public record UpdateOrder
{
    public Optional<string?> CustomerName { get; init; }
}
```

Use this when the default generated DTO doesn't match your API contract — you retain full control without giving up the generated mapper and response type.

`[UpdateDtoFor]` is also used for [child entity](../features/child-entities) update auto-discovery.

### [ResponseDtoFor(typeof(TEntity))]

Marks a class as the response DTO for an entity. Used with `IResponseMapper` for custom response shapes.

```csharp
[ResponseDtoFor(typeof(Invoice))]
public class InvoiceResponse
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; }
    public string AssociateName { get; set; } // computed field
    public decimal GrandTotal { get; set; }
}
```

### [AutoSequence(template)]

Generates sequential numbers automatically on Create. See [Auto Sequence](../features/auto-sequence) for details.

```csharp
[AutoSequence("INV-{year}-{seq:5}")]
public string InvoiceNumber { get; set; } = "";
```

### [ValueObject]

Marks a class as a value object for SourceGen DTO generation. See [Value Objects](../features/value-objects).

```csharp
[ValueObject]
public class Money
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TRY";
}
```

### [Flatten]

When applied to a property whose type is a `[ValueObject]`, flattens its properties into the parent DTO. See [Value Objects](../features/value-objects).

```csharp
[Flatten]
public Money Price { get; set; } = new();
// DTO: PriceAmount, PriceCurrency
```

## Property-Level Attributes

| Attribute | Target | Effect |
|-----------|--------|--------|
| `[Hashed]` | Property | BCrypt-hashes the value on Create and Update. Masked as `"***"` in audit trail. |
| `[SkipResponse]` | Property | Excluded from API response JSON (e.g. password hashes). Still audited unless also `[AuditIgnore]`. |
| `[SkipUpdate]` | Property | Set only on Create; ignored on Update (e.g. immutable SKU). |
| `[Protected]` | Property | Cannot be set through the Update DTO. Managed by workflow, hooks, or state machine only. |
| `[Unique]` | Property | Creates a partial unique index (soft-delete compatible). Checked on restore — conflicts return `409`. |
| `[Searchable]` | Property | Included in global full-text search queries. |
| `[AuditIgnore]` | Property | Completely excluded from audit trail. Field never appears in old/new change values. |
| `[NotExportable]` | Property | Excluded from CSV export output. |
| `[NotImportable]` | Property | Ignored during CSV import. |
| `[Filterable]` | Class / Property | Force-enable filtering (overrides entity-level `[NotFilterable]`) |
| `[NotFilterable]` | Class / Property | Disable filtering — queries with this field are silently skipped |
| `[Sortable]` | Class / Property | Force-enable sorting (overrides entity-level `[NotSortable]`) |
| `[NotSortable]` | Class / Property | Disable sorting — sort by this field is silently skipped |
| `[DefaultInclude]` | Class | Auto-includes a navigation property in queries. Supports `IncludeScope.All` or `IncludeScope.DetailOnly`. |

```csharp
[CrudEntity]
[Audited]
public class User : FullAuditableEntity
{
    [Required, MaxLength(50), Searchable, Unique]
    public string Username { get; set; } = string.Empty;

    [Required, Hashed, SkipResponse]
    public string PasswordHash { get; set; } = string.Empty;

    [SkipUpdate]
    public string RegistrationSource { get; set; } = "web";

    [Protected]
    public string Role { get; set; } = "user";

    [AuditIgnore]
    public string SecurityStamp { get; set; } = string.Empty;

    [NotExportable]
    public string InternalToken { get; set; } = string.Empty;
}
```
