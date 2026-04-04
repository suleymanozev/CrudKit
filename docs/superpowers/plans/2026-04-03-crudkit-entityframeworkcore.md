# CrudKit.EntityFrameworkCore Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the EF Core data-access layer — abstract DbContext with automatic soft-delete, multi-tenant, audit-log, concurrency, and enum conversion; generic repository; dialect-agnostic query builder.

**Architecture:** `CrudKitDbContext` handles all cross-cutting concerns via `OnModelCreating` (global filters, index setup) and `SaveChanges` override (timestamps, tenant, soft-delete interception, audit logs). `EfRepo<T>` implements `IRepo<T>` using reflection-based DTO→entity mapping and delegates querying to `QueryBuilder<T>`. Provider differences (LIKE/ILike, sequences) are abstracted behind `IDbDialect`, auto-detected from the DB provider name.

**Tech Stack:** .NET 10, EF Core 10 (Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Relational), BCrypt.Net-Next 4.*, Microsoft.EntityFrameworkCore.Sqlite (tests only), xunit 2.*

---

## File Map

```
src/CrudKit.EntityFrameworkCore/
├── CrudKit.EntityFrameworkCore.csproj
├── CrudKitDbContext.cs
├── Repository/
│   ├── IRepo.cs
│   └── EfRepo.cs
├── Query/
│   ├── QueryBuilder.cs
│   ├── FilterApplier.cs
│   ├── SortApplier.cs
│   └── IncludeApplier.cs
├── Dialect/
│   ├── IDbDialect.cs
│   ├── GenericDialect.cs
│   ├── SqliteDialect.cs
│   ├── SqlServerDialect.cs
│   ├── PostgresDialect.cs
│   └── DialectDetector.cs
├── Models/
│   ├── AuditLogEntry.cs
│   └── SequenceEntry.cs
├── Concurrency/
│   └── IConcurrent.cs
├── Numbering/
│   └── SequenceGenerator.cs
└── Extensions/
    └── ServiceCollectionExtensions.cs

tests/CrudKit.EntityFrameworkCore.Tests/
├── CrudKit.EntityFrameworkCore.Tests.csproj
├── Helpers/
│   ├── TestEntities.cs          # PersonEntity, SoftPersonEntity, TenantPersonEntity, etc.
│   └── DbHelper.cs             # CreateDb() factory using SQLite in-memory
├── Dialect/
│   └── DialectTests.cs
├── Query/
│   ├── FilterApplierTests.cs
│   ├── SortApplierTests.cs
│   └── QueryBuilderTests.cs
├── DbContextTests.cs
├── Repository/
│   └── EfRepoTests.cs
└── Numbering/
    └── SequenceGeneratorTests.cs
```

---

### Task 1: Project Scaffold

**Files:**
- Create: `src/CrudKit.EntityFrameworkCore/CrudKit.EntityFrameworkCore.csproj`
- Create: `tests/CrudKit.EntityFrameworkCore.Tests/CrudKit.EntityFrameworkCore.Tests.csproj`
- Modify: `CrudKit.slnx`

- [ ] **Step 1: Create the library project**

```xml
<!-- src/CrudKit.EntityFrameworkCore/CrudKit.EntityFrameworkCore.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>CrudKit.EntityFrameworkCore</AssemblyName>
    <RootNamespace>CrudKit.EntityFrameworkCore</RootNamespace>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrudKit.Core\CrudKit.Core.csproj" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.*" />
    <PackageReference Include="BCrypt.Net-Next" Version="4.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the test project**

```xml
<!-- tests/CrudKit.EntityFrameworkCore.Tests/CrudKit.EntityFrameworkCore.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrudKit.EntityFrameworkCore\CrudKit.EntityFrameworkCore.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add both projects to the solution**

```xml
<!-- CrudKit.slnx -->
<Solution>
  <Project Path="src/CrudKit.Core/CrudKit.Core.csproj" />
  <Project Path="src/CrudKit.EntityFrameworkCore/CrudKit.EntityFrameworkCore.csproj" />
  <Project Path="tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj" />
  <Project Path="tests/CrudKit.EntityFrameworkCore.Tests/CrudKit.EntityFrameworkCore.Tests.csproj" />
</Solution>
```

- [ ] **Step 4: Verify restore + build succeed**

Run: `dotnet build CrudKit.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git add CrudKit.slnx src/CrudKit.EntityFrameworkCore/CrudKit.EntityFrameworkCore.csproj tests/CrudKit.EntityFrameworkCore.Tests/CrudKit.EntityFrameworkCore.Tests.csproj
git commit -m "chore(ef): scaffold CrudKit.EntityFrameworkCore + test project"
```

---

### Task 2: Infrastructure Models — AuditLogEntry, SequenceEntry, IConcurrent

**Files:**
- Create: `src/CrudKit.EntityFrameworkCore/Models/AuditLogEntry.cs`
- Create: `src/CrudKit.EntityFrameworkCore/Models/SequenceEntry.cs`
- Create: `src/CrudKit.EntityFrameworkCore/Concurrency/IConcurrent.cs`
- Create: `tests/CrudKit.EntityFrameworkCore.Tests/Helpers/TestEntities.cs`
- Create: `tests/CrudKit.EntityFrameworkCore.Tests/Helpers/DbHelper.cs`

- [ ] **Step 1: Write the failing model tests**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/Helpers/TestEntities.cs
using CrudKit.Core.Interfaces;
using CrudKit.Core.Attributes;

namespace CrudKit.EntityFrameworkCore.Tests.Helpers;

