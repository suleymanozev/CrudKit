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
[CrudEntity(ReadOnly = true)]
public class Currency : Entity { }

// No delete allowed
[CrudEntity(Resource = "audit_entries", EnableDelete = false, EnableUpdate = false)]
public class AuditEntry : AuditableEntity { }

// Bulk operations enabled
[CrudEntity(EnableBulkDelete = true, EnableBulkUpdate = true)]
public class Product : AuditableEntity { }
```

## Best Practice: Global First, Exceptions Only

Enable features globally, then opt-out individual entities. This keeps your entities clean.

### Before — attribute-heavy entities

```csharp
opts.UseAuditTrail(); // only audit trail is global

[CrudEntity]
[Audited]        // redundant — already global
[Exportable]     // per-entity
[Importable]     // per-entity
[RequireAuth]    // per-entity
public class Invoice : FullAuditableAggregateRoot { }

[CrudEntity]
[Audited]        // redundant
[Exportable]     // per-entity
[Importable]     // per-entity
[RequireAuth]    // per-entity
public class Product : FullAuditableAggregateRoot { }

[CrudEntity(ReadOnly = true)]
[Audited]        // redundant
public class Currency : AuditableAggregateRoot { }
```

### After — global defaults, only exceptions annotated

```csharp
// Program.cs — set the defaults once
opts.UseAuditTrail();  // all entities audited
opts.UseExport();      // all entities exportable
opts.UseImport();      // all entities importable
```

```csharp
// Entities — zero repeated attributes
public class Invoice : FullAuditableAggregateRoot { }

public class Product : FullAuditableAggregateRoot { }

// Only annotate exceptions
[NotExportable]       // opt-out: this entity should not be exported
[NotImportable]       // opt-out: this entity should not be imported
public class AuditLog : AuditableEntity { }

[NotAudited]          // opt-out: no need to audit temporary data
public class TempUpload : AuditableEntity { }
```

### Rule of thumb

| If most entities need it... | Do this |
|---|---|
| Audit trail | `opts.UseAuditTrail()` globally, `[NotAudited]` on exceptions |
| Export | `opts.UseExport()` globally, `[NotExportable]` on exceptions |
| Import | `opts.UseImport()` globally, `[NotImportable]` on exceptions |
| Auth | Use `[RequireAuth]` per-entity or set up global auth middleware |

The only attributes that **must** stay on the entity are entity-specific ones with no global equivalent:

- `[AutoSequence]` — sequence template is per-property
- `[Protected]` — per-property decision
- `[CascadeSoftDelete]` — per-entity child relationship
- `[ChildOf]` — per-entity parent relationship
- `[Unique]` — per-property constraint
