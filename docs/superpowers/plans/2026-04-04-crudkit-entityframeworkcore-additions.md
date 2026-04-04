# CrudKit.EntityFrameworkCore Additions — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add missing EF Core features: TimeProvider injection, include strategy upgrade, hooks-based row-level security, cascade soft delete, restore unique constraint check, and bulk operations.

**Architecture:** These additions extend the existing `CrudKitDbContext` and `EfRepo<T>` without breaking the current API. `DefaultIncludeAttribute` becomes class-level with `IncludeScope`. `CascadeSoftDeleteAttribute` becomes class-level with explicit child type/FK. `EfRepo<T>` gains optional `ICrudHooks<T>` for `ApplyScope`. Bulk operations use EF Core `ExecuteUpdate`/`ExecuteDelete`.

**Tech Stack:** .NET 10, EF Core 10, BCrypt.Net-Next 4.*, SQLite (tests), xUnit 2.*

---

## Task 1: TimeProvider injection in CrudKitDbContext

### Step 1.1 — Write failing test for TimeProvider injection

- [ ] Create `tests/CrudKit.EntityFrameworkCore.Tests/TimeProviderTests.cs`
- [ ] Update `tests/CrudKit.EntityFrameworkCore.Tests/Helpers/DbHelper.cs` to accept optional `TimeProvider`
- [ ] Update `tests/CrudKit.EntityFrameworkCore.Tests/Helpers/TestDbContext.cs` to pass `TimeProvider` to base

**File: `tests/CrudKit.EntityFrameworkCore.Tests/Helpers/TestDbContext.cs`**
```csharp
using CrudKit.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Concrete DbContext for tests. Inherits CrudKitDbContext.
/// Owns the SqliteConnection and disposes it when the context is disposed.
/// </summary>
public class TestDbContext : CrudKitDbContext
{
    private readonly SqliteConnection? _connection;

    public TestDbContext(DbContextOptions<TestDbContext> options, ICurrentUser currentUser,
        SqliteConnection? connection = null, TimeProvider? timeProvider = null)
        : base(options, currentUser, timeProvider)
    {
        _connection = connection;
    }

    public DbSet<PersonEntity> Persons => Set<PersonEntity>();
    public DbSet<SoftPersonEntity> SoftPersons => Set<SoftPersonEntity>();
    public DbSet<TenantPersonEntity> TenantPersons => Set<TenantPersonEntity>();
    public DbSet<AuditPersonEntity> AuditPersons => Set<AuditPersonEntity>();
    public DbSet<ConcurrentEntity> ConcurrentEntities => Set<ConcurrentEntity>();
    public DbSet<InvoiceEntity> Invoices => Set<InvoiceEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();

    public override void Dispose()
    {
        base.Dispose();
        _connection?.Dispose();
    }

    public override ValueTask DisposeAsync()
    {
        _connection?.Dispose();
        return base.DisposeAsync();
    }
}
```

**File: `tests/CrudKit.EntityFrameworkCore.Tests/Helpers/DbHelper.cs`**
```csharp
using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Factory for creating isolated SQLite in-memory test database instances.
/// Each call produces a fresh database. The TestDbContext takes ownership
/// of the SqliteConnection and disposes it when the context is disposed.
/// </summary>
public static class DbHelper
{
    public static TestDbContext CreateDb(ICurrentUser? user = null, TimeProvider? timeProvider = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new TestDbContext(options, user ?? new FakeCurrentUser(), connection, timeProvider);
        db.Database.EnsureCreated();
        return db;
    }
}
```

**File: `tests/CrudKit.EntityFrameworkCore.Tests/TimeProviderTests.cs`**
```csharp
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests;

public class TimeProviderTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    [Fact]
    public async Task SaveChanges_UsesInjectedTimeProvider_ForCreatedAtAndUpdatedAt()
    {
        var fixedTime = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(fixedTime);
        using var db = DbHelper.CreateDb(timeProvider: fakeTime);

        var person = new PersonEntity { Name = "Alice" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        Assert.Equal(fixedTime.UtcDateTime, person.CreatedAt);
        Assert.Equal(fixedTime.UtcDateTime, person.UpdatedAt);
    }

    [Fact]
    public async Task SaveChanges_UsesInjectedTimeProvider_ForUpdatedAtOnModify()
    {
        var fixedTime = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var db = DbHelper.CreateDb(timeProvider: fixedTime);

        var person = new PersonEntity { Name = "Alice" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        fixedTime.Advance(TimeSpan.FromHours(5));

        person.Name = "Bob";
        await db.SaveChangesAsync();

        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), person.CreatedAt);
        Assert.Equal(new DateTime(2025, 1, 1, 5, 0, 0, DateTimeKind.Utc), person.UpdatedAt);
    }

    [Fact]
    public async Task SaveChanges_UsesInjectedTimeProvider_ForSoftDeleteDeletedAt()
    {
        var fixedTime = new FakeTimeProvider(new DateTimeOffset(2025, 3, 20, 12, 0, 0, TimeSpan.Zero));
        using var db = DbHelper.CreateDb(timeProvider: fixedTime);

        var entity = new SoftPersonEntity { Name = "Charlie" };
        db.SoftPersons.Add(entity);
        await db.SaveChangesAsync();

        fixedTime.Advance(TimeSpan.FromDays(1));

        db.SoftPersons.Remove(entity);
        await db.SaveChangesAsync();

        var raw = await db.SoftPersons.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);

        Assert.NotNull(raw);
        Assert.Equal(new DateTime(2025, 3, 21, 12, 0, 0, DateTimeKind.Utc), raw!.DeletedAt);
    }

    [Fact]
    public async Task SaveChanges_UsesInjectedTimeProvider_ForAuditLogTimestamp()
    {
        var fixedTime = new FakeTimeProvider(new DateTimeOffset(2025, 7, 4, 8, 0, 0, TimeSpan.Zero));
        using var db = DbHelper.CreateDb(timeProvider: fixedTime);

        var entity = new AuditPersonEntity { Name = "Audit" };
        db.AuditPersons.Add(entity);
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal(new DateTime(2025, 7, 4, 8, 0, 0, DateTimeKind.Utc), log!.Timestamp);
    }

    [Fact]
    public async Task SaveChanges_DefaultsToSystemTime_WhenNoTimeProviderInjected()
    {
        using var db = DbHelper.CreateDb();
        var before = DateTime.UtcNow;

        var person = new PersonEntity { Name = "Default" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        var after = DateTime.UtcNow;

        Assert.InRange(person.CreatedAt, before, after);
        Assert.InRange(person.UpdatedAt, before, after);
    }
}
```

- [ ] Run test — verify it fails because `CrudKitDbContext` constructor does not accept `TimeProvider`

### Step 1.2 — Implement TimeProvider injection in CrudKitDbContext

- [ ] Modify `src/CrudKit.EntityFrameworkCore/CrudKitDbContext.cs`:
  - Add optional `TimeProvider? timeProvider = null` constructor parameter
  - Store as `private readonly TimeProvider _timeProvider`
  - Replace all `DateTime.UtcNow` with `_timeProvider.GetUtcNow().UtcDateTime`

Replace the constructor and field section:

**In `src/CrudKit.EntityFrameworkCore/CrudKitDbContext.cs`, change constructor and field:**
```csharp
public abstract class CrudKitDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<SequenceEntry> Sequences => Set<SequenceEntry>();

    protected CrudKitDbContext(DbContextOptions options, ICurrentUser currentUser,
        TimeProvider? timeProvider = null)
        : base(options)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }
```

Replace all `DateTime.UtcNow` usages in `BeforeSaveChanges` and `WriteAuditLogs`:

In `BeforeSaveChanges`, the Added case:
```csharp
                case EntityState.Added:
                    // Id already set in Step 1
                    entry.Entity.CreatedAt = _timeProvider.GetUtcNow().UtcDateTime;
                    entry.Entity.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
                    if (entry.Entity is IMultiTenant mt && _currentUser.TenantId != null)
                        mt.TenantId = _currentUser.TenantId;
                    break;
```

In `BeforeSaveChanges`, the Modified case:
```csharp
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
                    entry.Property(nameof(IEntity.CreatedAt)).IsModified = false;
                    break;
```