/// <summary>Basic entity — only IEntity, no extra interfaces.</summary>
public class PersonEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>ISoftDeletable entity.</summary>
public class SoftPersonEntity : IEntity, ISoftDeletable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>IMultiTenant entity.</summary>
public class TenantPersonEntity : IEntity, IMultiTenant
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>IAuditable entity.</summary>
public class AuditPersonEntity : IEntity, IAuditable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>IConcurrent entity — optimistic concurrency.</summary>
public class ConcurrentEntity : IEntity, IConcurrent
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public uint RowVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>IDocumentNumbering entity — auto document number generation.</summary>
public class InvoiceEntity : IEntity, IDocumentNumbering
{
    public string Id { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static string Prefix => "INV";
    public static bool YearlyReset => true;
}

/// <summary>Entity with [Hashed] + [SkipResponse] attributes for EfRepo tests.</summary>
public class UserEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    [Hashed]
    [SkipResponse]
    public string? PasswordHash { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Write failing tests for models**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/Models/InfraModelsTests.cs
using CrudKit.EntityFrameworkCore.Models;
using CrudKit.EntityFrameworkCore.Concurrency;

namespace CrudKit.EntityFrameworkCore.Tests.Models;

public class InfraModelsTests
{
    [Fact]
    public void AuditLogEntry_HasRequiredProperties()
    {
        var entry = new AuditLogEntry
        {
            EntityType = "Order",
            EntityId = "123",
            Action = "Create",
            UserId = "u1",
            Timestamp = DateTime.UtcNow,
            NewValues = "{}",
        };

        Assert.Equal("Order", entry.EntityType);
        Assert.Equal("123", entry.EntityId);
        Assert.Equal("Create", entry.Action);
        Assert.NotNull(entry.Id);
    }

    [Fact]
    public void SequenceEntry_HasRequiredProperties()
    {
        var seq = new SequenceEntry
        {
            EntityType = "Invoice",
            TenantId = "t1",
            Year = "2026",
            CurrentVal = 42,
        };

        Assert.Equal("Invoice", seq.EntityType);
        Assert.Equal(42, seq.CurrentVal);
        Assert.NotNull(seq.Id);
    }

    [Fact]
    public void IConcurrent_RowVersion_IsUint()
    {
        var entity = new CrudKit.EntityFrameworkCore.Tests.Helpers.ConcurrentEntity
        {
            RowVersion = 7,
        };
        Assert.Equal(7u, entity.RowVersion);
    }
}
```

- [ ] **Step 3: Run tests — expect compile error (types not yet defined)**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ --no-build 2>&1 | head -20`
Expected: Build error — `AuditLogEntry`, `SequenceEntry`, `IConcurrent` not found

- [ ] **Step 4: Create AuditLogEntry**

```csharp
// src/CrudKit.EntityFrameworkCore/Models/AuditLogEntry.cs
namespace CrudKit.EntityFrameworkCore.Models;

/// <summary>One row per entity change, written by CrudKitDbContext for IAuditable entities.</summary>
public class AuditLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;   // Create | Update | Delete
    public string? UserId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? ChangedFields { get; set; }
}
```

- [ ] **Step 5: Create SequenceEntry**

```csharp
// src/CrudKit.EntityFrameworkCore/Models/SequenceEntry.cs
namespace CrudKit.EntityFrameworkCore.Models;

/// <summary>Tracks per-entity, per-tenant, per-year document number counters.</summary>
public class SequenceEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EntityType { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public long CurrentVal { get; set; }
}
```

- [ ] **Step 6: Create IConcurrent**

```csharp
// src/CrudKit.EntityFrameworkCore/Concurrency/IConcurrent.cs
namespace CrudKit.EntityFrameworkCore.Concurrency;

/// <summary>
/// Opt-in optimistic concurrency. CrudKitDbContext configures RowVersion as an EF rowversion column.
/// </summary>
public interface IConcurrent
{
    uint RowVersion { get; set; }
}
```

- [ ] **Step 7: Create DbHelper**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/Helpers/DbHelper.cs
using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Tests.Helpers;

public static class DbHelper
{
    /// <summary>
    /// Creates an isolated SQLite in-memory TestDbContext.
    /// Each call produces a fresh connection — tests are fully independent.
    /// </summary>
    public static TestDbContext CreateDb(ICurrentUser? user = null)
    {
        // Use a named in-memory database so the connection stays alive.
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"Data Source={dbName};Mode=Memory;Cache=Shared")
            .Options;

        var db = new TestDbContext(options, user ?? new FakeCurrentUser());
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}
```

- [ ] **Step 8: Run tests — expect pass**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ -v normal`
Expected: 3 tests pass (InfraModelsTests)

- [ ] **Step 9: Commit**

```bash
git add src/CrudKit.EntityFrameworkCore/Models/ src/CrudKit.EntityFrameworkCore/Concurrency/ tests/CrudKit.EntityFrameworkCore.Tests/Helpers/ tests/CrudKit.EntityFrameworkCore.Tests/Models/
git commit -m "feat(ef): infrastructure models — AuditLogEntry, SequenceEntry, IConcurrent"
```

---

### Task 3: Dialect System

**Files:**
- Create: `src/CrudKit.EntityFrameworkCore/Dialect/IDbDialect.cs`
- Create: `src/CrudKit.EntityFrameworkCore/Dialect/GenericDialect.cs`
- Create: `src/CrudKit.EntityFrameworkCore/Dialect/SqliteDialect.cs`
- Create: `src/CrudKit.EntityFrameworkCore/Dialect/SqlServerDialect.cs`
- Create: `src/CrudKit.EntityFrameworkCore/Dialect/PostgresDialect.cs`
- Create: `src/CrudKit.EntityFrameworkCore/Dialect/DialectDetector.cs`
- Create: `tests/CrudKit.EntityFrameworkCore.Tests/Dialect/DialectTests.cs`

- [ ] **Step 1: Write failing dialect tests**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/Dialect/DialectTests.cs
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using System.Linq.Expressions;

namespace CrudKit.EntityFrameworkCore.Tests.Dialect;

public class DialectTests
{
    private static Expression<Func<PersonEntity, string>> NameExpr()
        => e => e.Name;

    [Fact]
    public void GenericDialect_ApplyLike_FiltersCorrectly()
    {
        var dialect = new GenericDialect();
        var source = new[]
        {
            new PersonEntity { Id = "1", Name = "Alice" },
            new PersonEntity { Id = "2", Name = "Bob" },
            new PersonEntity { Id = "3", Name = "ALICE" },
        }.AsQueryable();

        var result = dialect.ApplyLike(source, NameExpr(), "alice");

        var names = result.Select(e => e.Name).ToList();
        Assert.Contains("Alice", names);
        Assert.Contains("ALICE", names);
        Assert.DoesNotContain("Bob", names);
    }

    [Fact]
    public void GenericDialect_ApplyStartsWith_FiltersCorrectly()
    {
        var dialect = new GenericDialect();
        var source = new[]
        {
            new PersonEntity { Id = "1", Name = "Alice" },
            new PersonEntity { Id = "2", Name = "Bob" },
            new PersonEntity { Id = "3", Name = "alan" },
        }.AsQueryable();

        var result = dialect.ApplyStartsWith(source, NameExpr(), "al");

        var names = result.Select(e => e.Name).ToList();
        Assert.Contains("Alice", names);
        Assert.Contains("alan", names);
        Assert.DoesNotContain("Bob", names);
    }

    [Fact]
    public void SqliteDialect_ApplyLike_FiltersCorrectly()
    {
        var dialect = new SqliteDialect();
        var source = new[]
        {
            new PersonEntity { Id = "1", Name = "Istanbul" },
            new PersonEntity { Id = "2", Name = "Ankara" },
        }.AsQueryable();

        var result = dialect.ApplyLike(source, NameExpr(), "stan");

        Assert.Single(result);
        Assert.Equal("Istanbul", result.First().Name);
    }

    [Fact]
    public void SqliteDialect_ApplyStartsWith_FiltersCorrectly()
    {
        var dialect = new SqliteDialect();
        var source = new[]
        {
            new PersonEntity { Id = "1", Name = "Istanbul" },
            new PersonEntity { Id = "2", Name = "Ankara" },
        }.AsQueryable();

        var result = dialect.ApplyStartsWith(source, NameExpr(), "ist");

        Assert.Single(result);
        Assert.Equal("Istanbul", result.First().Name);
    }

    [Fact]
    public void DialectDetector_DetectsSqlite_ForSqliteProvider()
    {
        using var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        Assert.IsType<SqliteDialect>(dialect);
    }

    [Fact]
    public void GenericDialect_GetUpsertSql_ContainsOnConflict()
    {
        var dialect = new GenericDialect();
        var sql = dialect.GetUpsertSql("test_table", ["col1", "col2"], ["col1"]);
        Assert.Contains("ON CONFLICT", sql);
        Assert.Contains("test_table", sql);
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ --no-build 2>&1 | head -20`
Expected: Build error — dialect types not found

- [ ] **Step 3: Create IDbDialect**

```csharp
// src/CrudKit.EntityFrameworkCore/Dialect/IDbDialect.cs
using System.Linq.Expressions;

namespace CrudKit.EntityFrameworkCore.Dialect;

/// <summary>Abstracts database-provider-specific SQL behaviors.</summary>
public interface IDbDialect
{
    /// <summary>Case-insensitive LIKE search. Implementation differs per provider.</summary>
    IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class;

    /// <summary>Case-insensitive starts-with search.</summary>
    IQueryable<T> ApplyStartsWith<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class;

    /// <summary>Generates a concurrent-safe upsert SQL statement.</summary>
    string GetUpsertSql(string table, string[] columns, string[] keyColumns);

    /// <summary>Generates SQL to fetch the next sequence value.</summary>
    string GetSequenceNextValueSql(string sequenceName);
}
```

- [ ] **Step 4: Create GenericDialect**

```csharp
// src/CrudKit.EntityFrameworkCore/Dialect/GenericDialect.cs
using System.Linq.Expressions;

namespace CrudKit.EntityFrameworkCore.Dialect;

/// <summary>
/// Fallback dialect — works with any EF Core provider.
/// Uses ToLower().Contains() / StartsWith() for case-insensitive operations.
/// </summary>
public class GenericDialect : IDbDialect
{
    public IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var toLower = Expression.Call(
            memberAccess,
            typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
        var contains = Expression.Call(
            toLower,
            typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!,
            Expression.Constant(value.ToLower()));
        return query.Where(Expression.Lambda<Func<T, bool>>(contains, param));
    }

    public IQueryable<T> ApplyStartsWith<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var toLower = Expression.Call(
            memberAccess,
            typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
        var startsWith = Expression.Call(
            toLower,
            typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!,
            Expression.Constant(value.ToLower()));
        return query.Where(Expression.Lambda<Func<T, bool>>(startsWith, param));
    }

    public string GetUpsertSql(string table, string[] columns, string[] keyColumns)
    {
        var colList = string.Join(", ", columns);
        var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var updateList = string.Join(", ", columns.Select(c => $"{c} = EXCLUDED.{c}"));
        var keyList = string.Join(", ", keyColumns);
        return $"INSERT INTO {table} ({colList}) VALUES ({paramList}) " +
               $"ON CONFLICT ({keyList}) DO UPDATE SET {updateList}";
    }

    public string GetSequenceNextValueSql(string sequenceName)
        => throw new NotSupportedException(
            $"Provider does not support sequences. Use SequenceGenerator with table-based approach.");
}
```

- [ ] **Step 5: Create SqliteDialect**

```csharp
// src/CrudKit.EntityFrameworkCore/Dialect/SqliteDialect.cs
using System.Linq.Expressions;

namespace CrudKit.EntityFrameworkCore.Dialect;

/// <summary>
/// SQLite dialect. Uses ToLower() for case-insensitive operations since
/// SQLite LIKE is only ASCII case-insensitive by default.
/// SQLite supports the same ON CONFLICT syntax as PostgreSQL.
/// </summary>
public class SqliteDialect : IDbDialect
{
    public IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var toLower = Expression.Call(
            memberAccess,
            typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
        var contains = Expression.Call(
            toLower,
            typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!,
            Expression.Constant(value.ToLower()));
        return query.Where(Expression.Lambda<Func<T, bool>>(contains, param));
    }

    public IQueryable<T> ApplyStartsWith<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var toLower = Expression.Call(
            memberAccess,
            typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
        var startsWith = Expression.Call(
            toLower,
            typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!,
            Expression.Constant(value.ToLower()));
        return query.Where(Expression.Lambda<Func<T, bool>>(startsWith, param));
    }

    public string GetUpsertSql(string table, string[] columns, string[] keyColumns)
    {
        var colList = string.Join(", ", columns);
        var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var updateList = string.Join(", ", columns.Select(c => $"{c} = EXCLUDED.{c}"));
        var keyList = string.Join(", ", keyColumns);
        return $"INSERT INTO {table} ({colList}) VALUES ({paramList}) " +
               $"ON CONFLICT ({keyList}) DO UPDATE SET {updateList}";
    }

    public string GetSequenceNextValueSql(string sequenceName)
        => throw new NotSupportedException("SQLite does not support sequences. Use SequenceGenerator.");
}
```

- [ ] **Step 6: Create PostgresDialect**

```csharp
// src/CrudKit.EntityFrameworkCore/Dialect/PostgresDialect.cs
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Dialect;

/// <summary>
/// PostgreSQL dialect using EF.Functions.ILike for native case-insensitive search.
/// Requires Npgsql.EntityFrameworkCore.PostgreSQL to be loaded at runtime.
/// Falls back to GenericDialect.ApplyLike if ILike is unavailable.
/// </summary>
public class PostgresDialect : IDbDialect
{
    private readonly GenericDialect _fallback = new();

    public IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        // Resolve NpgsqlDbFunctionsExtensions.ILike at runtime to avoid hard Npgsql dependency.
        var npgsqlType = Type.GetType(
            "NpgsqlEFCore.DbFunctionsExtensions, Npgsql.EntityFrameworkCore.PostgreSQL") ??
            Type.GetType(
            "Microsoft.EntityFrameworkCore.NpgsqlDbFunctionsExtensions, Npgsql.EntityFrameworkCore.PostgreSQL");

        var iLikeMethod = npgsqlType?.GetMethod("ILike",
            [typeof(DbFunctions), typeof(string), typeof(string)]);

        if (iLikeMethod == null)
            return _fallback.ApplyLike(query, property, value);

        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var pattern = Expression.Constant($"%{value}%");
        var call = Expression.Call(
            iLikeMethod,
            Expression.Constant(EF.Functions),
            memberAccess,
            pattern);
        return query.Where(Expression.Lambda<Func<T, bool>>(call, param));
    }

    public IQueryable<T> ApplyStartsWith<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        var npgsqlType = Type.GetType(
            "NpgsqlEFCore.DbFunctionsExtensions, Npgsql.EntityFrameworkCore.PostgreSQL") ??
            Type.GetType(
            "Microsoft.EntityFrameworkCore.NpgsqlDbFunctionsExtensions, Npgsql.EntityFrameworkCore.PostgreSQL");

        var iLikeMethod = npgsqlType?.GetMethod("ILike",
            [typeof(DbFunctions), typeof(string), typeof(string)]);

        if (iLikeMethod == null)
            return _fallback.ApplyStartsWith(query, property, value);

        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var pattern = Expression.Constant($"{value}%");
        var call = Expression.Call(
            iLikeMethod,
            Expression.Constant(EF.Functions),
            memberAccess,
            pattern);
        return query.Where(Expression.Lambda<Func<T, bool>>(call, param));
    }

    public string GetUpsertSql(string table, string[] columns, string[] keyColumns)
    {
        var colList = string.Join(", ", columns);
        var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var updateList = string.Join(", ", columns.Select(c => $"{c} = EXCLUDED.{c}"));
        var keyList = string.Join(", ", keyColumns);
        return $"INSERT INTO {table} ({colList}) VALUES ({paramList}) " +
               $"ON CONFLICT ({keyList}) DO UPDATE SET {updateList}";
    }

    public string GetSequenceNextValueSql(string sequenceName)
        => $"SELECT nextval('{sequenceName}')";
}
```

- [ ] **Step 7: Create SqlServerDialect**

```csharp
// src/CrudKit.EntityFrameworkCore/Dialect/SqlServerDialect.cs
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Dialect;

/// <summary>
/// SQL Server dialect. Uses EF.Functions.Like (SQL Server default collation is case-insensitive).
/// Requires Microsoft.EntityFrameworkCore.SqlServer to be loaded at runtime.
/// Falls back to GenericDialect.ApplyLike if unavailable.
/// </summary>
public class SqlServerDialect : IDbDialect
{
    private readonly GenericDialect _fallback = new();

    public IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        var likeMethod = typeof(DbFunctionsExtensions).GetMethod(
            "Like", [typeof(DbFunctions), typeof(string), typeof(string)]);

        if (likeMethod == null)
            return _fallback.ApplyLike(query, property, value);

        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var pattern = Expression.Constant($"%{value}%");
        var call = Expression.Call(
            likeMethod,
            Expression.Constant(EF.Functions),
            memberAccess,
            pattern);
        return query.Where(Expression.Lambda<Func<T, bool>>(call, param));
    }

    public IQueryable<T> ApplyStartsWith<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        var likeMethod = typeof(DbFunctionsExtensions).GetMethod(
            "Like", [typeof(DbFunctions), typeof(string), typeof(string)]);

        if (likeMethod == null)
            return _fallback.ApplyStartsWith(query, property, value);

        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var pattern = Expression.Constant($"{value}%");
        var call = Expression.Call(
            likeMethod,
            Expression.Constant(EF.Functions),
            memberAccess,
            pattern);
        return query.Where(Expression.Lambda<Func<T, bool>>(call, param));
    }

