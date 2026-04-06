---
sidebar_position: 4
title: ASP.NET Identity Integration
---

# ASP.NET Identity Integration

`CrudKit.Identity` provides `CrudKitIdentityDbContext<TUser>`, a drop-in replacement for `IdentityDbContext<TUser>` that inherits all CrudKit behaviors including soft delete, audit trail, multi-tenancy, and user tracking.

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
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
}
```

Register CrudKit and Identity together:

```csharp
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite("Data Source=app.db"));

builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.UseAuditTrail();
    opts.UseMultiTenancy().ResolveTenantFromClaim("tenant_id");
});

builder.Services
    .AddIdentity<AppUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();
```

## Constructor Overloads

`CrudKitIdentityDbContext<TUser>` provides three constructors to match different application needs:

### 1-Param Constructor

Minimal setup. Uses default Identity table names and schema. Suitable for simple applications.

```csharp
public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
```

### 3-Param Constructor

Passes `ICurrentUser` and an optional `TimeProvider` to enable CrudKit user tracking (`CreatedById`, `UpdatedById`, `DeletedById`) and testable timestamps.

```csharp
public AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentUser currentUser,
    TimeProvider? timeProvider = null)
    : base(options, currentUser, timeProvider) { }
```

### 8-Param Constructor

Full control over Identity table names and schema. Use when you need custom table names or a dedicated Identity schema.

```csharp
public AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentUser currentUser,
    TimeProvider? timeProvider,
    string schema,
    string usersTable,
    string rolesTable,
    string userClaimsTable,
    string userRolesTable)
    : base(options, currentUser, timeProvider, schema, usersTable, rolesTable, userClaimsTable, userRolesTable) { }
```

Example with custom schema:

```csharp
public AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentUser currentUser,
    TimeProvider? timeProvider = null)
    : base(options, currentUser, timeProvider,
           schema: "identity",
           usersTable: "users",
           rolesTable: "roles",
           userClaimsTable: "user_claims",
           userRolesTable: "user_roles") { }
```

## Combining with FullAuditableEntityWithUser

Use `FullAuditableEntityWithUser<TUser>` for entities that need user-tracked audit fields. `AppDbContext` inheriting from `CrudKitIdentityDbContext<AppUser>` wires everything together:

```csharp
[CrudEntity(Table = "invoices")]
[Audited]
public class Invoice : FullAuditableEntityWithUser<AppUser>
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
```

`CreatedById`, `UpdatedById`, and `DeletedById` are set automatically from `ICurrentUser.Id` on every `SaveChanges`.

## Migrations

Run migrations as usual — Identity tables and CrudKit internal tables (`__crud_audit_logs`, `__crud_sequences`) are all included automatically:

```bash
dotnet ef migrations add InitialCreate -c AppDbContext
dotnet ef database update -c AppDbContext
```