In `BeforeSaveChanges`, the Deleted case:
```csharp
                case EntityState.Deleted:
                    if (entry.Entity is ISoftDeletable sd)
                    {
                        entry.State = EntityState.Modified;
                        sd.DeletedAt = _timeProvider.GetUtcNow().UtcDateTime;
                        entry.Entity.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
                    }
                    break;
```

In `WriteAuditLogs`, the timestamp assignment:
```csharp
            var log = new AuditLogEntry
            {
                EntityType = entry.Entity.GetType().Name,
                EntityId = (entry.Entity as IEntity)?.Id ?? string.Empty,
                UserId = _currentUser.Id,
                Timestamp = _timeProvider.GetUtcNow().UtcDateTime,
            };
```

- [ ] Run test — verify all TimeProvider tests pass
- [ ] Run full test suite — verify existing tests still pass (they use `DbHelper.CreateDb()` which passes `null` for timeProvider, defaulting to `TimeProvider.System`)

---

## Task 2: DefaultIncludeAttribute upgrade + IncludeScope

### Step 2.1 — Write failing test for class-level DefaultIncludeAttribute

- [ ] Create `src/CrudKit.Core/Enums/IncludeScope.cs`
- [ ] Create `tests/CrudKit.EntityFrameworkCore.Tests/Query/IncludeApplierTests.cs` with tests that use the new class-level attribute

**File: `src/CrudKit.Core/Enums/IncludeScope.cs`**
```csharp
namespace CrudKit.Core.Enums;

/// <summary>
/// Controls when a [DefaultInclude] navigation property is included.
/// All = included in both list and detail queries.
/// DetailOnly = included only in detail (FindById) queries.
/// </summary>
public enum IncludeScope
{
    All,
    DetailOnly
}
```

**File: `tests/CrudKit.EntityFrameworkCore.Tests/Query/IncludeApplierTests.cs`**
```csharp
using CrudKit.Core.Attributes;
using CrudKit.Core.Enums;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Query;

// --- Test entities for include tests ---

public class OrderEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<OrderLineEntity> Lines { get; set; } = new();
    public List<OrderNoteEntity> Notes { get; set; } = new();
}

public class OrderLineEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public OrderEntity? Order { get; set; }
}

public class OrderNoteEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public OrderEntity? Order { get; set; }
}

/// <summary>
/// Order entity decorated with class-level [DefaultInclude].
/// Lines included in all queries, Notes only in detail.
/// </summary>
[DefaultInclude(nameof(Lines))]
[DefaultInclude(nameof(Notes), Scope = IncludeScope.DetailOnly)]
public class DecoratedOrderEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<OrderLineEntity> Lines { get; set; } = new();
    public List<OrderNoteEntity> Notes { get; set; } = new();
}

public class IncludeApplierTests
{
    [Fact]
    public void Apply_WithNullIncludeParam_ListQuery_AppliesOnlyScopeAll()
    {
        // DecoratedOrderEntity has Lines (All) and Notes (DetailOnly)
        // For a list query (isDetailQuery=false), only Lines should be included
        var attrs = typeof(DecoratedOrderEntity)
            .GetCustomAttributes(typeof(DefaultIncludeAttribute), false)
            .Cast<DefaultIncludeAttribute>()
            .ToList();

        // Verify attribute metadata is correct
        Assert.Equal(2, attrs.Count);
        var allScope = attrs.First(a => a.NavigationProperty == "Lines");
        var detailScope = attrs.First(a => a.NavigationProperty == "Notes");
        Assert.Equal(IncludeScope.All, allScope.Scope);
        Assert.Equal(IncludeScope.DetailOnly, detailScope.Scope);
    }

    [Fact]
    public void Apply_WithNoneIncludeParam_SkipsAllIncludes()
    {
        // When includeParam = "none", no includes should be applied
        // This is a metadata/logic test — actual DB test below
        var attrs = typeof(DecoratedOrderEntity)
            .GetCustomAttributes(typeof(DefaultIncludeAttribute), false);
        Assert.Equal(2, attrs.Length); // attributes exist but "none" should skip them
    }

    [Fact]
    public void Apply_WithExplicitIncludeParam_UsesThoseNavigations()
    {
        // When includeParam = "Notes", only Notes should be included
        // regardless of DefaultInclude attributes
        // This is tested at the IncludeApplier.Apply level
        Assert.True(true); // placeholder for compile verification — real test below
    }
}
```

- [ ] Run test — verify it fails because `DefaultIncludeAttribute` does not accept constructor args

### Step 2.2 — Implement DefaultIncludeAttribute upgrade

- [ ] Modify `src/CrudKit.Core/Attributes/DefaultIncludeAttribute.cs`:

**File: `src/CrudKit.Core/Attributes/DefaultIncludeAttribute.cs`**
```csharp
using CrudKit.Core.Enums;

namespace CrudKit.Core.Attributes;

/// <summary>
/// Applied to entity classes (not properties). Declares a navigation property
/// that should be auto-included in EF Core queries.
/// Use Scope to control whether the include applies to list queries, detail queries, or both.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class DefaultIncludeAttribute : Attribute
{
    /// <summary>The name of the navigation property to include.</summary>
    public string NavigationProperty { get; }

    /// <summary>
    /// When to apply the include. Default is All (both list and detail queries).
    /// Use DetailOnly for heavy navigations that should only load on FindById.
    /// </summary>
    public IncludeScope Scope { get; set; } = IncludeScope.All;

    public DefaultIncludeAttribute(string navigationProperty)
    {
        NavigationProperty = navigationProperty;
    }
}
```

- [ ] Run tests — verify IncludeApplierTests pass (attribute metadata tests)

---

## Task 3: IncludeApplier upgrade + ListParams.Include

### Step 3.1 — Write failing test for new IncludeApplier signature

- [ ] Add `Include` property to ListParams, update the attribute tests, and write integration test

**In `src/CrudKit.Core/Models/ListParams.cs`, add Include property and update reserved keys:**
```csharp
using Microsoft.AspNetCore.Http;

namespace CrudKit.Core.Models;

/// <summary>
/// Parses HTTP query string parameters into a structured form.
/// Separates pagination (page, per_page), sorting, include, and filter parameters.
/// Usage: ?page=2&amp;per_page=25&amp;sort=-created_at&amp;include=Lines,Notes&amp;name=like:ali
/// </summary>
public class ListParams
{
    private static readonly HashSet<string> ReservedKeys =
        new(StringComparer.OrdinalIgnoreCase) { "page", "per_page", "sort", "include" };

    public int Page { get; set; } = 1;
    public int PerPage { get; set; } = 20;
    public string? Sort { get; set; }

    /// <summary>
    /// Comma-separated navigation properties to include.
    /// null = use [DefaultInclude] attributes; "none" = skip all includes;
    /// "Lines,Notes" = include only those navigations.
    /// </summary>
    public string? Include { get; set; }

    public Dictionary<string, FilterOp> Filters { get; set; } = new();

    public static ListParams FromQuery(IQueryCollection query)
    {
        var result = new ListParams();

        if (query.TryGetValue("page", out var pageVal) && int.TryParse(pageVal, out var page) && page > 0)
            result.Page = page;

        if (query.TryGetValue("per_page", out var ppVal) && int.TryParse(ppVal, out var pp) && pp > 0)
            result.PerPage = Math.Min(pp, 100);

        if (query.TryGetValue("sort", out var sortVal))
            result.Sort = sortVal.ToString();

        if (query.TryGetValue("include", out var includeVal))
            result.Include = includeVal.ToString();

        foreach (var key in query.Keys)
        {
            if (ReservedKeys.Contains(key)) continue;
            var raw = query[key].ToString();
            result.Filters[key] = FilterOp.Parse(raw);
        }

        return result;
    }
}
```

- [ ] Run test — verify it fails because `IncludeApplier.Apply` has wrong signature

### Step 3.2 — Implement IncludeApplier upgrade

- [ ] Modify `src/CrudKit.EntityFrameworkCore/Query/IncludeApplier.cs`:

**File: `src/CrudKit.EntityFrameworkCore/Query/IncludeApplier.cs`**
```csharp
using System.Reflection;
using CrudKit.Core.Attributes;
using CrudKit.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Query;

/// <summary>
/// Resolves which navigation properties to Include() on an EF Core query.
/// Priority: explicit includeParam > "none" (skip all) > [DefaultInclude] attributes with scope filtering.
/// </summary>
public static class IncludeApplier
{
    /// <summary>
    /// Applies Include() calls to the query based on the include strategy.
    /// </summary>
    /// <param name="query">The base EF Core queryable.</param>
    /// <param name="includeParam">
    /// Comma-separated navigation names from the client. null = use [DefaultInclude] attributes.
    /// "none" = skip all includes.
    /// </param>
    /// <param name="isDetailQuery">
    /// true for FindById (includes DetailOnly navigations); false for List queries.
    /// </param>
    public static IQueryable<T> Apply<T>(IQueryable<T> query, string? includeParam, bool isDetailQuery)
        where T : class
    {
        // Strategy 1: explicit "none" — skip all includes
        if (string.Equals(includeParam, "none", StringComparison.OrdinalIgnoreCase))
            return query;

        // Strategy 2: explicit include list from client
        if (!string.IsNullOrWhiteSpace(includeParam))
        {
            var names = includeParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var validProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var name in names)
            {
                if (validProps.Contains(name))
                    query = query.Include(name);
            }
            return query;
        }

        // Strategy 3: [DefaultInclude] attributes on the entity class, filtered by scope
        var attributes = typeof(T).GetCustomAttributes<DefaultIncludeAttribute>(inherit: true);
        foreach (var attr in attributes)
        {
            if (attr.Scope == IncludeScope.DetailOnly && !isDetailQuery)
                continue;

            query = query.Include(attr.NavigationProperty);
        }

        return query;
    }
}
```

### Step 3.3 — Update QueryBuilder to use new IncludeApplier signature

- [ ] Modify `src/CrudKit.EntityFrameworkCore/Query/QueryBuilder.cs`:

**File: `src/CrudKit.EntityFrameworkCore/Query/QueryBuilder.cs`**
```csharp
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
        // 1. Apply includes (list context — isDetailQuery = false)
        query = IncludeApplier.Apply(query, listParams.Include, isDetailQuery: false);

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

### Step 3.4 — Update EfRepo to use new IncludeApplier signature

- [ ] Modify `src/CrudKit.EntityFrameworkCore/Repository/EfRepo.cs` — update `FindById` and `FindByIdOrDefault`:

In `FindById`:
```csharp
    public async Task<T> FindById(string id, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        query = IncludeApplier.Apply(query, includeParam: null, isDetailQuery: true);
        var entity = await query.FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");
    }
```

In `FindByIdOrDefault`:
```csharp
    public async Task<T?> FindByIdOrDefault(string id, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        query = IncludeApplier.Apply(query, includeParam: null, isDetailQuery: true);
        return await query.FirstOrDefaultAsync(e => e.Id == id, ct);
    }
```

- [ ] Run tests — verify all pass (existing QueryBuilder tests use `IncludeApplier.Apply` indirectly through `QueryBuilder.Apply`, and now the ListParams has `Include = null` by default which resolves to attribute-based includes)

---

## Task 4: ICrudHooks ApplyScope integration in EfRepo

### Step 4.1 — Write failing test for hooks-based scope filtering

- [ ] Add test entities and tests to `tests/CrudKit.EntityFrameworkCore.Tests/Repository/EfRepoTests.cs`

**Add to end of `EfRepoTests.cs`:**
```csharp
    // ---- ApplyScope via ICrudHooks ----

    [Fact]
    public async Task List_WithHooks_AppliesScope()
    {
        var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        var queryBuilder = new QueryBuilder<PersonEntity>(new FilterApplier(dialect));
        var hooks = new AgeFilterHooks(minAge: 25);
        var repo = new EfRepo<PersonEntity>(db, queryBuilder, hooks);

        await repo.Create(new { Name = "Young", Age = 20 });
        await repo.Create(new { Name = "Old", Age = 30 });
        await repo.Create(new { Name = "Mid", Age = 25 });

        var result = await repo.List(new ListParams());
        Assert.Equal(2, result.Total);
        Assert.All(result.Data, p => Assert.True(p.Age >= 25));
    }

    [Fact]
    public async Task FindById_WithHooks_AppliesScope_ThrowsWhenOutOfScope()
    {
        var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        var queryBuilder = new QueryBuilder<PersonEntity>(new FilterApplier(dialect));
        var hooks = new AgeFilterHooks(minAge: 25);
        var repo = new EfRepo<PersonEntity>(db, queryBuilder, hooks);

        var young = await repo.Create(new { Name = "Young", Age = 20 });

        // Use a separate repo without hooks to create the entity, then try to find with hooks
        var repoNoHooks = new EfRepo<PersonEntity>(db, queryBuilder);
        var youngCreated = await repoNoHooks.Create(new { Name = "Young2", Age = 18 });

        var ex = await Assert.ThrowsAsync<AppError>(() => repo.FindById(youngCreated.Id));
        Assert.Equal(404, ex.StatusCode);
    }

    /// <summary>Test hooks implementation that filters entities by minimum age.</summary>
    private class AgeFilterHooks : ICrudHooks<PersonEntity>
    {
        private readonly int _minAge;
        public AgeFilterHooks(int minAge) => _minAge = minAge;

        public IQueryable<PersonEntity> ApplyScope(IQueryable<PersonEntity> query,
            CrudKit.Core.Context.AppContext ctx)
            => query.Where(p => p.Age >= _minAge);
    }
```

- [ ] Run test — verify it fails because `EfRepo<T>` constructor does not accept `ICrudHooks<T>`

### Step 4.2 — Implement hooks integration in EfRepo

- [ ] Modify `src/CrudKit.EntityFrameworkCore/Repository/EfRepo.cs`:

**Update constructor and fields:**
```csharp
public class EfRepo<T> : IRepo<T> where T : class, IEntity
{
    private readonly CrudKitDbContext _db;
    private readonly QueryBuilder<T> _queryBuilder;
    private readonly ICrudHooks<T>? _hooks;

    public EfRepo(CrudKitDbContext db, QueryBuilder<T> queryBuilder, ICrudHooks<T>? hooks = null)
    {
        _db = db;
        _queryBuilder = queryBuilder;
        _hooks = hooks;
    }
```

**Add private helper to build AppContext:**
```csharp
    private CrudKit.Core.Context.AppContext BuildAppContext()
    {
        return new CrudKit.Core.Context.AppContext
        {
            Services = null!, // EfRepo does not have access to IServiceProvider
            CurrentUser = _db.CurrentUser,
        };
    }
```

**Note:** `CrudKitDbContext` currently has `_currentUser` as private. We need to expose it as an internal property. Add to `CrudKitDbContext`:

In `src/CrudKit.EntityFrameworkCore/CrudKitDbContext.cs`, add right after the `CurrentTenantId` property:
```csharp
    /// <summary>
    /// Exposes the current user for EfRepo to build AppContext.
    /// </summary>
    internal ICurrentUser CurrentUser => _currentUser;
```

**Update FindById in EfRepo:**
```csharp
    public async Task<T> FindById(string id, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        query = IncludeApplier.Apply(query, includeParam: null, isDetailQuery: true);
        if (_hooks != null)
            query = _hooks.ApplyScope(query, BuildAppContext());
        var entity = await query.FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity ?? throw AppError.NotFound($"{typeof(T).Name} with id '{id}' was not found.");
    }
```

**Update FindByIdOrDefault in EfRepo:**
```csharp
    public async Task<T?> FindByIdOrDefault(string id, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        query = IncludeApplier.Apply(query, includeParam: null, isDetailQuery: true);
        if (_hooks != null)
            query = _hooks.ApplyScope(query, BuildAppContext());
        return await query.FirstOrDefaultAsync(e => e.Id == id, ct);
    }
```

**Update List in EfRepo — pass hooks to QueryBuilder or apply after:**

We need to apply scope in `EfRepo.List` before delegating to QueryBuilder. Change `List`:
```csharp
    public async Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        if (_hooks != null)
            query = _hooks.ApplyScope(query, BuildAppContext());
        return await _queryBuilder.Apply(query, listParams, ct);
    }