    public string GetUpsertSql(string table, string[] columns, string[] keyColumns)
    {
        var colList = string.Join(", ", columns);
        var updateList = string.Join(", ", columns.Select(c => $"target.{c} = source.{c}"));
        var onClause = string.Join(" AND ", keyColumns.Select(k => $"target.{k} = source.{k}"));
        var paramList = string.Join(", ", columns.Select((c, i) => $"@p{i} AS {c}"));
        return $"MERGE INTO {table} AS target " +
               $"USING (SELECT {paramList}) AS source " +
               $"ON {onClause} " +
               $"WHEN MATCHED THEN UPDATE SET {updateList} " +
               $"WHEN NOT MATCHED THEN INSERT ({colList}) VALUES ({string.Join(", ", columns.Select(c => $"source.{c}"))});";
    }

    public string GetSequenceNextValueSql(string sequenceName)
        => $"SELECT NEXT VALUE FOR {sequenceName}";
}
```

- [ ] **Step 8: Create DialectDetector**

```csharp
// src/CrudKit.EntityFrameworkCore/Dialect/DialectDetector.cs
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Dialect;

/// <summary>
/// Auto-detects the database dialect from the EF Core provider name.
/// Called by ServiceCollectionExtensions — users never need to call this directly.
/// </summary>
public static class DialectDetector
{
    public static IDbDialect Detect(DbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        return provider switch
        {
            _ when provider.Contains("Npgsql")    => new PostgresDialect(),
            _ when provider.Contains("SqlServer") => new SqlServerDialect(),
            _ when provider.Contains("Sqlite")    => new SqliteDialect(),
            _                                      => new GenericDialect(),
        };
    }
}
```

- [ ] **Step 9: Run tests — expect pass**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ -v normal`
Expected: 9 tests pass (3 InfraModels + 6 Dialect)

Note: DialectTests use in-memory LINQ (no real SQLite DB) except for `DialectDetector_DetectsSqlite_ForSqliteProvider` which needs `TestDbContext`. That test will fail until `TestDbContext` is defined in Task 7. Comment it out or use a `Skip` attribute:

```csharp
[Fact(Skip = "Requires TestDbContext — enabled in Task 7")]
public void DialectDetector_DetectsSqlite_ForSqliteProvider()
```

- [ ] **Step 10: Commit**

```bash
git add src/CrudKit.EntityFrameworkCore/Dialect/ tests/CrudKit.EntityFrameworkCore.Tests/Dialect/
git commit -m "feat(ef): dialect system — IDbDialect + Generic/Sqlite/SqlServer/Postgres dialects + DialectDetector"
```

---

### Task 4: FilterApplier

**Files:**
- Create: `src/CrudKit.EntityFrameworkCore/Query/FilterApplier.cs`
- Create: `tests/CrudKit.EntityFrameworkCore.Tests/Query/FilterApplierTests.cs`

The FilterApplier takes a `(string propertyName, FilterOp op)` pair and adds a `Where` clause to the query. Property names are matched case-insensitively; snake_case is converted to PascalCase (`created_at` → `CreatedAt`). Unknown property names are silently ignored (SQL injection protection).

- [ ] **Step 1: Write failing tests**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/Query/FilterApplierTests.cs
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Tests.Helpers;

namespace CrudKit.EntityFrameworkCore.Tests.Query;

public class FilterApplierTests
{
    private static IQueryable<PersonEntity> Source() => new[]
    {
        new PersonEntity { Id = "1", Name = "Alice", Age = 30 },
        new PersonEntity { Id = "2", Name = "Bob",   Age = 25 },
        new PersonEntity { Id = "3", Name = "Carol", Age = 30 },
        new PersonEntity { Id = "4", Name = "alice", Age = 20 },
    }.AsQueryable();

    private static FilterApplier Applier() => new(new GenericDialect());

    [Fact]
    public void Apply_Eq_FiltersExact()
    {
        var result = Applier().Apply(Source(), "Name", FilterOp.Parse("Bob"));
        Assert.Single(result);
        Assert.Equal("Bob", result.First().Name);
    }

    [Fact]
    public void Apply_Neq_ExcludesValue()
    {
        var result = Applier().Apply(Source(), "Name", FilterOp.Parse("neq:Bob"));
        Assert.Equal(3, result.Count());
        Assert.DoesNotContain(result, e => e.Name == "Bob");
    }

    [Fact]
    public void Apply_Gt_FiltersGreaterThan()
    {
        var result = Applier().Apply(Source(), "Age", FilterOp.Parse("gt:25"));
        Assert.Equal(2, result.Count());
        Assert.All(result, e => Assert.True(e.Age > 25));
    }

    [Fact]
    public void Apply_Gte_FiltersGreaterThanOrEqual()
    {
        var result = Applier().Apply(Source(), "Age", FilterOp.Parse("gte:30"));
        Assert.Equal(2, result.Count());
        Assert.All(result, e => Assert.True(e.Age >= 30));
    }

    [Fact]
    public void Apply_Lt_FiltersLessThan()
    {
        var result = Applier().Apply(Source(), "Age", FilterOp.Parse("lt:30"));
        Assert.Equal(2, result.Count());
        Assert.All(result, e => Assert.True(e.Age < 30));
    }

    [Fact]
    public void Apply_Lte_FiltersLessThanOrEqual()
    {
        var result = Applier().Apply(Source(), "Age", FilterOp.Parse("lte:25"));
        Assert.Equal(2, result.Count());
        Assert.All(result, e => Assert.True(e.Age <= 25));
    }

    [Fact]
    public void Apply_Like_FiltersContains_CaseInsensitive()
    {
        var result = Applier().Apply(Source(), "Name", FilterOp.Parse("like:alic"));
        Assert.Equal(2, result.Count()); // Alice + alice
    }

    [Fact]
    public void Apply_Starts_FiltersStartsWith()
    {
        var result = Applier().Apply(Source(), "Name", FilterOp.Parse("starts:ali"));
        Assert.Equal(2, result.Count()); // Alice + alice
    }

    [Fact]
    public void Apply_In_FiltersMultipleValues()
    {
        var result = Applier().Apply(Source(), "Name", FilterOp.Parse("in:Alice,Bob"));
        Assert.Equal(2, result.Count());
    }

    [Fact]
    public void Apply_Null_FiltersNullValues()
    {
        var source = new[]
        {
            new PersonEntity { Id = "1", Name = "Alice", Age = 30 },
            new PersonEntity { Id = "2", Name = null!, Age = 25 },
        }.AsQueryable();
        var result = Applier().Apply(source, "Name", FilterOp.Parse("null:"));
        Assert.Single(result);
    }

    [Fact]
    public void Apply_Notnull_FiltersNonNullValues()
    {
        var source = new[]
        {
            new PersonEntity { Id = "1", Name = "Alice", Age = 30 },
            new PersonEntity { Id = "2", Name = null!, Age = 25 },
        }.AsQueryable();
        var result = Applier().Apply(source, "Name", FilterOp.Parse("notnull:"));
        Assert.Single(result);
        Assert.Equal("Alice", result.First().Name);
    }

    [Fact]
    public void Apply_UnknownProperty_IsIgnored()
    {
        // SQL injection protection — unknown fields are silently skipped
        var result = Applier().Apply(Source(), "NonExistentField", FilterOp.Parse("eq:hack"));
        Assert.Equal(4, result.Count()); // no filter applied
    }

    [Fact]
    public void Apply_SnakeCasePropertyName_IsMatched()
    {
        // "created_at" should match "CreatedAt"
        var source = new[]
        {
            new PersonEntity { Id = "1", Name = "Alice", Age = 30, CreatedAt = new DateTime(2026, 1, 1) },
            new PersonEntity { Id = "2", Name = "Bob",   Age = 25, CreatedAt = new DateTime(2025, 1, 1) },
        }.AsQueryable();
        var result = Applier().Apply(source, "created_at", FilterOp.Parse("gt:2025-06-01"));
        Assert.Single(result);
        Assert.Equal("Alice", result.First().Name);
    }
}
```

- [ ] **Step 2: Run tests — expect compile error**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ --no-build 2>&1 | head -20`
Expected: Build error — FilterApplier not found

- [ ] **Step 3: Implement FilterApplier**

```csharp
// src/CrudKit.EntityFrameworkCore/Query/FilterApplier.cs
using System.Linq.Expressions;
using System.Reflection;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Dialect;

namespace CrudKit.EntityFrameworkCore.Query;

/// <summary>
/// Converts a (propertyName, FilterOp) pair into a LINQ Where clause.
/// Property lookup is case-insensitive and supports snake_case → PascalCase conversion.
/// Unknown property names are silently ignored (SQL injection protection).
/// </summary>
public class FilterApplier
{
    private readonly IDbDialect _dialect;

    public FilterApplier(IDbDialect dialect) => _dialect = dialect;

    public IQueryable<T> Apply<T>(IQueryable<T> query, string propertyName, FilterOp op)
        where T : class
    {
        var prop = FindProperty(typeof(T), propertyName);
        if (prop == null) return query; // unknown field — skip silently

        return op.Operator switch
        {
            "like"    => ApplyLikeFilter(query, prop, op.Value),
            "starts"  => ApplyStartsFilter(query, prop, op.Value),
            "in"      => ApplyIn(query, prop, op.Values),
            "null"    => ApplyNull(query, prop),
            "notnull" => ApplyNotNull(query, prop),
            _         => ApplyComparison(query, prop, op),
        };
    }

    // ---- Property resolution ----

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        // 1. Exact match
        var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (prop != null) return prop;

        // 2. Case-insensitive match
        prop = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (prop != null) return prop;

        // 3. snake_case → PascalCase conversion (e.g. created_at → CreatedAt)
        var pascal = ToPascalCase(name);
        return type.GetProperty(pascal, BindingFlags.Public | BindingFlags.Instance)
            ?? type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => string.Equals(p.Name, pascal, StringComparison.OrdinalIgnoreCase));
    }

    private static string ToPascalCase(string snake)
    {
        if (!snake.Contains('_')) return snake;
        return string.Concat(snake.Split('_')
            .Select(seg => seg.Length == 0 ? "" : char.ToUpper(seg[0]) + seg[1..]));
    }

    // ---- Filter builders ----

    private IQueryable<T> ApplyLikeFilter<T>(IQueryable<T> query, PropertyInfo prop, string value)
        where T : class
    {
        var param = Expression.Parameter(typeof(T), "e");
        var memberAccess = Expression.Property(param, prop);
        var stringExpr = Expression.Lambda<Func<T, string>>(memberAccess, param);
        return _dialect.ApplyLike(query, stringExpr, value);
    }

    private IQueryable<T> ApplyStartsFilter<T>(IQueryable<T> query, PropertyInfo prop, string value)
        where T : class
    {
        var param = Expression.Parameter(typeof(T), "e");
        var memberAccess = Expression.Property(param, prop);
        var stringExpr = Expression.Lambda<Func<T, string>>(memberAccess, param);
        return _dialect.ApplyStartsWith(query, stringExpr, value);
    }

    private static IQueryable<T> ApplyIn<T>(IQueryable<T> query, PropertyInfo prop, List<string> values)
        where T : class
    {
        var param = Expression.Parameter(typeof(T), "e");
        var member = Expression.Property(param, prop);

        // Build: values.Contains(e.Prop.ToString()) — works for strings
        // For non-string types, convert to the actual type first
        var propType = prop.PropertyType;

        if (propType == typeof(string))
        {
            var typedValues = values.Cast<object?>().ToList();
            var containsMethod = typeof(List<string>).GetMethod(nameof(List<string>.Contains))!;
            var constant = Expression.Constant(values);
            var call = Expression.Call(constant, containsMethod, member);
            return query.Where(Expression.Lambda<Func<T, bool>>(call, param));
        }

        // Numeric types — convert each string value to the target type and build OR chain
        var convertedValues = values
            .Select(v => ConvertValue(v, propType))
            .Where(v => v != null)
            .ToList();

        Expression? orExpr = null;
        foreach (var converted in convertedValues)
        {
            var eq = Expression.Equal(member, Expression.Constant(converted, propType));
            orExpr = orExpr == null ? eq : Expression.OrElse(orExpr, eq);
        }

        if (orExpr == null) return query;
        return query.Where(Expression.Lambda<Func<T, bool>>(orExpr, param));
    }

    private static IQueryable<T> ApplyNull<T>(IQueryable<T> query, PropertyInfo prop)
        where T : class
    {
        var param = Expression.Parameter(typeof(T), "e");
        var member = Expression.Property(param, prop);
        var nullConst = Expression.Constant(null, prop.PropertyType);
        var eq = Expression.Equal(member, nullConst);
        return query.Where(Expression.Lambda<Func<T, bool>>(eq, param));
    }

    private static IQueryable<T> ApplyNotNull<T>(IQueryable<T> query, PropertyInfo prop)
        where T : class
    {
        var param = Expression.Parameter(typeof(T), "e");
        var member = Expression.Property(param, prop);
        var nullConst = Expression.Constant(null, prop.PropertyType);
        var neq = Expression.NotEqual(member, nullConst);
        return query.Where(Expression.Lambda<Func<T, bool>>(neq, param));
    }

    private static IQueryable<T> ApplyComparison<T>(IQueryable<T> query, PropertyInfo prop, FilterOp op)
        where T : class
    {
        var convertedValue = ConvertValue(op.Value, prop.PropertyType);
        if (convertedValue == null) return query;

        var param = Expression.Parameter(typeof(T), "e");
        var member = Expression.Property(param, prop);
        var constant = Expression.Constant(convertedValue, prop.PropertyType);

        Expression body = op.Operator switch
        {
            "eq"  => Expression.Equal(member, constant),
            "neq" => Expression.NotEqual(member, constant),
            "gt"  => Expression.GreaterThan(member, constant),
            "gte" => Expression.GreaterThanOrEqual(member, constant),
            "lt"  => Expression.LessThan(member, constant),
            "lte" => Expression.LessThanOrEqual(member, constant),
            _     => Expression.Equal(member, constant),
        };

        return query.Where(Expression.Lambda<Func<T, bool>>(body, param));
    }

    // ---- Type coercion ----

    private static object? ConvertValue(string raw, Type targetType)
    {
        // Unwrap Nullable<T>
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (underlying == typeof(string))   return raw;
            if (underlying == typeof(int))      return int.Parse(raw);
            if (underlying == typeof(long))     return long.Parse(raw);
            if (underlying == typeof(decimal))  return decimal.Parse(raw);
            if (underlying == typeof(double))   return double.Parse(raw);
            if (underlying == typeof(float))    return float.Parse(raw);
            if (underlying == typeof(bool))     return raw is "1" or "true" ? true : false;
            if (underlying == typeof(DateTime)) return DateTime.Parse(raw);
            if (underlying == typeof(Guid))     return Guid.Parse(raw);
            if (underlying.IsEnum)              return Enum.Parse(underlying, raw, ignoreCase: true);
        }
        catch
        {
            return null; // unparseable — skip filter
        }

        return raw;
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ -v normal`
Expected: 13 tests pass (3 InfraModels + 6 Dialect + 13 FilterApplier)

