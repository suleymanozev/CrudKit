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

## CrudKit Internal Tables

`CrudKitDbContext` automatically creates these tables when migrations are generated:

| Table | Purpose |
|-------|---------|
| `__crud_audit_logs` | Audit trail entries (created when `UseAuditTrail()` is enabled) |

These tables are defined in `OnModelCreating` and included in the initial migration automatically.

## Notes

- CrudKit **never** calls `EnsureCreated` or `Migrate` automatically in production.
- In the sample project, `EnsureCreated` is used only for development convenience.
- For production, always use explicit migrations and apply them as part of your deployment pipeline.
