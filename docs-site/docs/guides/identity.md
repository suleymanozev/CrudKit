---
sidebar_position: 4
title: ASP.NET Identity Integration
---

# ASP.NET Identity Integration

`CrudKit.Identity` provides `CrudKitIdentityDbContext`, a drop-in replacement for `IdentityDbContext` that includes all CrudKit behaviors: soft delete, audit trail, multi-tenancy, timestamps, and user tracking.

## Installation

```bash
dotnet add package CrudKit.Identity
```

## Quick Setup

Derive your `AppDbContext` from `CrudKitIdentityDbContext<TUser>` instead of `CrudKitDbContext`:

```csharp
public class AppUser : IdentityUser { }

public class AppDbContext : CrudKitIdentityDbContext<AppUser>
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
}
```

Register CrudKit and Identity together:

```csharp
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite("Data Source=app.db"));

builder.Services.AddCrudKitEf<AppDbContext>();
builder.Services.AddCrudKit(opts =>
{
    opts.UseAuditTrail();
    opts.UseMultiTenancy().ResolveTenantFromClaim("tenant_id");
});

builder.Services
    .AddIdentity<AppUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();
```

## Class Overloads

Three class variants with increasing Identity customization. All share the same constructor signature — only the generic type parameters differ.

### `CrudKitIdentityDbContext<TUser>` — most common

Uses `string` keys and default `IdentityRole`. Suitable for most applications.

```csharp
public class AppDbContext : CrudKitIdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }
}
```

### `CrudKitIdentityDbContext<TUser, TRole, TKey>` — custom key type

Use when you need `int` or `Guid` keys instead of `string`, or a custom role type.

```csharp
public class AppUser : IdentityUser<int> { }
public class AppRole : IdentityRole<int> { }

public class AppDbContext : CrudKitIdentityDbContext<AppUser, AppRole, int>
{
    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }
}
```

### `CrudKitIdentityDbContext<TUser, TRole, TKey, ...>` — full control (8 type params)

Use when you need fully custom Identity entity types (custom claims, logins, tokens).

```csharp
public class AppDbContext : CrudKitIdentityDbContext<
    AppUser, AppRole, Guid,
    AppUserClaim, AppUserRole, AppUserLogin,
    AppRoleClaim, AppUserToken>
{
    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }
}
```

### Constructor Parameters

All three class variants share the same constructor. Optional parameters are resolved via DI:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `options` | `DbContextOptions` | Yes | EF Core options |
| `currentUser` | `ICurrentUser` | Yes | Current authenticated user |
| `timeProvider` | `TimeProvider?` | No | For testable timestamps (default: `TimeProvider.System`) |
| `efOptions` | `CrudKitEfOptions?` | No | Audit trail, enum-as-string flags |
| `tenantContext` | `ITenantContext?` | No | Current tenant for multi-tenancy |
| `auditWriter` | `IAuditWriter?` | No | Custom audit writer |

### Inheritance Chain

```
CrudKitIdentityDbContext<TUser>
  → CrudKitIdentityDbContext<TUser, IdentityRole, string>
    → CrudKitIdentityDbContext<TUser, TRole, TKey, ...8 params...>
      → IdentityDbContext<TUser, TRole, TKey, ...>
```

All CrudKit logic lives in the 8-param base class. The 1-param and 3-param variants are convenience shortcuts with no additional code.

## Combining with FullAuditableEntityWithUser

Use `FullAuditableEntityWithUser<TUser>` for entities that need user-tracked audit fields:

```csharp
[CrudEntity]
[Audited]
public class Invoice : FullAuditableEntityWithUser<AppUser>
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
```

`CreatedById`, `UpdatedById`, and `DeletedById` are set automatically from `ICurrentUser.Id` on every `SaveChanges`.

## Migrations

Run migrations as usual — Identity tables and CrudKit internal tables (`__crud_audit_logs`) are all included automatically:

```bash
dotnet ef migrations add InitialCreate -c AppDbContext
dotnet ef database update -c AppDbContext
```