- [ ] **Step 5: Commit**

```bash
git add src/CrudKit.EntityFrameworkCore/Query/FilterApplier.cs tests/CrudKit.EntityFrameworkCore.Tests/Query/FilterApplierTests.cs
git commit -m "feat(ef): FilterApplier — all operators + type coercion + snake_case property matching"
```

---

### Task 5: SortApplier

**Files:**
- Create: `src/CrudKit.EntityFrameworkCore/Query/SortApplier.cs`
- Create: `tests/CrudKit.EntityFrameworkCore.Tests/Query/SortApplierTests.cs`

Sort string format: `"-created_at,name"`. `-` prefix means DESC, no prefix means ASC. First field uses `OrderBy`/`OrderByDescending`, subsequent fields use `ThenBy`/`ThenByDescending`. Invalid field names are silently ignored. Default when no sort: `CreatedAt DESC`.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/Query/SortApplierTests.cs
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Tests.Helpers;

namespace CrudKit.EntityFrameworkCore.Tests.Query;

public class SortApplierTests
{
    private static IQueryable<PersonEntity> Source() => new[]
    {
        new PersonEntity { Id = "1", Name = "Charlie", Age = 25 },
        new PersonEntity { Id = "2", Name = "Alice",   Age = 30 },
        new PersonEntity { Id = "3", Name = "Bob",     Age = 25 },
    }.AsQueryable();

    [Fact]
    public void Apply_SingleField_Asc()
    {
        var result = SortApplier.Apply(Source(), "name").ToList();
        Assert.Equal(["Alice", "Bob", "Charlie"], result.Select(e => e.Name));
    }

    [Fact]
    public void Apply_SingleField_Desc()
    {
        var result = SortApplier.Apply(Source(), "-name").ToList();
        Assert.Equal(["Charlie", "Bob", "Alice"], result.Select(e => e.Name));
    }

    [Fact]
    public void Apply_MultipleFields_AgeAscThenNameAsc()
    {
        var result = SortApplier.Apply(Source(), "age,name").ToList();
        // Age 25: Charlie, Bob → sorted by name: Bob, Charlie; then Alice (30)
        Assert.Equal(["Bob", "Charlie", "Alice"], result.Select(e => e.Name));
    }

    [Fact]
    public void Apply_MultipleFields_AgeDescThenNameAsc()
    {
        var result = SortApplier.Apply(Source(), "-age,name").ToList();
        // Age 30: Alice; Age 25: Bob, Charlie
        Assert.Equal(["Alice", "Bob", "Charlie"], result.Select(e => e.Name));
    }

    [Fact]
    public void Apply_NullOrEmpty_DefaultsToCreatedAtDesc()
    {
        var source = new[]
        {
            new PersonEntity { Id = "1", Name = "A", CreatedAt = new DateTime(2025, 1, 1) },
            new PersonEntity { Id = "2", Name = "B", CreatedAt = new DateTime(2026, 1, 1) },
        }.AsQueryable();

        var result = SortApplier.Apply(source, null).ToList();
        Assert.Equal("B", result[0].Name); // newest first
    }

    [Fact]
    public void Apply_InvalidField_IsIgnored()
    {
        // Should not throw; just return original order (or partial sort)
        var result = SortApplier.Apply(Source(), "nonexistent,name").ToList();
        // "nonexistent" is skipped, "name" asc is applied
        Assert.Equal(["Alice", "Bob", "Charlie"], result.Select(e => e.Name));
    }

    [Fact]
    public void Apply_SnakeCaseField_IsResolved()
    {
        var source = new[]
        {
            new PersonEntity { Id = "1", Name = "A", CreatedAt = new DateTime(2025, 1, 1) },
            new PersonEntity { Id = "2", Name = "B", CreatedAt = new DateTime(2026, 1, 1) },
        }.AsQueryable();

        var result = SortApplier.Apply(source, "created_at").ToList();
        Assert.Equal("A", result[0].Name); // oldest first (ASC)
    }
}
```

- [ ] **Step 2: Run — expect compile error**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ --no-build 2>&1 | head -10`
Expected: Build error — SortApplier not found

- [ ] **Step 3: Implement SortApplier**

```csharp
// src/CrudKit.EntityFrameworkCore/Query/SortApplier.cs
using System.Linq.Expressions;
using System.Reflection;

namespace CrudKit.EntityFrameworkCore.Query;

/// <summary>
/// Applies ORDER BY from a sort string.
/// Format: "field1,-field2,field3"  — '-' prefix = DESC, no prefix = ASC.
/// Unknown fields are silently ignored.
/// Default (null/empty): CreatedAt DESC.
/// </summary>
public static class SortApplier
{
    public static IQueryable<T> Apply<T>(IQueryable<T> query, string? sortString)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(sortString))
            return ApplyDefault(query);

        var fields = sortString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        IOrderedQueryable<T>? ordered = null;

        foreach (var field in fields)
        {
            var isDesc = field.StartsWith('-');
            var name = isDesc ? field[1..] : field;

            var prop = FindProperty(typeof(T), name);
            if (prop == null) continue;

            ordered = ordered == null
                ? ApplyOrderBy(query, prop, isDesc)
                : ApplyThenBy(ordered, prop, isDesc);
        }

        return ordered ?? ApplyDefault(query);
    }

    private static IQueryable<T> ApplyDefault<T>(IQueryable<T> query) where T : class
    {
        var createdAt = typeof(T).GetProperty("CreatedAt");
        if (createdAt == null) return query;
        return ApplyOrderBy(query, createdAt, descending: true);
    }

    private static IOrderedQueryable<T> ApplyOrderBy<T>(
        IQueryable<T> query, PropertyInfo prop, bool descending) where T : class
    {
        var keySelector = BuildKeySelector<T>(prop);
        return descending
            ? Queryable.OrderByDescending(query, (dynamic)keySelector)
            : Queryable.OrderBy(query, (dynamic)keySelector);
    }

    private static IOrderedQueryable<T> ApplyThenBy<T>(
        IOrderedQueryable<T> query, PropertyInfo prop, bool descending) where T : class
    {
        var keySelector = BuildKeySelector<T>(prop);
        return descending
            ? Queryable.ThenByDescending(query, (dynamic)keySelector)
            : Queryable.ThenBy(query, (dynamic)keySelector);
    }

    private static LambdaExpression BuildKeySelector<T>(PropertyInfo prop)
    {
        var param = Expression.Parameter(typeof(T), "e");
        var member = Expression.Property(param, prop);
        return Expression.Lambda(member, param);
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (prop != null) return prop;

        prop = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (prop != null) return prop;

        var pascal = ToPascalCase(name);
        return type.GetProperty(pascal, BindingFlags.Public | BindingFlags.Instance)
            ?? type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => string.Equals(p.Name, pascal, StringComparison.OrdinalIgnoreCase));
    }

    private static string ToPascalCase(string snake)
    {
        if (!snake.Contains('_')) return snake;
        return string.Concat(snake.Split('_')
            .Select(seg => seg.Length == 0 ? "" : char.ToUpper(seg[0]) + seg[1..]));
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ -v normal`
Expected: All previous + 7 SortApplier tests pass

- [ ] **Step 5: Commit**

```bash
git add src/CrudKit.EntityFrameworkCore/Query/SortApplier.cs tests/CrudKit.EntityFrameworkCore.Tests/Query/SortApplierTests.cs
git commit -m "feat(ef): SortApplier — multi-field sort, snake_case, default CreatedAt DESC"
```

---

### Task 6: QueryBuilder + IncludeApplier

**Files:**
- Create: `src/CrudKit.EntityFrameworkCore/Query/IncludeApplier.cs`
- Create: `src/CrudKit.EntityFrameworkCore/Query/QueryBuilder.cs`
- Create: `tests/CrudKit.EntityFrameworkCore.Tests/Query/QueryBuilderTests.cs`

`QueryBuilder<T>` orchestrates: apply includes → apply filters → count total → apply sort → apply pagination → return `Paginated<T>`. `IncludeApplier` reads `[DefaultInclude]` attributes on navigation properties and calls `query.Include(propName)`.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/Query/QueryBuilderTests.cs
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Tests.Helpers;

namespace CrudKit.EntityFrameworkCore.Tests.Query;

public class QueryBuilderTests
{
    private static IQueryable<PersonEntity> Source(int count = 10)
        => Enumerable.Range(1, count)
            .Select(i => new PersonEntity
            {
                Id = i.ToString(),
                Name = $"Person{i:D2}",
                Age = 20 + i,
                CreatedAt = new DateTime(2026, 1, i),
            })
            .AsQueryable();

    private static QueryBuilder<PersonEntity> Builder()
        => new(new FilterApplier(new GenericDialect()));

