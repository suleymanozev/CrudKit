---
sidebar_position: 4
title: Feature Flags
---

# Feature Flags

CrudKit uses a three-level override system for features that can be enabled globally, per-entity, or per-property.

## Three-Level Override

```
Property attribute > Entity attribute > Global flag
```

### Audit Trail

| Level | Enable | Disable |
|-------|--------|---------|
| Global | `opts.UseAuditTrail()` | — (off by default) |
| Entity | `[Audited]` | `[NotAudited]` |
| Property | — | `[AuditIgnore]` |

### Export

| Level | Enable | Disable |
|-------|--------|---------|
| Global | `opts.UseExport()` | — (off by default) |
| Entity | `[Exportable]` | `[NotExportable]` |
| Property | — | `[NotExportable]` |

### Import

| Level | Enable | Disable |
|-------|--------|---------|
| Global | `opts.UseImport()` | — (off by default) |
| Entity | `[Importable]` | `[NotImportable]` |
| Property | — | `[NotImportable]` |

## Endpoint Feature Flags on [CrudEntity]

| Property | Default | Description |
|----------|---------|-------------|
| `ReadOnly` | `false` | Generate List + Get only; no write endpoints |
| `EnableCreate` | `true` | Generate POST endpoint |
| `EnableUpdate` | `true` | Generate PUT endpoint |
| `EnableDelete` | `true` | Generate DELETE endpoint |
| `EnableBulkDelete` | `false` | Generate POST `/bulk-delete` endpoint |
| `EnableBulkUpdate` | `false` | Generate POST `/bulk-update` endpoint |

```csharp
// Read-only entity — List + Get only
[CrudEntity(Table = "currencies", ReadOnly = true)]
public class Currency : Entity { }

// No delete allowed
[CrudEntity(Table = "audit_entries", EnableDelete = false, EnableUpdate = false)]
public class AuditEntry : AuditableEntity { }

// Bulk operations enabled
[CrudEntity(Table = "products", EnableBulkDelete = true, EnableBulkUpdate = true)]
public class Product : AuditableEntity { }
```