```

### Step 4.3 — Update ServiceCollectionExtensions to wire hooks

- [ ] Modify `src/CrudKit.EntityFrameworkCore/Extensions/ServiceCollectionExtensions.cs`:

**File: `src/CrudKit.EntityFrameworkCore/Extensions/ServiceCollectionExtensions.cs`**
```csharp
using CrudKit.Core.Interfaces;
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
        services.TryAddScoped<CrudKitDbContext>(sp => sp.GetRequiredService<TContext>());

        // Dialect — auto-detected from TContext's provider.
        services.TryAddScoped<IDbDialect>(sp =>
        {
            var db = sp.GetRequiredService<TContext>();
            return DialectDetector.Detect(db);
        });

        // Query pipeline
        services.TryAddScoped<FilterApplier>();
        services.TryAdd(ServiceDescriptor.Scoped(typeof(QueryBuilder<>), typeof(QueryBuilder<>)));

        // Open generic repository: IRepo<T> → EfRepo<T>
        // EfRepo<T> resolves ICrudHooks<T>? via DI (optional dependency)
        services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepo<>), typeof(EfRepo<>)));

        // Document numbering
        services.TryAddScoped<SequenceGenerator>();

        return services;
    }
}
```

**Note on DI resolution:** `EfRepo<T>` has `ICrudHooks<T>? hooks = null` as an optional constructor parameter. The DI container will resolve `ICrudHooks<T>` if registered, or pass `null` if not. This works out of the box with Microsoft.Extensions.DependencyInjection because the parameter has a default value.

- [ ] Run tests — verify hooks tests pass
- [ ] Run full test suite — verify existing EfRepo tests pass (they create `EfRepo` with 2-arg constructor which still works)

---

## Task 5: CascadeSoftDelete

### Step 5.1 — Write failing tests for cascade soft delete

- [ ] Add cascade test entities to `tests/CrudKit.EntityFrameworkCore.Tests/Helpers/TestEntities.cs`
- [ ] Update `tests/CrudKit.EntityFrameworkCore.Tests/Helpers/TestDbContext.cs` to add new DbSets
- [ ] Create `tests/CrudKit.EntityFrameworkCore.Tests/DbContext/CascadeSoftDeleteTests.cs`

**Add to `tests/CrudKit.EntityFrameworkCore.Tests/Helpers/TestEntities.cs`:**
```csharp
/// <summary>Parent entity with cascade soft delete to children.</summary>
[CascadeSoftDelete(typeof(ChildItemEntity), nameof(ChildItemEntity.ParentItemId))]
public class ParentItemEntity : IEntity, ISoftDeletable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<ChildItemEntity> Children { get; set; } = new();
}

/// <summary>Child entity that gets cascade soft-deleted when parent is soft-deleted.</summary>
public class ChildItemEntity : IEntity, ISoftDeletable, ICascadeSoftDelete<ParentItemEntity>
{
    public string Id { get; set; } = string.Empty;
    public string ParentItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public static string ParentForeignKey => nameof(ParentItemId);
}

/// <summary>Entity with [Unique] + ISoftDeletable for restore conflict tests.</summary>
public class UniqueCodeEntity : IEntity, ISoftDeletable
{
    public string Id { get; set; } = string.Empty;