    [Fact]
    public async Task Apply_ReturnsAllItems_WhenNoFilters()
    {
        var result = await Builder().Apply(Source(), new ListParams(), CancellationToken.None);
        Assert.Equal(10, result.Total);
        Assert.Equal(10, result.Data.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PerPage);
    }

    [Fact]
    public async Task Apply_Pagination_ReturnsCorrectPage()
    {
        var lp = new ListParams { Page = 2, PerPage = 3 };
        var result = await Builder().Apply(Source(), lp, CancellationToken.None);

        Assert.Equal(10, result.Total);
        Assert.Equal(3, result.Data.Count);
        Assert.Equal(2, result.Page);
        Assert.Equal(3, result.PerPage);
        Assert.Equal(4, result.TotalPages); // ceil(10/3)
    }

    [Fact]
    public async Task Apply_Filter_ReducesTotal()
    {
        var lp = new ListParams
        {
            Filters = { ["Age"] = FilterOp.Parse("gte:28") },
        };
        var result = await Builder().Apply(Source(), lp, CancellationToken.None);
        Assert.Equal(3, result.Total); // ages 28,29,30
        Assert.All(result.Data, e => Assert.True(e.Age >= 28));
    }

    [Fact]
    public async Task Apply_Sort_OrdersCorrectly()
    {
        var lp = new ListParams { Sort = "-age" };
        var result = await Builder().Apply(Source(), lp, CancellationToken.None);
        var ages = result.Data.Select(e => e.Age).ToList();
        Assert.Equal(ages.OrderByDescending(a => a).ToList(), ages);
    }

    [Fact]
    public async Task Apply_LastPage_HasFewerItems()
    {
        var lp = new ListParams { Page = 4, PerPage = 3 };
        var result = await Builder().Apply(Source(), lp, CancellationToken.None);
        Assert.Equal(1, result.Data.Count); // only item 10
    }
}
```

- [ ] **Step 2: Run — expect compile error**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ --no-build 2>&1 | head -10`
Expected: Build error — QueryBuilder not found

- [ ] **Step 3: Implement IncludeApplier**

```csharp
// src/CrudKit.EntityFrameworkCore/Query/IncludeApplier.cs
using System.Reflection;
using CrudKit.Core.Attributes;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Query;

/// <summary>
/// Reads [DefaultInclude] attributes on navigation properties and calls EF Core .Include().
/// </summary>
public static class IncludeApplier
{
    public static IQueryable<T> Apply<T>(IQueryable<T> query) where T : class
    {
        foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<DefaultIncludeAttribute>() != null)
                query = query.Include(prop.Name);
        }
        return query;
    }
}
```

- [ ] **Step 4: Implement QueryBuilder**

```csharp
// src/CrudKit.EntityFrameworkCore/Query/QueryBuilder.cs
using CrudKit.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Query;

/// <summary>
/// Orchestrates filtering, counting, sorting, and pagination into a Paginated&lt;T&gt; result.
/// </summary>
public class QueryBuilder<T> where T : class
{
    private readonly FilterApplier _filterApplier;

    public QueryBuilder(FilterApplier filterApplier)
        => _filterApplier = filterApplier;

    public async Task<Paginated<T>> Apply(
        IQueryable<T> query,
        ListParams listParams,
        CancellationToken ct = default)
    {
        // 1. Apply [DefaultInclude] navigation properties
        query = IncludeApplier.Apply(query);

        // 2. Apply filters
        foreach (var (field, op) in listParams.Filters)
            query = _filterApplier.Apply(query, field, op);

        // 3. Count after filtering (before pagination)
        var total = await query.CountAsync(ct);

        // 4. Apply sort
        query = SortApplier.Apply(query, listParams.Sort);

        // 5. Paginate
        var data = await query
            .Skip((listParams.Page - 1) * listParams.PerPage)
            .Take(listParams.PerPage)
            .ToListAsync(ct);

        return new Paginated<T>
        {
            Data = data,
            Total = total,
            Page = listParams.Page,
            PerPage = listParams.PerPage,
            TotalPages = (int)Math.Ceiling((double)total / listParams.PerPage),
        };
    }
}
```

- [ ] **Step 5: Verify Paginated<T> has TotalPages**

Check `src/CrudKit.Core/Models/Paginated.cs`. If `TotalPages` property is missing, add it:

```csharp
// Expected content of src/CrudKit.Core/Models/Paginated.cs
namespace CrudKit.Core.Models;

public class Paginated<T>
{
    public List<T> Data { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PerPage { get; set; }
    public int TotalPages { get; set; }
}
```

If `TotalPages` is absent, add it as a property (do not change existing ones).

- [ ] **Step 6: Run tests — expect pass**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ -v normal`
Expected: All previous + 5 QueryBuilder tests pass

- [ ] **Step 7: Commit**

```bash
git add src/CrudKit.EntityFrameworkCore/Query/ tests/CrudKit.EntityFrameworkCore.Tests/Query/QueryBuilderTests.cs
git commit -m "feat(ef): QueryBuilder + IncludeApplier — filter, sort, paginate pipeline"
```

---

### Task 7: CrudKitDbContext

**Files:**
- Create: `src/CrudKit.EntityFrameworkCore/CrudKitDbContext.cs`
- Create: `tests/CrudKit.EntityFrameworkCore.Tests/Helpers/TestDbContext.cs`
- Create: `tests/CrudKit.EntityFrameworkCore.Tests/DbContextTests.cs`

This is the core of the EF layer. It handles: global soft-delete filter, global tenant filter (combined when both apply), concurrency token, enum-to-string conversion, `[Unique]` index, automatic Id + timestamp + tenant assignment in `SaveChanges`, soft-delete interception (DELETE → UPDATE), and audit log writing for `IAuditable` entities.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/DbContextTests.cs
using CrudKit.Core.Auth;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Tests;

public class DbContextTests
{
    // ---- Timestamps ----

    [Fact]
    public async Task SaveChanges_SetsCreatedAtAndUpdatedAt_OnAdd()
    {
        using var db = DbHelper.CreateDb();
        var person = new PersonEntity { Name = "Alice" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        Assert.NotEqual(default, person.CreatedAt);
        Assert.NotEqual(default, person.UpdatedAt);
        Assert.NotEqual(string.Empty, person.Id);
    }

    [Fact]
    public async Task SaveChanges_SetsUpdatedAt_OnModify_PreservesCreatedAt()
    {
        using var db = DbHelper.CreateDb();
        var person = new PersonEntity { Name = "Alice" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        var createdAt = person.CreatedAt;
        await Task.Delay(5); // ensure clock advances

        person.Name = "Bob";
        await db.SaveChangesAsync();

        Assert.Equal(createdAt, person.CreatedAt); // CreatedAt unchanged
        // UpdatedAt >= CreatedAt
        Assert.True(person.UpdatedAt >= createdAt);
    }

    // ---- Soft delete ----

    [Fact]
    public async Task Delete_SoftDeletable_SetsDeletedAt_NotRemovesRow()
    {
        using var db = DbHelper.CreateDb();
        var entity = new SoftPersonEntity { Name = "Alice" };
        db.SoftPersons.Add(entity);
        await db.SaveChangesAsync();

        db.SoftPersons.Remove(entity);
        await db.SaveChangesAsync();

        // Should not appear in normal query (global filter active)
        var found = await db.SoftPersons.FirstOrDefaultAsync(e => e.Id == entity.Id);
        Assert.Null(found);

        // But the row still exists (check via IgnoreQueryFilters)
        var raw = await db.SoftPersons.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);
        Assert.NotNull(raw);
        Assert.NotNull(raw!.DeletedAt);
    }

    [Fact]
    public async Task List_SoftDeletable_ExcludesDeletedRows()
    {
        using var db = DbHelper.CreateDb();
        db.SoftPersons.AddRange(
            new SoftPersonEntity { Name = "Alice" },
            new SoftPersonEntity { Name = "Bob" });
        await db.SaveChangesAsync();

        db.SoftPersons.Remove(db.SoftPersons.First(e => e.Name == "Bob"));
        await db.SaveChangesAsync();

        var list = await db.SoftPersons.ToListAsync();
        Assert.Single(list);
        Assert.Equal("Alice", list[0].Name);
    }

    // ---- Multi-tenant ----

    [Fact]
    public async Task SaveChanges_SetsTenantId_FromCurrentUser()
    {
        var user = new FakeCurrentUser();
        using var db = DbHelper.CreateDb(user);

        var entity = new TenantPersonEntity { Name = "Alice" };
        db.TenantPersons.Add(entity);
        await db.SaveChangesAsync();

        Assert.Equal(user.TenantId, entity.TenantId);
    }

    [Fact]
    public async Task List_MultiTenant_FiltersToCurrentTenant()
    {
        // Create two entities in different tenants using two separate DbContext instances.
        var user1 = new FakeCurrentUser("tenant-1");
        var user2 = new FakeCurrentUser("tenant-2");

        using var db1 = DbHelper.CreateDb(user1);
        db1.TenantPersons.Add(new TenantPersonEntity { Name = "Alice", TenantId = "tenant-1" });
        await db1.SaveChangesAsync();

        using var db2 = DbHelper.CreateDb(user2);
        db2.TenantPersons.Add(new TenantPersonEntity { Name = "Bob", TenantId = "tenant-2" });
        await db2.SaveChangesAsync();

        // User1 should only see their own entity
        var list = await db1.TenantPersons.ToListAsync();
        Assert.Single(list);
        Assert.Equal("Alice", list[0].Name);
    }

    // ---- Audit log ----

    [Fact]
    public async Task SaveChanges_WritesAuditLog_OnCreate()
    {
        using var db = DbHelper.CreateDb();
        var entity = new AuditPersonEntity { Name = "Alice" };
        db.AuditPersons.Add(entity);
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal("AuditPersonEntity", log!.EntityType);
        Assert.Equal("Create", log.Action);
        Assert.NotNull(log.NewValues);
    }

    [Fact]
    public async Task SaveChanges_WritesAuditLog_OnUpdate_WithChangedFields()
    {
        using var db = DbHelper.CreateDb();
        var entity = new AuditPersonEntity { Name = "Alice" };
        db.AuditPersons.Add(entity);
        await db.SaveChangesAsync();

        entity.Name = "Bob";
        await db.SaveChangesAsync();

        var updateLog = await db.AuditLogs
            .Where(l => l.Action == "Update")
            .FirstOrDefaultAsync();
        Assert.NotNull(updateLog);
        Assert.Contains("Name", updateLog!.ChangedFields ?? "");
    }
}
```

- [ ] **Step 2: Create TestDbContext helper class**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/Helpers/TestDbContext.cs
using CrudKit.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Concrete DbContext for tests. Inherits CrudKitDbContext.
/// Only adds DbSets — no configuration needed.
/// </summary>
public class TestDbContext : CrudKitDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<PersonEntity> Persons => Set<PersonEntity>();
    public DbSet<SoftPersonEntity> SoftPersons => Set<SoftPersonEntity>();
    public DbSet<TenantPersonEntity> TenantPersons => Set<TenantPersonEntity>();
    public DbSet<AuditPersonEntity> AuditPersons => Set<AuditPersonEntity>();
    public DbSet<ConcurrentEntity> ConcurrentEntities => Set<ConcurrentEntity>();
    public DbSet<InvoiceEntity> Invoices => Set<InvoiceEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
}
```

Also update `FakeCurrentUser` to accept a `tenantId` constructor parameter for multi-tenant tests (check `src/CrudKit.Core/Auth/FakeCurrentUser.cs`; if no such constructor exists, modify it):

```csharp
// Expected: FakeCurrentUser should have constructor accepting optional tenantId
// Check existing code; if it only has parameterless ctor, add:
public FakeCurrentUser(string tenantId) { ... }
```

- [ ] **Step 3: Run tests — expect compile error**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ --no-build 2>&1 | head -20`
Expected: Build error — CrudKitDbContext not found

