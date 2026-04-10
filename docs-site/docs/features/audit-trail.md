---
sidebar_position: 4
title: Audit Trail
---

# Audit Trail

Records Create, Update, and Delete operations to `__crud_audit_logs` with old/new property values.

## Setup

**1. Enable globally:**

```csharp
opts.UseAuditTrail();
```

**2. Opt entities in:**

```csharp
[CrudEntity(Resource = "orders")]
[Audited]
public class Order : FullAuditableEntity { }
```

**3. Control property visibility:**

| Attribute | Audit behavior |
|-----------|----------------|
| Normal property | Logged with old/new values |
| `[Hashed]` | Change recorded, value masked as `"***"` |
| `[AuditIgnore]` | Field completely excluded from audit log |

**4. Log failed operations (compliance):**

```csharp
opts.UseAuditTrail()
    .EnableAuditFailedOperations();
// Logs: FailedCreate, FailedUpdate, FailedDelete
```

**5. Correlation ID:**

Each `SaveChanges` call assigns a shared `CorrelationId` to all audit entries in that batch. Use it to group related changes (e.g. cascade soft-delete of Order + OrderLines).

**6. Custom audit writer:**

```csharp
opts.UseAuditTrail<ElasticAuditWriter>();

public class ElasticAuditWriter : IAuditWriter
{
    public async Task WriteAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct)
    {
        // Write to Elasticsearch, separate DB, file, etc.
    }
}
```

If `UseAuditTrail()` is not called, `[Audited]` is silently ignored and `__crud_audit_logs` is not created.

## Feature Flag Override (3 Levels)

```
Property attribute > Entity attribute > Global flag
```

| Level | Enable | Disable |
|-------|--------|---------|
| Global | `opts.UseAuditTrail()` | — (off by default) |
| Entity | `[Audited]` | `[NotAudited]` |
| Property | — | `[AuditIgnore]` |

The same 3-level override applies to Export and Import:

| Feature | Global | Entity On | Entity Off | Property Off |
|---------|--------|-----------|------------|--------------|
| Export | `UseExport()` | `[Exportable]` | `[NotExportable]` | `[NotExportable]` |
| Import | `UseImport()` | `[Importable]` | `[NotImportable]` | `[NotImportable]` |
| Audit | `UseAuditTrail()` | `[Audited]` | `[NotAudited]` | `[AuditIgnore]` |

## IAuditWriter Interface

```csharp
public interface IAuditWriter
{
    Task WriteAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct);
}
```

Implement this to write audit entries to any storage backend (Elasticsearch, a separate database, a file, etc.).
