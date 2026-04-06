---
sidebar_position: 12
title: Document Numbering
---

# Document Numbering

Auto-generate sequential document numbers scoped by entity type, tenant, and optionally year.

## Entity Setup

```csharp
[CrudEntity(Table = "invoices", NumberingPrefix = "INV", NumberingYearlyReset = true)]
public class Invoice : AuditableEntity, IDocumentNumbering
{
    public string DocumentNumber { get; set; } = string.Empty; // INV-2026-00001
    public string CustomerName { get; set; } = string.Empty;

    public static string Prefix => "INV";
    public static bool YearlyReset => true;
}
```

Generated format: `{Prefix}-{Year}-{Sequence}` → `INV-2026-00001`

## IDocumentNumbering Interface

```csharp
public interface IDocumentNumbering
{
    string DocumentNumber { get; set; }
    static abstract string Prefix { get; }
    static abstract bool YearlyReset { get; }
}
```

## Configuration via [CrudEntity]

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `NumberingPrefix` | `string` | — | Prefix for the document number (e.g. `"ORD"`, `"INV"`) |
| `NumberingYearlyReset` | `bool` | `true` | Reset sequence counter each calendar year |

## Internals

`SequenceGenerator` uses optimistic concurrency with retry to handle concurrent numbering safely. Sequences are stored in `__crud_sequences` and are tenant-scoped in multi-tenant applications.

When `NumberingYearlyReset = true`, the sequence resets to `00001` at the start of each calendar year. When `false`, the sequence is continuous across years.