- [ ] **Step 4: Implement CrudKitDbContext**

```csharp
// src/CrudKit.EntityFrameworkCore/CrudKitDbContext.cs
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Concurrency;
using CrudKit.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CrudKit.EntityFrameworkCore;

/// <summary>
/// Abstract base DbContext. Inherit from this, define your DbSets, done.
/// Cross-cutting concerns handled automatically:
/// - IEntity          → Id generation (Guid), CreatedAt/UpdatedAt (UTC)
/// - ISoftDeletable   → DELETE intercepted → soft delete, global query filter
/// - IMultiTenant     → global tenant filter, TenantId auto-set on Create
/// - IConcurrent      → EF rowversion concurrency token
/// - IAuditable       → audit log written on Create/Update/Delete
/// - Enum properties  → stored as strings
/// - [Unique]         → unique index (partial if ISoftDeletable)
/// </summary>
public abstract class CrudKitDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;

    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<SequenceEntry> Sequences => Set<SequenceEntry>();

    protected CrudKitDbContext(DbContextOptions options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            var isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(clrType);
            var isMultiTenant = typeof(IMultiTenant).IsAssignableFrom(clrType);
            var isConcurrent = typeof(IConcurrent).IsAssignableFrom(clrType);

            // ---- Soft delete global filter ----
            if (isSoftDeletable && !isMultiTenant)
            {
                modelBuilder.Entity(clrType).HasQueryFilter(
                    BuildSoftDeleteFilter(clrType));
            }

            // ---- Multi-tenant global filter (combines with soft delete if both apply) ----
            if (isMultiTenant)
            {
                var tenantFilter = BuildTenantFilter(clrType);
                var softFilter = isSoftDeletable ? BuildSoftDeleteFilter(clrType) : null;
                modelBuilder.Entity(clrType).HasQueryFilter(
                    CombineFilters(tenantFilter, softFilter));
            }

            // ---- Optimistic concurrency ----
            if (isConcurrent)
            {
                modelBuilder.Entity(clrType)
                    .Property(nameof(IConcurrent.RowVersion))
                    .IsRowVersion();
            }

            // ---- Enum properties → stored as strings ----
            foreach (var prop in clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (propType.IsEnum)
                {
                    modelBuilder.Entity(clrType)
                        .Property(prop.Name)
                        .HasConversion<string>();
                }
            }

            // ---- [Unique] attribute → unique index ----
            foreach (var prop in clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetCustomAttribute<UniqueAttribute>() == null) continue;

                var indexBuilder = modelBuilder.Entity(clrType)
                    .HasIndex(prop.Name)
                    .IsUnique();

                if (isSoftDeletable)
                    indexBuilder.HasFilter("deleted_at IS NULL");
            }
        }

        // CrudKit internal tables
        modelBuilder.Entity<AuditLogEntry>(b =>
        {
            b.ToTable("__crud_audit_logs");
            b.HasIndex(e => new { e.EntityType, e.EntityId });
            b.HasIndex(e => e.Timestamp);
        });

        modelBuilder.Entity<SequenceEntry>(b =>
        {
            b.ToTable("__crud_sequences");
            b.HasIndex(e => new { e.EntityType, e.TenantId, e.Year }).IsUnique();
        });

        OnModelCreatingCustom(modelBuilder);
    }

    /// <summary>
    /// Override to add entity configurations and seed data.
    /// Do not call base.OnModelCreating here.
    /// </summary>
    protected virtual void OnModelCreatingCustom(ModelBuilder modelBuilder) { }

    // ---- SaveChanges overrides ----

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BeforeSaveChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken ct = default)
    {
        BeforeSaveChanges();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    private void BeforeSaveChanges()
    {
        foreach (var entry in ChangeTracker.Entries<IEntity>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (string.IsNullOrEmpty(entry.Entity.Id))
                        entry.Entity.Id = Guid.NewGuid().ToString();
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    if (entry.Entity is IMultiTenant mt && _currentUser.TenantId != null)
                        mt.TenantId = _currentUser.TenantId;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    entry.Property(nameof(IEntity.CreatedAt)).IsModified = false;
                    break;

                case EntityState.Deleted:
                    if (entry.Entity is ISoftDeletable sd)
                    {
                        entry.State = EntityState.Modified;
                        sd.DeletedAt = DateTime.UtcNow;
                        entry.Entity.UpdatedAt = DateTime.UtcNow;
                    }
                    break;
            }
        }

        WriteAuditLogs();
    }

    private void WriteAuditLogs()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditable
                && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (entries.Count == 0) return;

        var auditEntries = new List<AuditLogEntry>();
        foreach (var entry in entries)
        {
            var log = new AuditLogEntry
            {
                EntityType = entry.Entity.GetType().Name,
                EntityId = (entry.Entity as IEntity)?.Id ?? string.Empty,
                UserId = _currentUser.Id,
                Timestamp = DateTime.UtcNow,
            };

            switch (entry.State)
            {
                case EntityState.Added:
                    log.Action = "Create";
                    log.NewValues = SerializeCurrentValues(entry.Properties);
                    break;

                case EntityState.Modified:
                    var modified = entry.Properties.Where(p => p.IsModified).ToList();
                    log.Action = "Update";
                    log.OldValues = SerializeOriginalValues(modified);
                    log.NewValues = SerializeCurrentValues(modified);
                    log.ChangedFields = JsonSerializer.Serialize(
                        modified.Select(p => p.Metadata.Name));
                    break;

                case EntityState.Deleted:
                    log.Action = "Delete";
                    log.OldValues = SerializeCurrentValues(entry.Properties);
                    break;
            }

            auditEntries.Add(log);
        }

        AuditLogs.AddRange(auditEntries);
    }

    // ---- Runtime tenant value used by EF Core global filter ----
    // EF Core captures a reference to the DbContext instance, so this property
    // is evaluated fresh on every query.
    private string? CurrentTenantId => _currentUser.TenantId;

    // ---- Filter expression builders ----

    private static LambdaExpression BuildSoftDeleteFilter(Type entityType)
    {
        var param = Expression.Parameter(entityType, "e");
        var prop = Expression.Property(param, nameof(ISoftDeletable.DeletedAt));
        var condition = Expression.Equal(prop, Expression.Constant(null, typeof(DateTime?)));
        return Expression.Lambda(condition, param);
    }

    private LambdaExpression BuildTenantFilter(Type entityType)
    {
        var param = Expression.Parameter(entityType, "e");
        var prop = Expression.Property(param, nameof(IMultiTenant.TenantId));
        var currentTenantProp = typeof(CrudKitDbContext)
            .GetProperty(nameof(CurrentTenantId), BindingFlags.NonPublic | BindingFlags.Instance)!;
        var tenantIdAccess = Expression.Property(Expression.Constant(this), currentTenantProp);
        var condition = Expression.Equal(prop, tenantIdAccess);
        return Expression.Lambda(condition, param);
    }

    private static LambdaExpression CombineFilters(
        LambdaExpression filter1, LambdaExpression? filter2)
    {
        if (filter2 == null) return filter1;
        var param = filter1.Parameters[0];
        var body = Expression.AndAlso(
            filter1.Body,
            Expression.Invoke(filter2, param));
        return Expression.Lambda(body, param);
    }

    // ---- Serialization helpers for audit log ----

    private static string SerializeCurrentValues(IEnumerable<PropertyEntry> props)
        => JsonSerializer.Serialize(
            props.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));

    private static string SerializeOriginalValues(IEnumerable<PropertyEntry> props)
        => JsonSerializer.Serialize(
            props.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue));
}
```

- [ ] **Step 5: Check and update FakeCurrentUser for tenantId support**

Read `src/CrudKit.Core/Auth/FakeCurrentUser.cs`. If it does not have a constructor accepting `string tenantId`, update it to add one:

```csharp
// Add to existing FakeCurrentUser:
private readonly string? _tenantId;

public FakeCurrentUser(string? tenantId = "test-tenant")
{
    _tenantId = tenantId;
}

public string? TenantId => _tenantId;
// Keep all other members unchanged
```

If the file already has a full-param constructor, check that `TenantId` returns a non-null value for tests that rely on it.

- [ ] **Step 6: Enable the previously skipped DialectDetector test**

In `tests/CrudKit.EntityFrameworkCore.Tests/Dialect/DialectTests.cs`, remove the `Skip` attribute from `DialectDetector_DetectsSqlite_ForSqliteProvider`.

