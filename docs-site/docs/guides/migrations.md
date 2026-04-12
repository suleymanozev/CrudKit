---
sidebar_position: 2
title: Migrations
---

# Migrations

CrudKit uses standard EF Core migrations. `CrudKitDbContext` defines internal tables (`__crud_audit_logs`) in `OnModelCreating` — they are included automatically when you run migrations.

## Initial Setup

```bash
# Initial migration includes CrudKit internal tables + your entities
dotnet ef migrations add InitialCreate -c AppDbContext

# Apply to database
dotnet ef database update -c AppDbContext
```

## Adding New Entities

```bash
# After adding new entities or upgrading CrudKit
dotnet ef migrations add AddInvoiceEntity -c AppDbContext
dotnet ef database update -c AppDbContext
```

## Multiple DbContexts

When using multiple DbContexts (modular monolith pattern), run migrations per context:

```bash
dotnet ef migrations add InitialCreate -c OrderDbContext
dotnet ef migrations add InitialCreate -c InventoryDbContext

dotnet ef database update -c OrderDbContext
dotnet ef database update -c InventoryDbContext
```

## Entity Configuration

CrudKit auto-configures system fields (query filters, concurrency tokens, unique indexes). For custom entity configuration, use EF Core's `IEntityTypeConfiguration<T>` pattern:

```csharp
// Per-entity configuration file
public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasIndex(e => new { e.TenantId, e.InvoiceNumber }).IsUnique();
        builder.Property(e => e.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.OwnsOne(e => e.Total); // value object
    }
}
```

Register in your DbContext:

```csharp
protected override void OnModelCreatingCustom(ModelBuilder modelBuilder)
{
    UseModuleSchema(modelBuilder, "finance");
    
    // Apply all IEntityTypeConfiguration<T> from this assembly
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceDbContext).Assembly);
}
```

### What CrudKit configures automatically

You do **not** need to configure these — CrudKit handles them in `OnModelCreating`:

| Feature | Configuration |
|---------|--------------|
| `IEntity` | Guid value generator |
| `ISoftDeletable` | `HasQueryFilter` — `DeletedAt == null` |
| `IMultiTenant` | `HasQueryFilter` — `TenantId == CurrentTenantId` |
| `IConcurrent` | `RowVersion` as concurrency token |
| `[Unique]` | Partial unique index (soft-delete aware) |

### What you configure

| Feature | Your configuration |
|---------|-------------------|
| Property constraints | `HasMaxLength`, `IsRequired`, `HasPrecision` |
| Composite indexes | `HasIndex(e => new { e.A, e.B })` |
| Value objects | `OwnsOne(e => e.Address)` |
| Relations | `HasMany`, `HasOne` (navigation properties) |
| Table name override | `ToTable("custom_name")` |
| Schema | `UseModuleSchema(modelBuilder, "schema")` |

## CrudKit Internal Tables

`CrudKitDbContext` automatically creates these tables when migrations are generated:

| Table | Purpose |
|-------|---------|
| `__crud_audit_logs` | Audit trail entries (when `UseAuditTrail()` is enabled) |
| `__crud_sequences` | Auto-sequence counters (when `[AutoSequence]` is used) |

These tables are defined in `OnModelCreating` and included in the initial migration automatically.

## Notes

- CrudKit **never** calls `EnsureCreated` or `Migrate` automatically in production.
- In the sample project, `EnsureCreated` is used only for development convenience.
- For production, always use explicit migrations and apply them as part of your deployment pipeline.
