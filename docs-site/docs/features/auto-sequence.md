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

## Default Template

`[AutoSequence]` without a template defaults to `{seq:5}`:

```csharp
[AutoSequence]
public string Code { get; set; }
// → 00001, 00002, 00003
```

## Custom Placeholders

Add custom placeholders in the template and resolve them via `ISequenceCustomizer<T>`:

```csharp
[AutoSequence("{prefix}-{year}-{seq:5}")]
public string InvoiceNumber { get; set; }
```

```csharp
public class InvoiceSequenceCustomizer : ISequenceCustomizer<Invoice>
{
    private readonly ICompanyConfigService _config;

    public Dictionary<string, string>? ResolvePlaceholders(string? tenantId)
        => new() { ["prefix"] = _config.GetPrefix(tenantId) ?? "INV" };
}
// Tenant A: INV-2026-00001
// Tenant B: FTR-2026-00001 (different prefix, independent sequences)
```

## Template from Database

Override the entire template per tenant:

```csharp
public class InvoiceSequenceCustomizer : ISequenceCustomizer<Invoice>
{
    public string? ResolveTemplate(string? tenantId)
        => _config.GetSequenceTemplate(tenantId, "Invoice");
        // Returns "FTR/{year}{month}/{seq:6}" from DB
        // Returns null → falls back to [AutoSequence] attribute
}
```

## Priority

1. `ISequenceCustomizer.ResolveTemplate()` — if non-null, overrides attribute
2. `[AutoSequence("template")]` — attribute template
3. `[AutoSequence]` — default `{seq:5}`

## Behavior

- Sequences are scoped per **tenant + entity type + resolved prefix**
- New year/month resets the sequence (different prefix)
- Atomic increment — no duplicate numbers under concurrency
- If the property already has a value, it is not overwritten
- Stored in `__crud_sequences` table