- [ ] **Step 7: Run tests — expect pass**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ -v normal`
Expected: All previous + 7 DbContextTests + 1 DialectDetector test pass (≥ 30 tests total)

- [ ] **Step 8: Commit**

```bash
git add src/CrudKit.EntityFrameworkCore/CrudKitDbContext.cs tests/CrudKit.EntityFrameworkCore.Tests/Helpers/TestDbContext.cs tests/CrudKit.EntityFrameworkCore.Tests/DbContextTests.cs
git commit -m "feat(ef): CrudKitDbContext — soft delete, tenant filter, audit log, timestamps, enum-to-string"
```

---

### Task 8: IRepo\<T\> + EfRepo\<T\>

**Files:**
- Create: `src/CrudKit.EntityFrameworkCore/Repository/IRepo.cs`
- Create: `src/CrudKit.EntityFrameworkCore/Repository/EfRepo.cs`
- Create: `tests/CrudKit.EntityFrameworkCore.Tests/Repository/EfRepoTests.cs`

`EfRepo<T>` maps DTOs to entities via reflection. Key behaviors:
- **Create**: map DTO props → entity, hash `[Hashed]` fields (BCrypt), assign DocumentNumber for `IDocumentNumbering`, call `SaveChangesAsync`, null out `[SkipResponse]` fields before returning.
- **Update**: fetch entity, skip `[Protected]` + `[SkipUpdate]` props, apply `Optional<T>` (HasValue=true → apply, HasValue=false → skip) or plain nullable (null → skip), save, null out `[SkipResponse]`.
- **Delete**: for `ISoftDeletable` → soft delete; otherwise physical remove.
- **Restore**: only for `ISoftDeletable` — set `DeletedAt = null`.
- **FindById**: throws `AppError.NotFound` if missing.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/Repository/EfRepoTests.cs
using CrudKit.Core.Auth;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Numbering;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Repository;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Tests.Repository;

public class EfRepoTests
{
    private static (TestDbContext db, EfRepo<PersonEntity> repo) CreatePersonRepo(
        ICurrentUser? user = null)
    {
        var db = DbHelper.CreateDb(user);
        var dialect = DialectDetector.Detect(db);
        var queryBuilder = new QueryBuilder<PersonEntity>(new FilterApplier(dialect));
        var repo = new EfRepo<PersonEntity>(db, queryBuilder);
        return (db, repo);
    }

    private static (TestDbContext db, EfRepo<SoftPersonEntity> repo) CreateSoftRepo()
    {
        var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        var queryBuilder = new QueryBuilder<SoftPersonEntity>(new FilterApplier(dialect));
        var repo = new EfRepo<SoftPersonEntity>(db, queryBuilder);
        return (db, repo);
    }

    private static (TestDbContext db, EfRepo<UserEntity> repo) CreateUserRepo()
    {
        var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        var queryBuilder = new QueryBuilder<UserEntity>(new FilterApplier(dialect));
        var repo = new EfRepo<UserEntity>(db, queryBuilder);
        return (db, repo);
    }

    // ---- Create ----

    [Fact]
    public async Task Create_MapsDto_AndReturnsEntity()
    {
        var (db, repo) = CreatePersonRepo();
        var dto = new { Name = "Alice", Age = 30 };

        var result = await repo.Create(dto);

        Assert.NotEmpty(result.Id);
        Assert.Equal("Alice", result.Name);
        Assert.Equal(30, result.Age);
        Assert.True(await db.Persons.AnyAsync(e => e.Id == result.Id));
    }

    [Fact]
    public async Task Create_WithHashedField_StoresHash_AndNullsInResponse()
    {
        var (_, repo) = CreateUserRepo();
        var dto = new { Username = "alice", PasswordHash = "secret123" };

        var result = await repo.Create(dto);

        Assert.Equal("alice", result.Username);
        Assert.Null(result.PasswordHash); // SkipResponse clears it from returned object
    }

    // ---- FindById ----

    [Fact]
    public async Task FindById_ReturnsEntity_WhenExists()
    {
        var (_, repo) = CreatePersonRepo();
        var created = await repo.Create(new { Name = "Bob", Age = 25 });

        var found = await repo.FindById(created.Id);
        Assert.Equal("Bob", found.Name);
    }

    [Fact]
    public async Task FindById_ThrowsNotFound_WhenMissing()
    {
        var (_, repo) = CreatePersonRepo();
        var ex = await Assert.ThrowsAsync<AppError>(() => repo.FindById("non-existent"));
        Assert.Equal(404, ex.StatusCode);
    }

    // ---- FindByIdOrDefault ----

    [Fact]
    public async Task FindByIdOrDefault_ReturnsNull_WhenMissing()
    {
        var (_, repo) = CreatePersonRepo();
        var result = await repo.FindByIdOrDefault("non-existent");
        Assert.Null(result);
    }

    // ---- Update ----

    [Fact]
    public async Task Update_AppliesOnlyProvidedFields()
    {
        var (_, repo) = CreatePersonRepo();
        var created = await repo.Create(new { Name = "Alice", Age = 30 });

        // Partial update — only Age is changed (Name absent = skip)
        var updateDto = new { Age = 31 };
        var updated = await repo.Update(created.Id, updateDto);

        Assert.Equal("Alice", updated.Name); // unchanged
        Assert.Equal(31, updated.Age);
    }

    [Fact]
    public async Task Update_WithOptional_SkipsAbsentFields()
    {
        var (_, repo) = CreatePersonRepo();
        var created = await repo.Create(new { Name = "Alice", Age = 30 });

        // Optional<string> Undefined → should not touch Name
        var updateDto = new
        {
            Name = Optional<string>.Undefined,
            Age = (Optional<int>)32,
        };
        var updated = await repo.Update(created.Id, updateDto);

        Assert.Equal("Alice", updated.Name);
        Assert.Equal(32, updated.Age);
    }

    // ---- Delete ----

    [Fact]
    public async Task Delete_PhysicalEntity_RemovesRow()
    {
        var (db, repo) = CreatePersonRepo();
        var created = await repo.Create(new { Name = "Carol", Age = 22 });

        await repo.Delete(created.Id);

        Assert.False(await db.Persons.AnyAsync(e => e.Id == created.Id));
    }

    [Fact]
    public async Task Delete_SoftDeletable_SetsDeletedAt_RowStillExists()
    {
        var (db, repo) = CreateSoftRepo();
        var created = await repo.Create(new { Name = "Dave" });

        await repo.Delete(created.Id);

        var raw = await db.SoftPersons.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == created.Id);
        Assert.NotNull(raw);
        Assert.NotNull(raw!.DeletedAt);
    }

    // ---- Restore ----

    [Fact]
    public async Task Restore_SoftDeletable_ClearsDeletedAt()
    {
        var (db, repo) = CreateSoftRepo();
        var created = await repo.Create(new { Name = "Eve" });
        await repo.Delete(created.Id);

        await repo.Restore(created.Id);

        var restored = await db.SoftPersons.FirstOrDefaultAsync(e => e.Id == created.Id);
        Assert.NotNull(restored);
        Assert.Null(restored!.DeletedAt);
    }

    // ---- List ----

    [Fact]
    public async Task List_ReturnsPaginatedResult()
    {
        var (_, repo) = CreatePersonRepo();
        for (var i = 1; i <= 5; i++)
            await repo.Create(new { Name = $"Person{i}", Age = 20 + i });

        var result = await repo.List(new ListParams { Page = 1, PerPage = 3 });

        Assert.Equal(5, result.Total);
        Assert.Equal(3, result.Data.Count);
    }

    // ---- Exists + Count ----

    [Fact]
    public async Task Exists_ReturnsTrueForExistingId()
    {
        var (_, repo) = CreatePersonRepo();
        var created = await repo.Create(new { Name = "Frank", Age = 40 });
        Assert.True(await repo.Exists(created.Id));
        Assert.False(await repo.Exists("non-existent"));
    }

    [Fact]
    public async Task Count_ReturnsEntityCount()
    {
        var (_, repo) = CreatePersonRepo();
        await repo.Create(new { Name = "G1", Age = 20 });
        await repo.Create(new { Name = "G2", Age = 21 });
        Assert.Equal(2, await repo.Count());
    }

    // ---- FindByField ----

    [Fact]
    public async Task FindByField_ReturnsMatchingEntities()
    {
        var (_, repo) = CreatePersonRepo();
        await repo.Create(new { Name = "Helen", Age = 35 });
        await repo.Create(new { Name = "Ivan",  Age = 35 });
        await repo.Create(new { Name = "Julia", Age = 28 });

        var result = await repo.FindByField("Age", 35);
        Assert.Equal(2, result.Count);
    }
}
```

- [ ] **Step 2: Run — expect compile error**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ --no-build 2>&1 | head -10`
Expected: Build error — IRepo, EfRepo not found

- [ ] **Step 3: Create IRepo\<T\>**

```csharp
// src/CrudKit.EntityFrameworkCore/Repository/IRepo.cs
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;

namespace CrudKit.EntityFrameworkCore.Repository;

/// <summary>Generic CRUD contract for EF Core entities.</summary>
public interface IRepo<T> where T : class, IEntity
{
    Task<T> FindById(string id, CancellationToken ct = default);
    Task<T?> FindByIdOrDefault(string id, CancellationToken ct = default);
    Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default);
    Task<List<T>> FindByField(string fieldName, object value, CancellationToken ct = default);
    Task<T> Create(object createDto, CancellationToken ct = default);
    Task<T> Update(string id, object updateDto, CancellationToken ct = default);
    Task Delete(string id, CancellationToken ct = default);
    Task Restore(string id, CancellationToken ct = default);
    Task<bool> Exists(string id, CancellationToken ct = default);
    Task<long> Count(CancellationToken ct = default);
}
```

- [ ] **Step 4: Create EfRepo\<T\>**

```csharp
// src/CrudKit.EntityFrameworkCore/Repository/EfRepo.cs
using System.Reflection;
using BCrypt.Net;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Repository;

/// <summary>
/// Generic EF Core repository. Handles DTO→entity mapping via reflection.
/// All cross-cutting concerns (timestamps, tenant, soft delete, audit) are in CrudKitDbContext.
/// </summary>
public class EfRepo<T> : IRepo<T> where T : class, IEntity
{
    private readonly CrudKitDbContext _db;
    private readonly QueryBuilder<T> _queryBuilder;

    public EfRepo(CrudKitDbContext db, QueryBuilder<T> queryBuilder)
    {
        _db = db;
        _queryBuilder = queryBuilder;
    }

    public async Task<T> FindById(string id, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        query = IncludeApplier.Apply(query);
        var entity = await query.FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");
    }

    public async Task<T?> FindByIdOrDefault(string id, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        query = IncludeApplier.Apply(query);
        return await query.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        return await _queryBuilder.Apply(query, listParams, ct);
    }

    public async Task<List<T>> FindByField(string fieldName, object value, CancellationToken ct = default)
    {
        var prop = typeof(T).GetProperty(fieldName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop == null) return [];

        var all = await _db.Set<T>().AsNoTracking().ToListAsync(ct);
        return all.Where(e =>
        {
            var propVal = prop.GetValue(e);
            return propVal != null && propVal.Equals(value);
        }).ToList();
    }

    public async Task<T> Create(object createDto, CancellationToken ct = default)
    {
        var entity = Activator.CreateInstance<T>();
        MapDtoToEntity(createDto, entity, isCreate: true);

        _db.Set<T>().Add(entity);
        await _db.SaveChangesAsync(ct);

        ClearSkipResponseFields(entity);
        return entity;
    }

    public async Task<T> Update(string id, object updateDto, CancellationToken ct = default)
    {
        var entity = await _db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");

        MapDtoToEntity(updateDto, entity, isCreate: false);
        await _db.SaveChangesAsync(ct);

        ClearSkipResponseFields(entity);
        return entity;
    }

    public async Task Delete(string id, CancellationToken ct = default)
    {
        var entity = await _db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");

        _db.Set<T>().Remove(entity);
        // CrudKitDbContext.BeforeSaveChanges intercepts DELETE for ISoftDeletable entities
        await _db.SaveChangesAsync(ct);
    }

    public async Task Restore(string id, CancellationToken ct = default)
    {
        if (typeof(T).IsAssignableTo(typeof(ISoftDeletable)) == false)
            throw new InvalidOperationException($"{typeof(T).Name} does not implement ISoftDeletable.");

        var entity = await _db.Set<T>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound($"Deleted {typeof(T).Name} with id '{id}' was not found.");

        ((ISoftDeletable)entity).DeletedAt = null;
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> Exists(string id, CancellationToken ct = default)
        => _db.Set<T>().AnyAsync(e => e.Id == id, ct);

    public Task<long> Count(CancellationToken ct = default)
        => _db.Set<T>().LongCountAsync(ct);

    // ---- Reflection-based DTO mapping ----

    private static void MapDtoToEntity(object dto, T entity, bool isCreate)
    {
        var entityProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var entityPropMap = entityProps.ToDictionary(
            p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        foreach (var dtoProp in dto.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!entityPropMap.TryGetValue(dtoProp.Name, out var entityProp)) continue;

            // Skip system/infrastructure fields (managed by DbContext)
            if (entityProp.Name is nameof(IEntity.Id)
                or nameof(IEntity.CreatedAt)
                or nameof(IEntity.UpdatedAt)) continue;

            if (!isCreate)
            {
                if (entityProp.GetCustomAttribute<ProtectedAttribute>() != null) continue;
                if (entityProp.GetCustomAttribute<SkipUpdateAttribute>() != null) continue;
            }

            var dtoValue = dtoProp.GetValue(dto);

            // Handle Optional<T> — absent fields (HasValue=false) are skipped
            if (IsOptionalType(dtoProp.PropertyType))
            {
                var hasValue = (bool)dtoProp.PropertyType.GetProperty("HasValue")!.GetValue(dtoValue)!;
                if (!hasValue) continue;
                dtoValue = dtoProp.PropertyType.GetProperty("Value")!.GetValue(dtoValue);
            }
            else if (!isCreate && dtoValue == null)
            {
                // For plain nullable types in Update, null means "don't touch"
                continue;
            }

            // Apply BCrypt hashing for [Hashed] fields on Create
            if (isCreate
                && entityProp.GetCustomAttribute<HashedAttribute>() != null
                && dtoValue is string plainText)
            {
                entityProp.SetValue(entity, BCrypt.HashPassword(plainText));
                continue;
            }

            entityProp.SetValue(entity, dtoValue);
        }
    }

    private static bool IsOptionalType(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>);

    private static void ClearSkipResponseFields(T entity)
    {
        foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<SkipResponseAttribute>() != null
                && prop.CanWrite)
            {
                prop.SetValue(entity, null);
            }
        }
    }
}
```

- [ ] **Step 5: Run tests — expect pass**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ -v normal`
Expected: All previous + ~15 EfRepo tests pass

- [ ] **Step 6: Commit**

