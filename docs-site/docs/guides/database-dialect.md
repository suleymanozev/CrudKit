---
sidebar_position: 3
title: Database Dialect
---

# Database Dialect

CrudKit auto-detects the database provider from the EF Core provider name at startup and adapts SQL generation accordingly. No configuration is required.

## Supported Providers

| Provider | Dialect | LIKE behavior |
|----------|---------|---------------|
| SQLite | `SqliteDialect` | `LIKE` (case-insensitive by default) |
| PostgreSQL | `PostgresDialect` | `ILIKE` (case-insensitive) |
| SQL Server | `SqlServerDialect` | `EF.Functions.Like` |
| MySQL / MariaDB | `MySqlDialect` | `LIKE` (case-insensitive by default) |
| Other | `GenericDialect` | `LIKE` fallback |

## Setup

Register the EF Core provider normally — CrudKit handles dialect detection automatically:

```csharp
// SQLite
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite("Data Source=app.db"));

// PostgreSQL
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(connectionString));

// SQL Server
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlServer(connectionString));

// MySQL / MariaDB
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
```

No CrudKit-specific dialect configuration is needed. The correct dialect is selected at startup based on the registered provider.

## MySQL / MariaDB Notes

- MySQL does not support schemas. `UseModuleSchema` calls are silently skipped — use separate databases for module isolation instead. See [Modular Monolith — Schema Isolation](../advanced/modular-monolith.md#schema-isolation).
- Upsert operations use `ON DUPLICATE KEY UPDATE` syntax.
- `LIKE` is case-insensitive by default on most MySQL/MariaDB collations.

## Notes

- Dialect affects string filtering (`like`, `starts` operators in query parameters).
- PostgreSQL uses `ILIKE` for case-insensitive contains/starts-with queries.
- SQLite's `LIKE` is case-insensitive by default for ASCII characters.
- SQL Server uses `EF.Functions.Like` which respects the database collation.
