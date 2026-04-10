---
sidebar_position: 1
title: Introduction
slug: /intro
---

# CrudKit

A convention-based CRUD framework for .NET 10. Define entities, get endpoints.

## Features

- Auto-mapped REST endpoints (List, Get, Create, Update, Delete)
- Soft delete with cascade and restore
- Multi-tenant isolation (`ITenantContext` + 5 built-in resolvers + 3-layer cross-tenant protection)
- Audit trail (`[Audited]` + `UseAuditTrail()` opt-in, `[AuditIgnore]` per-property, `[Hashed]` auto-masked)
- Per-operation authorization (`Authorize()` builder — role-based, permission-based, convention)
- Custom endpoints via `.WithCustomEndpoints()` under same route group
- Optimistic concurrency
- Lifecycle hooks (`ICrudHooks<T>`) + global hooks (`IGlobalCrudHook`) for cross-cutting concerns
- Validation (FluentValidation priority, DataAnnotation fallback)
- State machine transitions (`IStateMachine<TState>`)
- Master-child relationships (fluent `.WithChild()`)
- Idempotency via request header
- Bulk operations (`/bulk-count`, `/bulk-delete`, `/bulk-update`)
- ReadOnly entities (List + Get only)
- CSV import/export (`[Exportable]`, `[Importable]`, per-property control)
- Source generation — DTOs, mappers, endpoint mapping, hook stubs
- `Optional<T>` for partial updates (distinguishes null from missing)
- Property attributes: `[Hashed]`, `[SkipResponse]`, `[SkipUpdate]`, `[Protected]`, `[Unique]`, `[Searchable]`
- Document numbering (e.g. `ORD-2026-00001`) with tenant-scoped sequences
- Modular monolith support (`IModule` with assembly scan, multi-DbContext auto-resolution)
- Multi-database dialect (SQLite, PostgreSQL, SQL Server) — auto-detected
- Entity base class hierarchy (`Entity`, `AuditableEntity`, `FullAuditableEntity` — with `<TUser>` variants)
- Enum properties stored as strings automatically
- Structured error responses (409 concurrency, dev/prod stack trace toggle)

## Quick Start

**1. Register services**

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite("Data Source=app.db"));

builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.DefaultPageSize = 25;
    opts.MaxPageSize = 100;
    opts.UseAuditTrail();
    opts.UseMultiTenancy().ResolveTenantFromHeader("X-Tenant-Id");
});
```

**2. Map endpoints**

```csharp
var app = builder.Build();
app.UseCrudKit();

// Route auto-derived from [CrudEntity(Resource = "products")]
app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>();

// Or with SourceGen — maps all [CrudEntity] types in one call
app.MapAllCrudEndpoints();

app.Run();
```

**3. Define an entity**

```csharp
[CrudEntity(Resource = "products")]
[Audited]
[RequirePermissions]  // auto-convention: products:read, products:create, ...
public class Product : FullAuditableEntity
{
    [Required, MaxLength(200), Searchable]
    public string Name { get; set; } = string.Empty;

    [Range(0.01, 999_999.99)]
    public decimal Price { get; set; }

    [Unique, SkipUpdate]
    public string Sku { get; set; } = string.Empty;

    [Hashed, SkipResponse]
    public string InternalToken { get; set; } = string.Empty;
}
```

**Generated endpoints**

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/products` | List (paginated, filtered, sorted) |
| GET | `/api/products/{id}` | Get by ID |
| POST | `/api/products` | Create |
| PUT | `/api/products/{id}` | Update (partial via `Optional<T>`) |
| DELETE | `/api/products/{id}` | Soft-delete (`ISoftDeletable`) |
| POST | `/api/products/{id}/restore` | Restore soft-deleted record |
| POST | `/api/products/{id}/transition/{action}` | State transition (`IStateMachine<TState>` only) |

## Entity Hierarchy

| Class | Provides |
|-------|----------|
| `Entity` | `Guid Id` |
| `Entity<TKey>` | Custom key type (e.g. `long`, `int`) |
| `AuditableEntity` | `Id` + `CreatedAt`, `UpdatedAt` |
| `AuditableEntityWithUser<TUser>` | + `CreatedById`, `UpdatedById` (auto-set from `ICurrentUser`) + navigations |
| `FullAuditableEntity` | `AuditableEntity` + `DeletedAt` (implements `ISoftDeletable`) |
| `FullAuditableEntityWithUser<TUser>` | + `CreatedById`, `UpdatedById`, `DeletedById` (auto-set) + navigations |

## Next Steps

- [Quick Start](getting-started/quick-start) — detailed setup walkthrough
- [Entity Hierarchy](getting-started/entity-hierarchy) — choosing the right base class
- [Configuration](getting-started/configuration) — all configuration options
