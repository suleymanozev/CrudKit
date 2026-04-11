---
sidebar_position: 9
title: Auto Sequence
---

# Auto Sequence

Generate sequential numbers automatically per tenant.

```csharp
[CrudEntity]
public class Invoice : FullAuditableAggregateRoot, IMultiTenant
{
    [AutoSequence("INV-{year}-{seq:5}")]
    public string InvoiceNumber { get; set; } = "";
    // → INV-2026-00001, INV-2026-00002, ...
}
```

## Template Tokens

| Token | Description | Example |
|-------|-------------|---------|
| `{year}` | Current year | 2026 |
| `{month}` | Current month (zero-padded) | 04 |
| `{day}` | Current day (zero-padded) | 09 |
| `{seq:N}` | Sequence number (N = zero-padding width) | 00042 |

## Behavior

- Sequences are scoped per **tenant + entity type + resolved prefix**
- New year/month resets the sequence (different prefix)
- Atomic increment — no duplicate numbers under concurrency
- If the property already has a value, it is not overwritten
- Stored in `__crud_sequences` table