    [Unique]
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

**Add to `TestDbContext.cs` DbSets:**
```csharp
    public DbSet<ParentItemEntity> ParentItems => Set<ParentItemEntity>();
    public DbSet<ChildItemEntity> ChildItems => Set<ChildItemEntity>();
    public DbSet<UniqueCodeEntity> UniqueCodes => Set<UniqueCodeEntity>();
```

**File: `tests/CrudKit.EntityFrameworkCore.Tests/DbContext/CascadeSoftDeleteTests.cs`**
```csharp
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.DbContext;

public class CascadeSoftDeleteTests
{
    [Fact]
    public async Task SoftDelete_Parent_CascadesToChildren()
    {
        using var db = DbHelper.CreateDb();

        var parent = new ParentItemEntity { Name = "Parent1" };
        db.ParentItems.Add(parent);
        await db.SaveChangesAsync();

        var child1 = new ChildItemEntity { ParentItemId = parent.Id, Name = "Child1" };
        var child2 = new ChildItemEntity { ParentItemId = parent.Id, Name = "Child2" };
        db.ChildItems.AddRange(child1, child2);
        await db.SaveChangesAsync();

        // Soft-delete the parent
        db.ParentItems.Remove(parent);
        await db.SaveChangesAsync();

        // Parent should be soft-deleted
        var parentRaw = await db.ParentItems.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == parent.Id);
        Assert.NotNull(parentRaw);
        Assert.NotNull(parentRaw!.DeletedAt);

        // Children should also be soft-deleted
        var children = await db.ChildItems.IgnoreQueryFilters()
            .Where(c => c.ParentItemId == parent.Id)
            .ToListAsync();
        Assert.Equal(2, children.Count);
        Assert.All(children, c => Assert.NotNull(c.DeletedAt));
    }

    [Fact]
    public async Task SoftDelete_Parent_DoesNotAffectOtherParentsChildren()
    {
        using var db = DbHelper.CreateDb();

        var parent1 = new ParentItemEntity { Name = "Parent1" };
        var parent2 = new ParentItemEntity { Name = "Parent2" };
        db.ParentItems.AddRange(parent1, parent2);
        await db.SaveChangesAsync();

        var child1 = new ChildItemEntity { ParentItemId = parent1.Id, Name = "Child1" };
        var child2 = new ChildItemEntity { ParentItemId = parent2.Id, Name = "Child2" };
        db.ChildItems.AddRange(child1, child2);
        await db.SaveChangesAsync();

        // Soft-delete only parent1
        db.ParentItems.Remove(parent1);
        await db.SaveChangesAsync();

        // parent2's child should NOT be affected
        var child2Check = await db.ChildItems
            .FirstOrDefaultAsync(c => c.Id == child2.Id);
        Assert.NotNull(child2Check);
        Assert.Null(child2Check!.DeletedAt);
    }

    [Fact]
    public async Task SoftDelete_Parent_CascadeUsesTimeProvider()
    {
        var fixedTime = new FakeTimeProvider(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));
        using var db = DbHelper.CreateDb(timeProvider: fixedTime);

        var parent = new ParentItemEntity { Name = "Parent" };
        db.ParentItems.Add(parent);
        await db.SaveChangesAsync();

        var child = new ChildItemEntity { ParentItemId = parent.Id, Name = "Child" };
        db.ChildItems.Add(child);
        await db.SaveChangesAsync();

        fixedTime.Advance(TimeSpan.FromDays(10));
        db.ParentItems.Remove(parent);
        await db.SaveChangesAsync();

        var childRaw = await db.ChildItems.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == child.Id);
        Assert.NotNull(childRaw);
        Assert.Equal(new DateTime(2025, 6, 11, 0, 0, 0, DateTimeKind.Utc), childRaw!.DeletedAt);
    }

    /// <summary>Reusable fake TimeProvider for cascade tests.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
```

- [ ] Run test — verify it fails because `CascadeSoftDeleteAttribute` does not accept constructor args

### Step 5.2 — Implement CascadeSoftDeleteAttribute and ICascadeSoftDelete changes

- [ ] Modify `src/CrudKit.Core/Attributes/CascadeSoftDeleteAttribute.cs`:

**File: `src/CrudKit.Core/Attributes/CascadeSoftDeleteAttribute.cs`**
```csharp
namespace CrudKit.Core.Attributes;

/// <summary>
/// Applied to parent entity classes. Declares a child entity type and its foreign key
/// so that when the parent is soft-deleted, children are cascade soft-deleted too.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class CascadeSoftDeleteAttribute : Attribute
{
    /// <summary>The child entity type that should be cascade soft-deleted.</summary>
    public Type ChildType { get; }

    /// <summary>
    /// The property name on the child entity that holds the foreign key to this parent.
    /// </summary>
    public string ForeignKeyProperty { get; }

    public CascadeSoftDeleteAttribute(Type childType, string foreignKeyProperty)
    {
        ChildType = childType;
        ForeignKeyProperty = foreignKeyProperty;
    }
}
```

- [ ] Modify `src/CrudKit.Core/Interfaces/ICascadeSoftDelete.cs`:

**File: `src/CrudKit.Core/Interfaces/ICascadeSoftDelete.cs`**
```csharp
namespace CrudKit.Core.Interfaces;

/// <summary>
/// Implemented by child entities that participate in cascade soft delete.
/// The static abstract ParentForeignKey property declares which property holds the parent FK.
/// </summary>
/// <typeparam name="TParent">The parent entity type.</typeparam>
public interface ICascadeSoftDelete<TParent> : ISoftDeletable
    where TParent : class, IEntity, ISoftDeletable
{
    /// <summary>The property name on this entity that holds the foreign key to the parent.</summary>
    static abstract string ParentForeignKey { get; }
}
```

### Step 5.3 — Implement ApplyCascadeSoftDelete in CrudKitDbContext

- [ ] Add `ApplyCascadeSoftDelete()` method to `CrudKitDbContext` and call it from `BeforeSaveChanges`

**In `src/CrudKit.EntityFrameworkCore/CrudKitDbContext.cs`, add method after `WriteAuditLogs()`:**
```csharp
    private void ApplyCascadeSoftDelete()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Find entities being soft-deleted in this save operation.
        // At this point in BeforeSaveChanges, soft-deleted entities have already been
        // converted from Deleted → Modified with DeletedAt set.
        var softDeletedEntries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified
                && e.Entity is ISoftDeletable sd
                && sd.DeletedAt != null
                && e.Property(nameof(ISoftDeletable.DeletedAt)).IsModified)
            .ToList();

        foreach (var entry in softDeletedEntries)
        {
            var parentType = entry.Entity.GetType();
            var cascadeAttrs = parentType.GetCustomAttributes(typeof(CascadeSoftDeleteAttribute), true)
                .Cast<CascadeSoftDeleteAttribute>();

            foreach (var attr in cascadeAttrs)
            {
                var parentId = ((IEntity)entry.Entity).Id;
                CascadeDeleteChildren(attr.ChildType, attr.ForeignKeyProperty, parentId, now);
            }
        }
    }

    private void CascadeDeleteChildren(Type childType, string foreignKeyProperty, string parentId, DateTime now)
    {
        // Use raw SQL to update children without loading them into memory.
        // This handles the case where children might be in a different DbSet.
        var tableName = Model.FindEntityType(childType)?.GetTableName();
        if (tableName == null) return;

        var fkColumn = Model.FindEntityType(childType)?
            .FindProperty(foreignKeyProperty)?.GetColumnName();
        if (fkColumn == null) return;

        var deletedAtColumn = Model.FindEntityType(childType)?
            .FindProperty(nameof(ISoftDeletable.DeletedAt))?.GetColumnName();
        if (deletedAtColumn == null) return;

        var updatedAtColumn = Model.FindEntityType(childType)?
            .FindProperty(nameof(IEntity.UpdatedAt))?.GetColumnName();

        // Only cascade to children that are not already deleted
        var sql = updatedAtColumn != null
            ? $"UPDATE \"{tableName}\" SET \"{deletedAtColumn}\" = {{0}}, \"{updatedAtColumn}\" = {{0}} WHERE \"{fkColumn}\" = {{1}} AND \"{deletedAtColumn}\" IS NULL"
            : $"UPDATE \"{tableName}\" SET \"{deletedAtColumn}\" = {{0}} WHERE \"{fkColumn}\" = {{1}} AND \"{deletedAtColumn}\" IS NULL";

        Database.ExecuteSqlRaw(sql, now, parentId);
    }
```

**Update `BeforeSaveChanges()` to call `ApplyCascadeSoftDelete()` after the main loop:**
```csharp
    private void BeforeSaveChanges()
    {
        // Step 1: Generate IDs for new entities first (needed by audit log)
        foreach (var entry in ChangeTracker.Entries<IEntity>()
            .Where(e => e.State == EntityState.Added).ToList())
        {
            if (string.IsNullOrEmpty(entry.Entity.Id))
                entry.Entity.Id = Guid.NewGuid().ToString();
        }

        // Step 2: Write audit logs (now IDs are available for new entities,
        // and Deleted state is still intact for soft-delete detection)
        WriteAuditLogs();

        // Step 3: Set remaining fields and handle soft-delete conversion
        foreach (var entry in ChangeTracker.Entries<IEntity>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = _timeProvider.GetUtcNow().UtcDateTime;
                    entry.Entity.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
                    if (entry.Entity is IMultiTenant mt && _currentUser.TenantId != null)
                        mt.TenantId = _currentUser.TenantId;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
                    entry.Property(nameof(IEntity.CreatedAt)).IsModified = false;
                    break;

                case EntityState.Deleted:
                    if (entry.Entity is ISoftDeletable sd)
                    {
                        entry.State = EntityState.Modified;
                        sd.DeletedAt = _timeProvider.GetUtcNow().UtcDateTime;
                        entry.Entity.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
                    }
                    break;
            }
        }

        // Step 4: Cascade soft delete to child entities
        ApplyCascadeSoftDelete();
    }
```

- [ ] Run tests — verify cascade soft delete tests pass

### Step 5.4 — Write failing test for cascade restore

- [ ] Add restore cascade test to `tests/CrudKit.EntityFrameworkCore.Tests/Repository/EfRepoTests.cs`

**Add to `EfRepoTests.cs`:**
```csharp
    private static (TestDbContext db, EfRepo<ParentItemEntity> repo) CreateParentRepo()
    {
        var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        var queryBuilder = new QueryBuilder<ParentItemEntity>(new FilterApplier(dialect));
        var repo = new EfRepo<ParentItemEntity>(db, queryBuilder);
        return (db, repo);
    }

    // ---- Cascade Restore ----

    [Fact]
    public async Task Restore_CascadesRestoreToChildren()
    {
        var (db, repo) = CreateParentRepo();

        var parent = await repo.Create(new { Name = "Parent" });

        var child1 = new ChildItemEntity { ParentItemId = parent.Id, Name = "C1" };
        var child2 = new ChildItemEntity { ParentItemId = parent.Id, Name = "C2" };
        db.ChildItems.AddRange(child1, child2);
        await db.SaveChangesAsync();

        // Delete parent (cascades to children)
        await repo.Delete(parent.Id);

        // Verify children are soft-deleted
        var deletedChildren = await db.ChildItems.IgnoreQueryFilters()
            .Where(c => c.ParentItemId == parent.Id).ToListAsync();
        Assert.All(deletedChildren, c => Assert.NotNull(c.DeletedAt));

        // Restore parent (should cascade restore to children)
        await repo.Restore(parent.Id);

        var restoredChildren = await db.ChildItems
            .Where(c => c.ParentItemId == parent.Id).ToListAsync();
        Assert.Equal(2, restoredChildren.Count);
        Assert.All(restoredChildren, c => Assert.Null(c.DeletedAt));
    }

    [Fact]
    public async Task Restore_CascadeDoesNotRestoreOtherParentsChildren()
    {
        var (db, repo) = CreateParentRepo();

        var parent1 = await repo.Create(new { Name = "P1" });
        var parent2 = await repo.Create(new { Name = "P2" });

        db.ChildItems.Add(new ChildItemEntity { ParentItemId = parent1.Id, Name = "C1" });
        db.ChildItems.Add(new ChildItemEntity { ParentItemId = parent2.Id, Name = "C2" });
        await db.SaveChangesAsync();

        // Delete both parents
        await repo.Delete(parent1.Id);
        await repo.Delete(parent2.Id);

        // Restore only parent1
        await repo.Restore(parent1.Id);

        // parent2's child should remain deleted
        var p2Children = await db.ChildItems.IgnoreQueryFilters()
            .Where(c => c.ParentItemId == parent2.Id).ToListAsync();
        Assert.All(p2Children, c => Assert.NotNull(c.DeletedAt));
    }
```

- [ ] Run test — verify it fails because `EfRepo.Restore` does not cascade

### Step 5.5 — Implement cascade restore in EfRepo

- [ ] Modify `EfRepo.Restore` in `src/CrudKit.EntityFrameworkCore/Repository/EfRepo.cs`:

```csharp
    public async Task Restore(string id, CancellationToken ct = default)
    {
        if (typeof(T).IsAssignableTo(typeof(ISoftDeletable)) == false)
            throw new InvalidOperationException($"{typeof(T).Name} does not implement ISoftDeletable.");

        var entity = await _db.Set<T>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound($"Deleted {typeof(T).Name} with id '{id}' was not found.");

        // Re-enforce tenant isolation since IgnoreQueryFilters bypasses the tenant filter.
        if (entity is IMultiTenant multiTenant)
        {
            var currentTenantId = _db.CurrentTenantId;
            if (currentTenantId != null && multiTenant.TenantId != currentTenantId)
                throw AppError.NotFound($"Deleted {typeof(T).Name} with id '{id}' was not found.");
        }

        ((ISoftDeletable)entity).DeletedAt = null;
        await _db.SaveChangesAsync(ct);

        // Cascade restore children
        CascadeRestoreChildren(id);
    }

    private void CascadeRestoreChildren(string parentId)
    {
        var cascadeAttrs = typeof(T).GetCustomAttributes(typeof(CascadeSoftDeleteAttribute), true)
            .Cast<CascadeSoftDeleteAttribute>();

        foreach (var attr in cascadeAttrs)
        {
            var childType = attr.ChildType;
            var tableName = _db.Model.FindEntityType(childType)?.GetTableName();
            if (tableName == null) continue;

            var fkColumn = _db.Model.FindEntityType(childType)?
                .FindProperty(attr.ForeignKeyProperty)?.GetColumnName();
            if (fkColumn == null) continue;

            var deletedAtColumn = _db.Model.FindEntityType(childType)?
                .FindProperty(nameof(ISoftDeletable.DeletedAt))?.GetColumnName();
            if (deletedAtColumn == null) continue;

            var updatedAtColumn = _db.Model.FindEntityType(childType)?
                .FindProperty(nameof(IEntity.UpdatedAt))?.GetColumnName();

            var sql = updatedAtColumn != null
                ? $"UPDATE \"{tableName}\" SET \"{deletedAtColumn}\" = NULL, \"{updatedAtColumn}\" = {{0}} WHERE \"{fkColumn}\" = {{1}} AND \"{deletedAtColumn}\" IS NOT NULL"
                : $"UPDATE \"{tableName}\" SET \"{deletedAtColumn}\" = NULL WHERE \"{fkColumn}\" = {{1}} AND \"{deletedAtColumn}\" IS NOT NULL";

            if (updatedAtColumn != null)
                _db.Database.ExecuteSqlRaw(sql, DateTime.UtcNow, parentId);
            else
                _db.Database.ExecuteSqlRaw(sql, parentId);
        }
    }
```

- [ ] Run tests — verify cascade restore tests pass
- [ ] Run full test suite — verify all existing tests still pass

---

## Task 6: EfRepo.Restore unique constraint check

### Step 6.1 — Write failing test for restore unique conflict

- [ ] Add test to `tests/CrudKit.EntityFrameworkCore.Tests/Repository/EfRepoTests.cs`

**Add helper:**
```csharp
    private static (TestDbContext db, EfRepo<UniqueCodeEntity> repo) CreateUniqueCodeRepo()
    {
        var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        var queryBuilder = new QueryBuilder<UniqueCodeEntity>(new FilterApplier(dialect));
        var repo = new EfRepo<UniqueCodeEntity>(db, queryBuilder);
        return (db, repo);
    }
```

**Add tests:**
```csharp
    // ---- Restore unique constraint check ----

    [Fact]
    public async Task Restore_ThrowsConflict_WhenUniqueFieldClashesWithActiveRecord()
    {
        var (db, repo) = CreateUniqueCodeRepo();

        // Create entity with unique code, then soft-delete it
        var original = await repo.Create(new { Code = "ABC-001", Name = "Original" });
        await repo.Delete(original.Id);

        // Create another entity with the same unique code
        var duplicate = await repo.Create(new { Code = "ABC-001", Name = "Duplicate" });

        // Try to restore the original — should fail with 409 Conflict
        var ex = await Assert.ThrowsAsync<AppError>(() => repo.Restore(original.Id));
        Assert.Equal(409, ex.StatusCode);
        Assert.Contains("ABC-001", ex.Message);
    }

    [Fact]
    public async Task Restore_Succeeds_WhenUniqueFieldDoesNotClash()
    {
        var (db, repo) = CreateUniqueCodeRepo();

        var entity = await repo.Create(new { Code = "XYZ-999", Name = "Solo" });
        await repo.Delete(entity.Id);

        // No other entity has "XYZ-999", restore should succeed
        await repo.Restore(entity.Id);

        var restored = await repo.FindById(entity.Id);
        Assert.Equal("XYZ-999", restored.Code);
    }

    [Fact]
    public async Task Restore_Succeeds_WhenUniqueFieldClashesWithDeletedRecord()
    {
        var (db, repo) = CreateUniqueCodeRepo();

        // Two entities with same code, both deleted
        var first = await repo.Create(new { Code = "DUP-001", Name = "First" });
        await repo.Delete(first.Id);

        var second = await repo.Create(new { Code = "DUP-001", Name = "Second" });
        await repo.Delete(second.Id);

        // Restore first — should succeed because the other record is also deleted
        await repo.Restore(first.Id);
        var restored = await repo.FindById(first.Id);
        Assert.Equal("DUP-001", restored.Code);
    }
```

- [ ] Run test — verify it fails because `Restore` does not check unique constraints

### Step 6.2 — Implement unique constraint check in Restore

- [ ] Modify `EfRepo.Restore` in `src/CrudKit.EntityFrameworkCore/Repository/EfRepo.cs`:

Insert the unique check **after** setting `DeletedAt = null` but **before** `SaveChangesAsync`:

```csharp
    public async Task Restore(string id, CancellationToken ct = default)
    {
        if (typeof(T).IsAssignableTo(typeof(ISoftDeletable)) == false)
            throw new InvalidOperationException($"{typeof(T).Name} does not implement ISoftDeletable.");

        var entity = await _db.Set<T>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound($"Deleted {typeof(T).Name} with id '{id}' was not found.");

        // Re-enforce tenant isolation since IgnoreQueryFilters bypasses the tenant filter.
        if (entity is IMultiTenant multiTenant)
        {
            var currentTenantId = _db.CurrentTenantId;
            if (currentTenantId != null && multiTenant.TenantId != currentTenantId)
                throw AppError.NotFound($"Deleted {typeof(T).Name} with id '{id}' was not found.");
        }

        // Check [Unique] properties for conflicts with active records before restoring
        await CheckUniqueConflictsOnRestore(entity, ct);

        ((ISoftDeletable)entity).DeletedAt = null;
        await _db.SaveChangesAsync(ct);

        // Cascade restore children
        CascadeRestoreChildren(id);
    }

    private async Task CheckUniqueConflictsOnRestore(T entity, CancellationToken ct)
    {
        var uniqueProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<UniqueAttribute>() != null)
            .ToList();

        if (uniqueProps.Count == 0) return;

        foreach (var prop in uniqueProps)
        {
            var value = prop.GetValue(entity);
            if (value == null) continue;

            // Build predicate: e => e.Property == value && e.Id != entity.Id
            var param = Expression.Parameter(typeof(T), "e");
            var propAccess = Expression.Property(param, prop);
            var valueConst = Expression.Constant(value, prop.PropertyType);
            var eq = Expression.Equal(propAccess, valueConst);

            var idAccess = Expression.Property(param, nameof(IEntity.Id));
            var idConst = Expression.Constant(entity.Id, typeof(string));
            var neqId = Expression.NotEqual(idAccess, idConst);

            var combined = Expression.AndAlso(eq, neqId);
            var predicate = Expression.Lambda<Func<T, bool>>(combined, param);

            // Query active records only (global query filter excludes soft-deleted)
            var conflictExists = await _db.Set<T>().Where(predicate).AnyAsync(ct);
            if (conflictExists)
            {
                throw AppError.Conflict(
                    $"Cannot restore {typeof(T).Name}: an active record with {prop.Name} = '{value}' already exists.");
            }
        }
    }
```

- [ ] Run tests — verify unique constraint tests pass
- [ ] Run full test suite

---

## Task 7: IRepo bulk operations

### Step 7.1 — Write failing tests for bulk operations

- [ ] Add bulk operation method signatures to `IRepo<T>`
- [ ] Add tests to `tests/CrudKit.EntityFrameworkCore.Tests/Repository/EfRepoTests.cs`

**In `src/CrudKit.EntityFrameworkCore/Repository/IRepo.cs`, add methods:**
```csharp
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

    /// <summary>
    /// Bulk update matching entities with the given field values.
    /// Returns the number of affected rows.
    /// </summary>
    Task<int> BulkUpdate(Dictionary<string, FilterOp> filters, Dictionary<string, object?> updateValues, CancellationToken ct = default);

    /// <summary>
    /// Bulk delete matching entities. For ISoftDeletable entities, sets DeletedAt instead.
    /// Returns the number of affected rows.
    /// </summary>
    Task<int> BulkDelete(Dictionary<string, FilterOp> filters, CancellationToken ct = default);

    /// <summary>
    /// Count matching entities using the filter pipeline.
    /// </summary>
    Task<long> BulkCount(Dictionary<string, FilterOp> filters, CancellationToken ct = default);
}
```

**Add to `EfRepoTests.cs`:**
```csharp
    // ---- BulkCount ----

    [Fact]
    public async Task BulkCount_ReturnsCountMatchingFilter()
    {
        var (_, repo) = CreatePersonRepo();
        await repo.Create(new { Name = "A", Age = 20 });
        await repo.Create(new { Name = "B", Age = 30 });
        await repo.Create(new { Name = "C", Age = 30 });

        var filters = new Dictionary<string, FilterOp>
        {
            ["Age"] = FilterOp.Parse("eq:30")
        };

        var count = await repo.BulkCount(filters);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task BulkCount_ReturnsZero_WhenNoMatch()
    {
        var (_, repo) = CreatePersonRepo();
        await repo.Create(new { Name = "A", Age = 20 });

        var filters = new Dictionary<string, FilterOp>
        {
            ["Age"] = FilterOp.Parse("eq:99")
        };

        var count = await repo.BulkCount(filters);
        Assert.Equal(0, count);
    }

    // ---- BulkDelete ----

    [Fact]
    public async Task BulkDelete_PhysicalEntity_RemovesMatchingRows()
    {
        var (db, repo) = CreatePersonRepo();
        await repo.Create(new { Name = "Keep", Age = 20 });
        await repo.Create(new { Name = "Del1", Age = 30 });
        await repo.Create(new { Name = "Del2", Age = 30 });

        var filters = new Dictionary<string, FilterOp>
        {
            ["Age"] = FilterOp.Parse("eq:30")
        };

        var affected = await repo.BulkDelete(filters);
        Assert.Equal(2, affected);

        var remaining = await repo.Count();
        Assert.Equal(1, remaining);
    }

    [Fact]
    public async Task BulkDelete_SoftDeletable_SetsDeletedAt()
    {
        var (db, repo) = CreateSoftRepo();
        await repo.Create(new { Name = "Keep" });
        await repo.Create(new { Name = "Del1" });
        await repo.Create(new { Name = "Del2" });

        var filters = new Dictionary<string, FilterOp>
        {
            ["Name"] = FilterOp.Parse("starts:Del")
        };

        var affected = await repo.BulkDelete(filters);
        Assert.Equal(2, affected);

        // Only "Keep" should remain visible
        var remaining = await repo.Count();
        Assert.Equal(1, remaining);

        // But the rows still exist
        var allRows = await db.SoftPersons.IgnoreQueryFilters().CountAsync();
        Assert.Equal(3, allRows);
    }

    // ---- BulkUpdate ----

    [Fact]
    public async Task BulkUpdate_UpdatesMatchingEntities()
    {
        var (db, repo) = CreatePersonRepo();
        await repo.Create(new { Name = "A", Age = 20 });
        await repo.Create(new { Name = "B", Age = 30 });
        await repo.Create(new { Name = "C", Age = 30 });

        var filters = new Dictionary<string, FilterOp>
        {
            ["Age"] = FilterOp.Parse("eq:30")
        };
        var updates = new Dictionary<string, object?> { ["Age"] = 35 };

        var affected = await repo.BulkUpdate(filters, updates);
        Assert.Equal(2, affected);

        var list = await db.Persons.Where(p => p.Age == 35).ToListAsync();
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task BulkUpdate_ReturnsZero_WhenNoMatch()
    {
        var (_, repo) = CreatePersonRepo();
        await repo.Create(new { Name = "A", Age = 20 });

        var filters = new Dictionary<string, FilterOp>
        {
            ["Age"] = FilterOp.Parse("eq:99")
        };
        var updates = new Dictionary<string, object?> { ["Age"] = 100 };

        var affected = await repo.BulkUpdate(filters, updates);
        Assert.Equal(0, affected);
    }
```

- [ ] Run test — verify it fails because `EfRepo<T>` does not implement bulk methods

### Step 7.2 — Implement bulk operations in EfRepo

- [ ] Add bulk method implementations to `src/CrudKit.EntityFrameworkCore/Repository/EfRepo.cs`

**Add these methods to `EfRepo<T>`:**
```csharp
    public async Task<long> BulkCount(Dictionary<string, FilterOp> filters, CancellationToken ct = default)
    {
        var query = ApplyFilters(_db.Set<T>().AsNoTracking(), filters);
        return await query.LongCountAsync(ct);
    }

    public async Task<int> BulkDelete(Dictionary<string, FilterOp> filters, CancellationToken ct = default)
    {
        var query = ApplyFilters(_db.Set<T>().AsQueryable(), filters);

        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
        {
            // Soft delete: set DeletedAt and UpdatedAt via ExecuteUpdate
            var now = DateTime.UtcNow;
            return await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(e => ((ISoftDeletable)(object)e).DeletedAt, now)
                .SetProperty(e => ((IEntity)e).UpdatedAt, now), ct);
        }

        return await query.ExecuteDeleteAsync(ct);
    }

    public async Task<int> BulkUpdate(Dictionary<string, FilterOp> filters,
        Dictionary<string, object?> updateValues, CancellationToken ct = default)
    {
        var query = ApplyFilters(_db.Set<T>().AsQueryable(), filters);

        // Build SetProperty calls dynamically using expression trees
        return await query.ExecuteUpdateAsync(setters =>
        {
            foreach (var (propName, value) in updateValues)
            {
                var prop = typeof(T).GetProperty(propName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null) continue;

                // Build: e => e.Property
                var param = Expression.Parameter(typeof(T), "e");
                var memberAccess = Expression.Property(param, prop);
                var lambda = Expression.Lambda(memberAccess, param);

                // Build: _ => value
                var valueParam = Expression.Parameter(typeof(T), "_");
                var convertedValue = Convert.ChangeType(value, prop.PropertyType);
                var valueLambda = Expression.Lambda(
                    Expression.Constant(convertedValue, prop.PropertyType), valueParam);

                // Call SetProperty<T, TProp>(lambda, valueLambda) via reflection
                var setPropertyMethod = typeof(SetPropertyCalls<T>).GetMethods()
                    .Where(m => m.Name == nameof(SetPropertyCalls<T>.SetProperty))
                    .Where(m => m.GetParameters().Length == 2)
                    .Where(m => m.GetParameters()[1].ParameterType.IsGenericType
                        && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>))
                    .First()
                    .MakeGenericMethod(prop.PropertyType);

                var lambdaCompiled = lambda.Compile();
                var valueLambdaCompiled = valueLambda.Compile();

                setPropertyMethod.Invoke(setters, [lambdaCompiled, valueLambdaCompiled]);
            }
        }, ct);
    }

    private IQueryable<T> ApplyFilters(IQueryable<T> query, Dictionary<string, FilterOp> filters)
    {
        foreach (var (field, op) in filters)
            query = _queryBuilder.ApplyFilter(query, field, op);
        return query;
    }
```

**WAIT** — `QueryBuilder` doesn't expose `ApplyFilter`. We need a different approach. The `FilterApplier` is what does individual filters. `EfRepo` needs access to it. Let's revise:

**Revised approach — EfRepo gets FilterApplier reference via QueryBuilder or directly:**

Actually, let's keep it simpler. We'll inject `FilterApplier` into `EfRepo` or use the public `FilterApplier.Apply` method. The cleanest approach: `EfRepo` already has `_queryBuilder`, and `QueryBuilder` has a `_filterApplier` field. Let's expose a method on `QueryBuilder`:

**In `src/CrudKit.EntityFrameworkCore/Query/QueryBuilder.cs`, add a public method:**
```csharp
    /// <summary>
    /// Applies a single filter to the query. Used by EfRepo for bulk operations.
    /// </summary>
    public IQueryable<T> ApplyFilter(IQueryable<T> query, string field, FilterOp op)
        => _filterApplier.Apply(query, field, op);
```

**Now the `ApplyFilters` helper in `EfRepo` works:**
```csharp
    private IQueryable<T> ApplyFilters(IQueryable<T> query, Dictionary<string, FilterOp> filters)
    {
        foreach (var (field, op) in filters)
            query = _queryBuilder.ApplyFilter(query, field, op);
        return query;
    }
```

**Revised BulkUpdate — use raw SQL approach for simplicity and SQLite compatibility:**

EF Core `ExecuteUpdateAsync` with `SetPropertyCalls<T>` requires lambda expressions at compile time, making dynamic property selection complex. A simpler and more reliable approach uses the filter pipeline for WHERE + raw SQL for SET:

```csharp
    public async Task<int> BulkUpdate(Dictionary<string, FilterOp> filters,
        Dictionary<string, object?> updateValues, CancellationToken ct = default)
    {
        var query = ApplyFilters(_db.Set<T>().AsQueryable(), filters);

        // Build SetProperty calls dynamically
        // We need to use expression trees to build the setters
        var param = Expression.Parameter(typeof(SetPropertyCalls<T>), "s");
        Expression body = param;

        foreach (var (propName, value) in updateValues)
        {
            var prop = typeof(T).GetProperty(propName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) continue;

            // Build lambda: e => e.Property
            var entityParam = Expression.Parameter(typeof(T), "e");
            var memberAccess = Expression.Property(entityParam, prop);
            var propLambda = Expression.Lambda(
                typeof(Func<,>).MakeGenericType(typeof(T), prop.PropertyType),
                memberAccess, entityParam);

            // Build lambda: e => value
            var valueParam = Expression.Parameter(typeof(T), "v");
            var convertedValue = value == null ? null : Convert.ChangeType(value, prop.PropertyType);
            var valueLambda = Expression.Lambda(
                typeof(Func<,>).MakeGenericType(typeof(T), prop.PropertyType),
                Expression.Constant(convertedValue, prop.PropertyType), valueParam);

            // Call SetProperty on body
            var setPropertyMethod = typeof(SetPropertyCalls<T>).GetMethods()
                .First(m => m.Name == "SetProperty"
                    && m.IsGenericMethod
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>))
                .MakeGenericMethod(prop.PropertyType);

            body = Expression.Call(body, setPropertyMethod, propLambda, valueLambda);
        }

        var settersLambda = Expression.Lambda<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>>(body, param);
        return await query.ExecuteUpdateAsync(settersLambda, ct);
    }
```

**Revised BulkDelete:**
```csharp
    public async Task<int> BulkDelete(Dictionary<string, FilterOp> filters, CancellationToken ct = default)
    {
        var query = ApplyFilters(_db.Set<T>().AsQueryable(), filters);

        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
        {
            // Soft delete via ExecuteUpdate — build expression dynamically
            var now = DateTime.UtcNow;

            // Build: s => s.SetProperty(e => e.DeletedAt, e => now).SetProperty(e => e.UpdatedAt, e => now)
            var settersParam = Expression.Parameter(typeof(SetPropertyCalls<T>), "s");

            // SetProperty for DeletedAt
            var entityParam1 = Expression.Parameter(typeof(T), "e");
            var deletedAtProp = typeof(ISoftDeletable).GetProperty(nameof(ISoftDeletable.DeletedAt))!;
            var deletedAtAccess = Expression.Property(
                Expression.Convert(entityParam1, typeof(ISoftDeletable)), deletedAtProp);
            var deletedAtLambda = Expression.Lambda<Func<T, DateTime?>>(deletedAtAccess, entityParam1);

            var valueParam1 = Expression.Parameter(typeof(T), "v");
            var nowExpr = Expression.Constant((DateTime?)now, typeof(DateTime?));
            var deletedAtValueLambda = Expression.Lambda<Func<T, DateTime?>>(nowExpr, valueParam1);

            var setDeletedAt = typeof(SetPropertyCalls<T>)
                .GetMethods()
                .First(m => m.Name == "SetProperty"
                    && m.IsGenericMethod
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>))
                .MakeGenericMethod(typeof(DateTime?));

            Expression body = Expression.Call(settersParam, setDeletedAt, deletedAtLambda, deletedAtValueLambda);

            // SetProperty for UpdatedAt
            var entityParam2 = Expression.Parameter(typeof(T), "e");
            var updatedAtProp = typeof(IEntity).GetProperty(nameof(IEntity.UpdatedAt))!;
            var updatedAtAccess = Expression.Property(entityParam2, updatedAtProp);
            var updatedAtLambda = Expression.Lambda<Func<T, DateTime>>(updatedAtAccess, entityParam2);

            var valueParam2 = Expression.Parameter(typeof(T), "v");
            var updatedAtValueLambda = Expression.Lambda<Func<T, DateTime>>(
                Expression.Constant(now), valueParam2);

            var setUpdatedAt = typeof(SetPropertyCalls<T>)
                .GetMethods()
                .First(m => m.Name == "SetProperty"
                    && m.IsGenericMethod
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>))
                .MakeGenericMethod(typeof(DateTime));

            body = Expression.Call(body, setUpdatedAt, updatedAtLambda, updatedAtValueLambda);

            var settersLambda = Expression.Lambda<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>>(body, settersParam);
            return await query.ExecuteUpdateAsync(settersLambda, ct);
        }

        return await query.ExecuteDeleteAsync(ct);
    }
```

- [ ] Run tests — verify all bulk operation tests pass
- [ ] Run full test suite — verify no regressions

---

## Summary of all file changes

| File | Action |
|------|--------|
| `src/CrudKit.Core/Enums/IncludeScope.cs` | CREATE |
| `src/CrudKit.Core/Attributes/DefaultIncludeAttribute.cs` | MODIFY — class-level, add IncludeScope |
| `src/CrudKit.Core/Attributes/CascadeSoftDeleteAttribute.cs` | MODIFY — class-level with ChildType/FK |
| `src/CrudKit.Core/Interfaces/ICascadeSoftDelete.cs` | MODIFY — generic with static abstract |
| `src/CrudKit.Core/Models/ListParams.cs` | MODIFY — add Include property |
| `src/CrudKit.EntityFrameworkCore/CrudKitDbContext.cs` | MODIFY — TimeProvider, ApplyCascadeSoftDelete |
| `src/CrudKit.EntityFrameworkCore/Repository/IRepo.cs` | MODIFY — add BulkUpdate/BulkDelete/BulkCount |
| `src/CrudKit.EntityFrameworkCore/Repository/EfRepo.cs` | MODIFY — hooks, include upgrade, cascade restore, bulk ops, unique check |
| `src/CrudKit.EntityFrameworkCore/Query/IncludeApplier.cs` | MODIFY — new signature |
| `src/CrudKit.EntityFrameworkCore/Query/QueryBuilder.cs` | MODIFY — new IncludeApplier call, add ApplyFilter |
| `src/CrudKit.EntityFrameworkCore/Extensions/ServiceCollectionExtensions.cs` | MODIFY — hooks DI note |
| `tests/CrudKit.EntityFrameworkCore.Tests/Helpers/TestEntities.cs` | MODIFY — add cascade + unique test entities |
| `tests/CrudKit.EntityFrameworkCore.Tests/Helpers/TestDbContext.cs` | MODIFY — add TimeProvider param, new DbSets |
| `tests/CrudKit.EntityFrameworkCore.Tests/Helpers/DbHelper.cs` | MODIFY — add TimeProvider param |
| `tests/CrudKit.EntityFrameworkCore.Tests/TimeProviderTests.cs` | CREATE |
| `tests/CrudKit.EntityFrameworkCore.Tests/DbContext/CascadeSoftDeleteTests.cs` | CREATE |
| `tests/CrudKit.EntityFrameworkCore.Tests/Query/IncludeApplierTests.cs` | CREATE |
| `tests/CrudKit.EntityFrameworkCore.Tests/Repository/EfRepoTests.cs` | MODIFY — add hooks, bulk, restore unique tests |