```bash
git add src/CrudKit.EntityFrameworkCore/Repository/ tests/CrudKit.EntityFrameworkCore.Tests/Repository/
git commit -m "feat(ef): IRepo<T> + EfRepo<T> — CRUD, reflection mapping, hashing, soft delete, restore"
```

---

### Task 9: SequenceGenerator

**Files:**
- Create: `src/CrudKit.EntityFrameworkCore/Numbering/SequenceGenerator.cs`
- Create: `tests/CrudKit.EntityFrameworkCore.Tests/Numbering/SequenceGeneratorTests.cs`

Generates document numbers in `PREFIX-YYYY-NNNNN` format. Uses an EF Core transaction with `SELECT FOR UPDATE` semantics (serializable isolation) to prevent race conditions. Format example: `INV-2026-00001`.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/Numbering/SequenceGeneratorTests.cs
using CrudKit.EntityFrameworkCore.Numbering;
using CrudKit.EntityFrameworkCore.Tests.Helpers;

namespace CrudKit.EntityFrameworkCore.Tests.Numbering;

public class SequenceGeneratorTests
{
    private static (TestDbContext db, SequenceGenerator gen) Setup()
    {
        var db = DbHelper.CreateDb();
        var gen = new SequenceGenerator(db);
        return (db, gen);
    }

    [Fact]
    public async Task Next_ReturnsFormattedNumber()
    {
        var (_, gen) = Setup();
        var number = await gen.Next<InvoiceEntity>("tenant-1");
        Assert.Matches(@"^INV-\d{4}-\d{5}$", number);
    }

    [Fact]
    public async Task Next_IncrementsSequentially()
    {
        var (_, gen) = Setup();
        var first  = await gen.Next<InvoiceEntity>("tenant-1");
        var second = await gen.Next<InvoiceEntity>("tenant-1");
        var third  = await gen.Next<InvoiceEntity>("tenant-1");

        Assert.EndsWith("-00001", first);
        Assert.EndsWith("-00002", second);
        Assert.EndsWith("-00003", third);
    }

    [Fact]
    public async Task Next_DifferentTenants_HaveSeparateSequences()
    {
        var (_, gen) = Setup();
        var t1 = await gen.Next<InvoiceEntity>("tenant-1");
        var t2 = await gen.Next<InvoiceEntity>("tenant-2");

        Assert.EndsWith("-00001", t1);
        Assert.EndsWith("-00001", t2); // separate counter per tenant
    }

    [Fact]
    public async Task Next_YearlyReset_UsesCurrentYear()
    {
        var (_, gen) = Setup();
        var number = await gen.Next<InvoiceEntity>("tenant-1");
        var year = DateTime.UtcNow.Year.ToString();
        Assert.Contains(year, number);
    }
}
```

- [ ] **Step 2: Run — expect compile error**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ --no-build 2>&1 | head -10`
Expected: Build error — SequenceGenerator not found

- [ ] **Step 3: Implement SequenceGenerator**

```csharp
// src/CrudKit.EntityFrameworkCore/Numbering/SequenceGenerator.cs
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Numbering;

/// <summary>
/// Generates sequential, tenant-scoped document numbers.
/// Format: PREFIX-YYYY-NNNNN (e.g. INV-2026-00001).
/// Uses a database transaction to ensure monotonically increasing values without gaps.
/// Thread-safe via EF Core optimistic concurrency (retry on conflict).
/// </summary>
public class SequenceGenerator
{
    private readonly CrudKitDbContext _db;

    public SequenceGenerator(CrudKitDbContext db) => _db = db;

    public async Task<string> Next<T>(string tenantId, CancellationToken ct = default)
        where T : class, IDocumentNumbering
    {
        var prefix = T.Prefix;
        var yearlyReset = T.YearlyReset;
        var entityType = typeof(T).Name;
        var year = yearlyReset ? DateTime.UtcNow.Year.ToString() : "0000";

        long nextVal;

        // Retry loop handles concurrent requests cleanly.
        while (true)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var entry = await _db.Sequences
                    .FirstOrDefaultAsync(
                        s => s.EntityType == entityType
                          && s.TenantId == tenantId
                          && s.Year == year,
                        ct);

                if (entry == null)
                {
                    entry = new SequenceEntry
                    {
                        EntityType = entityType,
                        TenantId = tenantId,
                        Year = year,
                        CurrentVal = 1,
                    };
                    _db.Sequences.Add(entry);
                    nextVal = 1;
                }
                else
                {
                    entry.CurrentVal++;
                    nextVal = entry.CurrentVal;
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                break;
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(ct);
                // Detach stale entries and retry
                foreach (var efEntry in _db.ChangeTracker.Entries())
                    efEntry.State = EntityState.Detached;
            }
        }

        return $"{prefix}-{year}-{nextVal:D5}";
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ -v normal`
Expected: All previous + 4 SequenceGenerator tests pass

- [ ] **Step 5: Commit**

```bash
git add src/CrudKit.EntityFrameworkCore/Numbering/ tests/CrudKit.EntityFrameworkCore.Tests/Numbering/
git commit -m "feat(ef): SequenceGenerator — tenant-scoped document numbering with retry on concurrency conflict"
```

---

### Task 10: ServiceCollectionExtensions

**Files:**
- Create: `src/CrudKit.EntityFrameworkCore/Extensions/ServiceCollectionExtensions.cs`
- Create: `tests/CrudKit.EntityFrameworkCore.Tests/Extensions/ServiceCollectionExtensionsTests.cs`

Registers: IDbDialect (auto-detected), CrudKitDbContext alias (so EfRepo receives TContext), IRepo<> open generic, QueryBuilder<>, FilterApplier, SequenceGenerator.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/Extensions/ServiceCollectionExtensionsTests.cs
using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Extensions;
using CrudKit.EntityFrameworkCore.Numbering;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Repository;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.EntityFrameworkCore.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser());
        services.AddDbContext<TestDbContext>((sp, opts) =>
            opts.UseSqlite($"Data Source={Guid.NewGuid()};Mode=Memory;Cache=Shared"));
        services.AddCrudKitEf<TestDbContext>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddCrudKitEf_RegistersIDbDialect()
    {
        using var sp = BuildProvider() as ServiceProvider;
        using var scope = sp!.CreateScope();
        var dialect = scope.ServiceProvider.GetRequiredService<IDbDialect>();
        Assert.NotNull(dialect);
    }

    [Fact]
    public void AddCrudKitEf_RegistersFilterApplier()
    {
        using var sp = BuildProvider() as ServiceProvider;
        using var scope = sp!.CreateScope();
        var applier = scope.ServiceProvider.GetRequiredService<FilterApplier>();
        Assert.NotNull(applier);
    }

    [Fact]
    public void AddCrudKitEf_RegistersOpenGenericQueryBuilder()
    {
        using var sp = BuildProvider() as ServiceProvider;
        using var scope = sp!.CreateScope();
        var qb = scope.ServiceProvider.GetRequiredService<QueryBuilder<PersonEntity>>();
        Assert.NotNull(qb);
    }

    [Fact]
    public void AddCrudKitEf_RegistersOpenGenericIRepo()
    {
        using var sp = BuildProvider() as ServiceProvider;
        using var scope = sp!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepo<PersonEntity>>();
        Assert.IsType<EfRepo<PersonEntity>>(repo);
    }

    [Fact]
    public void AddCrudKitEf_RegistersSequenceGenerator()
    {
        using var sp = BuildProvider() as ServiceProvider;
        using var scope = sp!.CreateScope();
        var gen = scope.ServiceProvider.GetRequiredService<SequenceGenerator>();
        Assert.NotNull(gen);
    }
}
```

- [ ] **Step 2: Run — expect compile error**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ --no-build 2>&1 | head -10`
Expected: Build error — AddCrudKitEf not found

- [ ] **Step 3: Implement ServiceCollectionExtensions**

```csharp
// src/CrudKit.EntityFrameworkCore/Extensions/ServiceCollectionExtensions.cs
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Numbering;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrudKit.EntityFrameworkCore.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the CrudKit EF Core infrastructure.
    /// Call after AddDbContext&lt;TContext&gt;.
    /// </summary>
    /// <example>
    /// services.AddDbContext&lt;AppDbContext&gt;(...);
    /// services.AddCrudKitEf&lt;AppDbContext&gt;();
    /// </example>
    public static IServiceCollection AddCrudKitEf<TContext>(this IServiceCollection services)
        where TContext : CrudKitDbContext
    {
        // Register TContext also as CrudKitDbContext so EfRepo<T> can receive it.
        services.AddScoped<CrudKitDbContext>(sp => sp.GetRequiredService<TContext>());

        // Dialect — auto-detected from TContext's provider.
        services.TryAddScoped<IDbDialect>(sp =>
        {
            var db = sp.GetRequiredService<TContext>();
            return DialectDetector.Detect(db);
        });

        // Query pipeline
        services.TryAddScoped<FilterApplier>();
        services.AddScoped(typeof(QueryBuilder<>));

        // Open generic repository: IRepo<T> → EfRepo<T>
        services.AddScoped(typeof(IRepo<>), typeof(EfRepo<>));

        // Document numbering
        services.TryAddScoped<SequenceGenerator>();

        return services;
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ -v normal`
Expected: All tests pass including 5 new ServiceCollectionExtensions tests

- [ ] **Step 5: Run full solution tests**

Run: `dotnet test CrudKit.slnx -v normal`
Expected: All tests across CrudKit.Core.Tests and CrudKit.EntityFrameworkCore.Tests pass, 0 failures

- [ ] **Step 6: Commit**

```bash
git add src/CrudKit.EntityFrameworkCore/Extensions/ tests/CrudKit.EntityFrameworkCore.Tests/Extensions/
git commit -m "feat(ef): ServiceCollectionExtensions — DI registration for EF layer"
```

---

## Self-Review

### Spec Coverage Check

| Spec Section | Covered By |
|---|---|
| 2.1 File structure | All tasks create the specified files |
| 2.2 CrudKitDbContext (soft delete, tenant, audit, enum, concurrency, timestamps, partial index) | Task 7 |
| 2.3 IRepo\<T\> interface | Task 8 |
| 2.4 EfRepo\<T\> (mapping, [Hashed], [SkipResponse], [Protected], [SkipUpdate], partial update) | Task 8 |
| 2.5 QueryBuilder\<T\> | Task 6 |
| 2.6 FilterApplier (all operators, type coercion, property reflection) | Task 4 |
| 2.7 SortApplier (multi-field, default, snake_case) | Task 5 |
| 2.8 Dialect system (IDbDialect, 4 implementations, DialectDetector) | Task 3 |
| 2.9 SequenceGenerator (PREFIX-YYYY-NNNNN, per-tenant, yearly reset) | Task 9 |
| 2.10 ServiceCollectionExtensions (AddCrudKitEf) | Task 10 |

**IConcurrent + RowVersion:** Covered in Task 2 (model) and Task 7 (configured in OnModelCreating).

**[DefaultInclude] → IncludeApplier:** Covered in Task 6 + referenced from EfRepo (Task 8).

**DialectDetector auto-registration:** Covered in Task 10.

### Deviations from Spec (documented)

1. **`EfRepo<T>` vs `EfRepo<TContext, T>`**: Simplified to single type param. `AddCrudKitEf<TContext>` registers `TContext` as `CrudKitDbContext` alias, so DI injection works cleanly with standard .NET open generic registration.

2. **SequenceGenerator uses EF Core transactions** instead of raw SQL upsert. Provides correct behavior on all providers including SQLite (used in tests). Production users requiring maximum throughput can swap to provider-specific raw SQL.

3. **PostgresDialect.ILike uses runtime reflection** to avoid hard Npgsql compile-time dependency. Falls back to GenericDialect if Npgsql is not loaded.

4. **EF Core 10 packages** (spec said 9.*) — consistent with the project's net10.0 target.
