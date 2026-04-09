# CrudKit v2 Features Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three features to CrudKit: (1) `existingEntity` parameter on hook update methods, (2) AggregateRoot hierarchy with domain events, (3) per-tenant auto-sequence generator.

**Architecture:** Feature 1 modifies the existing hook interfaces and endpoint mapper to pass the pre-update entity state. Feature 2 adds a parallel entity hierarchy (AggregateRoot variants) with domain event collection/dispatch in SaveChanges. Feature 3 adds a `[AutoSequence]` attribute, a `CrudKitSequence` DB table, and atomic increment logic in the EF layer.

**Tech Stack:** .NET 10, EF Core 10, xUnit, SQLite (tests), Minimal API

---

## Task 1: Hook `existingEntity` Parameter — Interface Changes

**Files:**
- Modify: `src/CrudKit.Core/Interfaces/IGlobalCrudHook.cs`
- Modify: `src/CrudKit.Core/Interfaces/ICrudHooks.cs`
- Test: `tests/CrudKit.Core.Tests/Interfaces/HookInterfaceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/CrudKit.Core.Tests/Interfaces/HookInterfaceTests.cs
using System.Reflection;
using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Core.Tests.Interfaces;

public class HookInterfaceTests
{
    [Fact]
    public void IGlobalCrudHook_HasBeforeUpdateWithExistingEntity()
    {
        var method = typeof(IGlobalCrudHook).GetMethod(
            "BeforeUpdate",
            [typeof(object), typeof(object), typeof(CrudKit.Core.Context.AppContext)]);
        Assert.NotNull(method);
    }

    [Fact]
    public void IGlobalCrudHook_HasAfterUpdateWithExistingEntity()
    {
        var method = typeof(IGlobalCrudHook).GetMethod(
            "AfterUpdate",
            [typeof(object), typeof(object), typeof(CrudKit.Core.Context.AppContext)]);
        Assert.NotNull(method);
    }

    [Fact]
    public void ICrudHooks_HasBeforeUpdateWithExistingEntity()
    {
        var method = typeof(ICrudHooks<>).GetMethod(
            "BeforeUpdate",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            [typeof(ICrudHooks<>).GetGenericArguments()[0],
             typeof(ICrudHooks<>).GetGenericArguments()[0],
             typeof(CrudKit.Core.Context.AppContext)],
            null);
        // Generic methods need different lookup — check by parameter count
        var methods = typeof(ICrudHooks<>).GetMethods()
            .Where(m => m.Name == "BeforeUpdate" && m.GetParameters().Length == 3);
        Assert.Single(methods);
    }

    [Fact]
    public void ICrudHooks_HasAfterUpdateWithExistingEntity()
    {
        var methods = typeof(ICrudHooks<>).GetMethods()
            .Where(m => m.Name == "AfterUpdate" && m.GetParameters().Length == 3);
        Assert.Single(methods);
    }

    [Fact]
    public void IGlobalCrudHook_DefaultImplementation_DoesNotThrow()
    {
        var hook = new TestGlobalHook();
        // 2-param overload (existing) should still work
        var task = hook.BeforeUpdate(new object(), new CrudKit.Core.Context.AppContext
        {
            Services = null!,
            CurrentUser = null!
        });
        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void IGlobalCrudHook_3ParamOverload_DefaultCallsTwoParam()
    {
        var hook = new TestGlobalHook();
        // 3-param overload should default to calling the 2-param version
        var task = hook.BeforeUpdate(new object(), new object(),
            new CrudKit.Core.Context.AppContext { Services = null!, CurrentUser = null! });
        Assert.True(task.IsCompleted);
    }

    private class TestGlobalHook : IGlobalCrudHook { }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CrudKit.Core.Tests/ --filter "HookInterfaceTests" -v m`
Expected: FAIL — methods with 3 parameters don't exist yet.

- [ ] **Step 3: Add overloads to IGlobalCrudHook**

```csharp
// src/CrudKit.Core/Interfaces/IGlobalCrudHook.cs
namespace CrudKit.Core.Interfaces;

/// <summary>
/// Global lifecycle hook that runs for ALL entities on every CRUD operation.
/// Use for cross-cutting concerns like search indexing, caching invalidation, event publishing.
/// Register via opts.UseGlobalHook&lt;T&gt;() or directly in DI as IGlobalCrudHook.
/// All methods have default empty implementations — override only what you need.
/// </summary>
public interface IGlobalCrudHook
{
    Task BeforeCreate(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task AfterCreate(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task BeforeUpdate(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called before an update is committed. Receives both the updated entity and its previous state.
    /// Default implementation delegates to the 2-parameter overload (ignoring existingEntity).
    /// </summary>
    Task BeforeUpdate(object entity, object? existingEntity, CrudKit.Core.Context.AppContext ctx)
        => BeforeUpdate(entity, ctx);

    Task AfterUpdate(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called after an update is committed. Receives both the updated entity and its previous state.
    /// Default implementation delegates to the 2-parameter overload (ignoring existingEntity).
    /// </summary>
    Task AfterUpdate(object entity, object? existingEntity, CrudKit.Core.Context.AppContext ctx)
        => AfterUpdate(entity, ctx);

    Task BeforeDelete(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task AfterDelete(object entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
}
```

- [ ] **Step 4: Add overloads to ICrudHooks\<T\>**

```csharp
// src/CrudKit.Core/Interfaces/ICrudHooks.cs
using CrudKit.Core.Context;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Entity lifecycle hooks. All methods have empty default implementations.
/// Override only the hooks you need.
/// Execution order: Validate → Before* → DB op → After*
/// </summary>
public interface ICrudHooks<T> where T : class, IAuditableEntity
{
    Task BeforeCreate(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task AfterCreate(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task BeforeUpdate(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called before an update is committed. Receives both the updated entity and its previous state.
    /// Default implementation delegates to the 2-parameter overload (ignoring existingEntity).
    /// </summary>
    Task BeforeUpdate(T entity, T? existingEntity, CrudKit.Core.Context.AppContext ctx)
        => BeforeUpdate(entity, ctx);

    Task AfterUpdate(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called after an update is committed. Receives both the updated entity and its previous state.
    /// Default implementation delegates to the 2-parameter overload (ignoring existingEntity).
    /// </summary>
    Task AfterUpdate(T entity, T? existingEntity, CrudKit.Core.Context.AppContext ctx)
        => AfterUpdate(entity, ctx);

    Task BeforeDelete(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task AfterDelete(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task BeforeRestore(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;
    Task AfterRestore(T entity, CrudKit.Core.Context.AppContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Applies additional filters to List and FindById queries.
    /// Use for row-level security filtering.
    /// Default: returns the query unchanged.
    /// </summary>
    IQueryable<T> ApplyScope(IQueryable<T> query, CrudKit.Core.Context.AppContext ctx) => query;

    /// <summary>
    /// Customizes EF Core Include() calls for complex include scenarios (e.g., ThenInclude).
    /// Applied before [DefaultInclude] attributes.
    /// Default: returns the query unchanged.
    /// </summary>
    IQueryable<T> ApplyIncludes(IQueryable<T> query) => query;
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/CrudKit.Core.Tests/ --filter "HookInterfaceTests" -v m`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/CrudKit.Core/Interfaces/IGlobalCrudHook.cs src/CrudKit.Core/Interfaces/ICrudHooks.cs tests/CrudKit.Core.Tests/Interfaces/HookInterfaceTests.cs
git commit -m "feat: add existingEntity parameter overloads to hook interfaces"
```

---

## Task 2: Hook `existingEntity` — Endpoint Mapper Integration

**Files:**
- Modify: `src/CrudKit.Api/Endpoints/CrudEndpointMapper.cs` (update handler, ~lines 477-510)
- Test: `tests/CrudKit.Api.Tests/Hooks/ExistingEntityHookTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/CrudKit.Api.Tests/Hooks/ExistingEntityHookTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AppContext = CrudKit.Core.Context.AppContext;

namespace CrudKit.Api.Tests.Hooks;

public class ExistingEntityHookTests
{
    /// <summary>Captures the existing entity passed to BeforeUpdate.</summary>
    public class ExistingEntityTracker : IGlobalCrudHook
    {
        public object? CapturedExisting;
        public object? CapturedCurrent;

        public Task BeforeUpdate(object entity, object? existingEntity, AppContext ctx)
        {
            CapturedCurrent = entity;
            CapturedExisting = existingEntity;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task BeforeUpdate_ReceivesExistingEntity_WithOldValues()
    {
        var tracker = new ExistingEntityTracker();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc => svc.AddScoped<IGlobalCrudHook>(_ => tracker),
            configureEndpoints: web =>
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        // Create
        var createResp = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Original", Price = 10.0 });
        var created = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        // Update
        await app.Client.PutAsJsonAsync($"/api/products/{id}", new { Name = "Updated", Price = 20.0 });

        // existingEntity should have the OLD values
        Assert.NotNull(tracker.CapturedExisting);
        var existing = (ProductEntity)tracker.CapturedExisting;
        Assert.Equal("Original", existing.Name);
        Assert.Equal(10.0m, existing.Price);

        // entity should have the NEW values
        var current = (ProductEntity)tracker.CapturedCurrent!;
        Assert.Equal("Updated", current.Name);
        Assert.Equal(20.0m, current.Price);
    }

    /// <summary>Captures the existing entity passed to entity-specific hooks.</summary>
    public class TypedExistingEntityTracker : ICrudHooks<ProductEntity>
    {
        public ProductEntity? CapturedExisting;

        public Task BeforeUpdate(ProductEntity entity, ProductEntity? existingEntity, AppContext ctx)
        {
            CapturedExisting = existingEntity;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TypedHook_BeforeUpdate_ReceivesExistingEntity()
    {
        var tracker = new TypedExistingEntityTracker();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc =>
                svc.AddScoped<ICrudHooks<ProductEntity>>(_ => tracker),
            configureEndpoints: web =>
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var createResp = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Before", Price = 5.0 });
        var created = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        await app.Client.PutAsJsonAsync($"/api/products/{id}", new { Name = "After" });

        Assert.NotNull(tracker.CapturedExisting);
        Assert.Equal("Before", tracker.CapturedExisting!.Name);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CrudKit.Api.Tests/ --filter "ExistingEntityHookTests" -v m`
Expected: FAIL — CapturedExisting is null because the endpoint mapper doesn't pass it yet.

- [ ] **Step 3: Modify endpoint mapper to capture and pass existingEntity**

In `src/CrudKit.Api/Endpoints/CrudEndpointMapper.cs`, the PUT handler (around line 477) currently does:

```csharp
var entity = await repo.Update(guid, dto, ct);
```

Change the update block to capture the existing entity before the update:

```csharp
// PUT /api/{route}/{id} — Update
group.MapPut("/{id}", async (string id, TUpdate dto, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
{
    var guid = ParseGuid(id, typeof(TEntity).Name);
    var db = CrudEndpointMapper.ResolveDbContextFor<TEntity>(httpCtx.RequestServices);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    try
    {
        var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
        var globalHooks = httpCtx.RequestServices.GetServices<IGlobalCrudHook>().ToList();

        // Capture existing entity state before update (detached snapshot)
        TEntity? existingEntity = null;
        if (globalHooks.Count > 0 || hooks != null)
        {
            var tracked = await db.Set<TEntity>().AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == guid, ct);
            existingEntity = tracked;
        }

        var entity = await repo.Update(guid, dto, ct);

        var appCtx = BuildAppContext(httpCtx);

        // Global before hooks run first — pass existingEntity
        foreach (var gh in globalHooks)
            await gh.BeforeUpdate(entity, existingEntity, appCtx);

        if (hooks != null)
        {
            await hooks.BeforeUpdate(entity, existingEntity, appCtx);
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);

        if (hooks != null)
            await hooks.AfterUpdate(entity, existingEntity, appCtx);

        // Global after hooks run last — pass existingEntity
        foreach (var gh in globalHooks)
            await gh.AfterUpdate(entity, existingEntity, appCtx);

        return Results.Ok(entity);
    }
    catch
    {
        await tx.RollbackAsync(ct);
        throw;
    }
})
```

Note: The `AsNoTracking()` query produces a detached snapshot so EF tracking isn't affected. The `FirstOrDefaultAsync` uses the entity's `Id` property via the `IEntity<TKey>` constraint — use `EF.Property<Guid>(e, "Id") == guid` or the expression `e => e.Id == guid` (already available since TEntity : IAuditableEntity which extends IEntity<Guid>).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/CrudKit.Api.Tests/ --filter "ExistingEntityHookTests" -v m`
Expected: PASS

- [ ] **Step 5: Run all existing hook tests to ensure no regressions**

Run: `dotnet test tests/CrudKit.Api.Tests/ --filter "GlobalHookTests" -v m`
Expected: All 6 existing tests PASS (default implementations delegate to 2-param overload).

- [ ] **Step 6: Commit**

```bash
git add src/CrudKit.Api/Endpoints/CrudEndpointMapper.cs tests/CrudKit.Api.Tests/Hooks/ExistingEntityHookTests.cs
git commit -m "feat: pass existingEntity to hook update methods in endpoint mapper"
```

---

## Task 3: Domain Events — Core Interfaces

**Files:**
- Create: `src/CrudKit.Core/Events/IDomainEvent.cs`
- Create: `src/CrudKit.Core/Events/IHasDomainEvents.cs`
- Create: `src/CrudKit.Core/Events/IDomainEventHandler.cs`
- Create: `src/CrudKit.Core/Events/IDomainEventDispatcher.cs`
- Test: `tests/CrudKit.Core.Tests/Events/DomainEventInterfaceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/CrudKit.Core.Tests/Events/DomainEventInterfaceTests.cs
using CrudKit.Core.Events;
using Xunit;

namespace CrudKit.Core.Tests.Events;

public class DomainEventInterfaceTests
{
    private record TestEvent(string Message) : IDomainEvent;

    private class TestHandler : IDomainEventHandler<TestEvent>
    {
        public TestEvent? Received;
        public Task HandleAsync(TestEvent domainEvent, CancellationToken ct = default)
        {
            Received = domainEvent;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void IDomainEvent_IsMarkerInterface()
    {
        var members = typeof(IDomainEvent).GetMembers();
        // Marker interface — no members
        Assert.Empty(members);
    }

    [Fact]
    public void IHasDomainEvents_ExposesRequiredMembers()
    {
        var type = typeof(IHasDomainEvents);
        Assert.NotNull(type.GetMethod("AddDomainEvent"));
        Assert.NotNull(type.GetMethod("ClearDomainEvents"));
        Assert.NotNull(type.GetProperty("DomainEvents"));
    }

    [Fact]
    public void IDomainEventHandler_HasHandleAsyncMethod()
    {
        var method = typeof(IDomainEventHandler<TestEvent>).GetMethod("HandleAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
    }

    [Fact]
    public void IDomainEventDispatcher_HasDispatchAsyncMethod()
    {
        var method = typeof(IDomainEventDispatcher).GetMethod("DispatchAsync");
        Assert.NotNull(method);
    }

    [Fact]
    public async Task TestHandler_ReceivesEvent()
    {
        var handler = new TestHandler();
        var ev = new TestEvent("hello");
        await handler.HandleAsync(ev);
        Assert.Equal("hello", handler.Received?.Message);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CrudKit.Core.Tests/ --filter "DomainEventInterfaceTests" -v m`
Expected: FAIL — namespaces and types don't exist yet.

- [ ] **Step 3: Create IDomainEvent**

```csharp
// src/CrudKit.Core/Events/IDomainEvent.cs
namespace CrudKit.Core.Events;

/// <summary>
/// Marker interface for domain events raised by aggregate roots.
/// </summary>
public interface IDomainEvent;
```

- [ ] **Step 4: Create IHasDomainEvents**

```csharp
// src/CrudKit.Core/Events/IHasDomainEvents.cs
namespace CrudKit.Core.Events;

/// <summary>
/// Implemented by entities (typically aggregate roots) that collect domain events.
/// Events are dispatched automatically during SaveChanges when a dispatcher is registered.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void AddDomainEvent(IDomainEvent domainEvent);
    void ClearDomainEvents();
}
```

- [ ] **Step 5: Create IDomainEventHandler**

```csharp
// src/CrudKit.Core/Events/IDomainEventHandler.cs
namespace CrudKit.Core.Events;

/// <summary>
/// Handles a specific domain event type.
/// Register implementations in DI or use assembly scanning via UseDomainEvents().
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct = default);
}
```

- [ ] **Step 6: Create IDomainEventDispatcher**

```csharp
// src/CrudKit.Core/Events/IDomainEventDispatcher.cs
namespace CrudKit.Core.Events;

/// <summary>
/// Dispatches collected domain events to their handlers.
/// CrudKit provides a default implementation; override via UseDomainEvents&lt;TDispatcher&gt;().
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default);
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/CrudKit.Core.Tests/ --filter "DomainEventInterfaceTests" -v m`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/CrudKit.Core/Events/ tests/CrudKit.Core.Tests/Events/DomainEventInterfaceTests.cs
git commit -m "feat: add domain event interfaces — IDomainEvent, IHasDomainEvents, handler, dispatcher"
```

---

## Task 4: AggregateRoot Entity Hierarchy

**Files:**
- Create: `src/CrudKit.Core/Entities/AggregateRoot.cs`
- Create: `src/CrudKit.Core/Entities/AuditableAggregateRoot.cs`
- Create: `src/CrudKit.Core/Entities/FullAuditableAggregateRoot.cs`
- Test: `tests/CrudKit.Core.Tests/Entities/AggregateRootTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/CrudKit.Core.Tests/Entities/AggregateRootTests.cs
using CrudKit.Core.Entities;
using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Core.Tests.Entities;

public class AggregateRootTests
{
    private record OrderCreatedEvent(Guid OrderId) : IDomainEvent;
    private record OrderShippedEvent(Guid OrderId, DateTime ShippedAt) : IDomainEvent;

    private class TestAggregate : AggregateRoot
    {
        public string Name { get; set; } = "";

        public void Ship()
        {
            AddDomainEvent(new OrderShippedEvent(Id, DateTime.UtcNow));
        }
    }

    private class TestAuditableAggregate : AuditableAggregateRoot
    {
        public string Title { get; set; } = "";
    }

    private class TestFullAuditableAggregate : FullAuditableAggregateRoot
    {
        public string Code { get; set; } = "";
    }

    // --- AggregateRoot base ---

    [Fact]
    public void AggregateRoot_ImplementsIHasDomainEvents()
    {
        Assert.True(typeof(IHasDomainEvents).IsAssignableFrom(typeof(AggregateRoot)));
    }

    [Fact]
    public void AggregateRoot_InheritsFromEntity()
    {
        Assert.True(typeof(Entity).IsAssignableFrom(typeof(AggregateRoot)));
    }

    [Fact]
    public void AggregateRoot_StartsWithNoDomainEvents()
    {
        var agg = new TestAggregate();
        Assert.Empty(agg.DomainEvents);
    }

    [Fact]
    public void AddDomainEvent_AddsToCollection()
    {
        var agg = new TestAggregate { Id = Guid.NewGuid() };
        agg.Ship();
        Assert.Single(agg.DomainEvents);
        Assert.IsType<OrderShippedEvent>(agg.DomainEvents[0]);
    }

    [Fact]
    public void ClearDomainEvents_RemovesAll()
    {
        var agg = new TestAggregate { Id = Guid.NewGuid() };
        agg.Ship();
        agg.Ship();
        Assert.Equal(2, agg.DomainEvents.Count);
        agg.ClearDomainEvents();
        Assert.Empty(agg.DomainEvents);
    }

    [Fact]
    public void DomainEvents_IsReadOnly()
    {
        var agg = new TestAggregate();
        Assert.IsAssignableFrom<IReadOnlyList<IDomainEvent>>(agg.DomainEvents);
    }

    // --- AuditableAggregateRoot ---

    [Fact]
    public void AuditableAggregateRoot_InheritsFromAuditableEntity()
    {
        Assert.True(typeof(IAuditableEntity).IsAssignableFrom(typeof(AuditableAggregateRoot)));
    }

    [Fact]
    public void AuditableAggregateRoot_ImplementsIHasDomainEvents()
    {
        Assert.True(typeof(IHasDomainEvents).IsAssignableFrom(typeof(AuditableAggregateRoot)));
    }

    [Fact]
    public void AuditableAggregateRoot_HasAuditFields()
    {
        var agg = new TestAuditableAggregate();
        agg.CreatedAt = DateTime.UtcNow;
        agg.UpdatedAt = DateTime.UtcNow;
        Assert.NotEqual(default, agg.CreatedAt);
    }

    // --- FullAuditableAggregateRoot ---

    [Fact]
    public void FullAuditableAggregateRoot_ImplementsISoftDeletable()
    {
        Assert.True(typeof(ISoftDeletable).IsAssignableFrom(typeof(FullAuditableAggregateRoot)));
    }

    [Fact]
    public void FullAuditableAggregateRoot_ImplementsIHasDomainEvents()
    {
        Assert.True(typeof(IHasDomainEvents).IsAssignableFrom(typeof(FullAuditableAggregateRoot)));
    }

    [Fact]
    public void FullAuditableAggregateRoot_HasSoftDeleteField()
    {
        var agg = new TestFullAuditableAggregate();
        Assert.Null(agg.DeletedAt);
    }

    [Fact]
    public void FullAuditableAggregateRoot_CanAddDomainEvents()
    {
        var agg = new TestFullAuditableAggregate { Id = Guid.NewGuid() };
        agg.AddDomainEvent(new OrderCreatedEvent(agg.Id));
        Assert.Single(agg.DomainEvents);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CrudKit.Core.Tests/ --filter "AggregateRootTests" -v m`
Expected: FAIL — AggregateRoot classes don't exist.

- [ ] **Step 3: Create AggregateRoot base classes**

```csharp
// src/CrudKit.Core/Entities/AggregateRoot.cs
using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Entities;

/// <summary>
/// Base class for aggregate roots with domain event support.
/// Domain events are collected and dispatched automatically during SaveChanges.
/// </summary>
public abstract class AggregateRoot<TKey> : Entity<TKey>, IHasDomainEvents
    where TKey : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Aggregate root using the default <see cref="Guid"/> primary key.
/// </summary>
public abstract class AggregateRoot : AggregateRoot<Guid>, IEntity { }
```

- [ ] **Step 4: Create AuditableAggregateRoot**

```csharp
// src/CrudKit.Core/Entities/AuditableAggregateRoot.cs
using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Entities;

/// <summary>
/// Aggregate root with audit fields (CreatedAt, UpdatedAt) and domain event support.
/// </summary>
public abstract class AuditableAggregateRoot<TKey> : AuditableEntity<TKey>, IHasDomainEvents
    where TKey : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Auditable aggregate root using the default <see cref="Guid"/> primary key.
/// </summary>
public abstract class AuditableAggregateRoot : AuditableAggregateRoot<Guid>, IEntity, IAuditableEntity { }

/// <summary>
/// Auditable aggregate root with user tracking (CreatedBy, UpdatedBy) and domain events.
/// </summary>
public abstract class AuditableAggregateRootWithUser<TKey, TUser, TUserKey>
    : AuditableEntityWithUser<TKey, TUser, TUserKey>, IHasDomainEvents
    where TKey : notnull
    where TUser : class
    where TUserKey : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Auditable aggregate root with user tracking using default Guid keys.
/// </summary>
public abstract class AuditableAggregateRootWithUser<TUser>
    : AuditableAggregateRootWithUser<Guid, TUser, Guid>
    where TUser : class { }
```

- [ ] **Step 5: Create FullAuditableAggregateRoot**

```csharp
// src/CrudKit.Core/Entities/FullAuditableAggregateRoot.cs
using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Entities;

/// <summary>
/// Aggregate root with audit fields, soft delete, and domain event support.
/// </summary>
public abstract class FullAuditableAggregateRoot<TKey> : AuditableEntity<TKey>, ISoftDeletable, IHasDomainEvents
    where TKey : notnull
{
    public DateTime? DeletedAt { get; set; }

    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Full auditable aggregate root using the default <see cref="Guid"/> primary key.
/// </summary>
public abstract class FullAuditableAggregateRoot : FullAuditableAggregateRoot<Guid>, IEntity, IAuditableEntity { }

/// <summary>
/// Full auditable aggregate root with user tracking (CreatedBy, UpdatedBy, DeletedBy) and domain events.
/// </summary>
public abstract class FullAuditableAggregateRootWithUser<TKey, TUser, TUserKey>
    : AuditableEntityWithUser<TKey, TUser, TUserKey>, ISoftDeletable, IHasDomainEvents
    where TKey : notnull
    where TUser : class
    where TUserKey : notnull
{
    public DateTime? DeletedAt { get; set; }
    public TUserKey? DeletedById { get; set; }
    public TUser? DeletedBy { get; set; }

    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Full auditable aggregate root with user tracking using default Guid keys.
/// </summary>
public abstract class FullAuditableAggregateRootWithUser<TUser>
    : FullAuditableAggregateRootWithUser<Guid, TUser, Guid>
    where TUser : class { }
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/CrudKit.Core.Tests/ --filter "AggregateRootTests" -v m`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/CrudKit.Core/Entities/AggregateRoot.cs src/CrudKit.Core/Entities/AuditableAggregateRoot.cs src/CrudKit.Core/Entities/FullAuditableAggregateRoot.cs tests/CrudKit.Core.Tests/Entities/AggregateRootTests.cs
git commit -m "feat: add AggregateRoot entity hierarchy with domain event support"
```

---

## Task 5: Default Domain Event Dispatcher

**Files:**
- Create: `src/CrudKit.Api/Events/CrudKitEventDispatcher.cs`
- Test: `tests/CrudKit.Api.Tests/Events/CrudKitEventDispatcherTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/CrudKit.Api.Tests/Events/CrudKitEventDispatcherTests.cs
using CrudKit.Api.Events;
using CrudKit.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.Api.Tests.Events;

public class CrudKitEventDispatcherTests
{
    private record OrderPlacedEvent(Guid OrderId) : IDomainEvent;
    private record OrderCancelledEvent(Guid OrderId) : IDomainEvent;

    private class OrderPlacedHandler : IDomainEventHandler<OrderPlacedEvent>
    {
        public OrderPlacedEvent? Received;
        public Task HandleAsync(OrderPlacedEvent domainEvent, CancellationToken ct = default)
        {
            Received = domainEvent;
            return Task.CompletedTask;
        }
    }

    private class SecondOrderPlacedHandler : IDomainEventHandler<OrderPlacedEvent>
    {
        public bool WasCalled;
        public Task HandleAsync(OrderPlacedEvent domainEvent, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    private class OrderCancelledHandler : IDomainEventHandler<OrderCancelledEvent>
    {
        public bool WasCalled;
        public Task HandleAsync(OrderCancelledEvent domainEvent, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DispatchAsync_CallsCorrectHandler()
    {
        var handler = new OrderPlacedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler);
        var sp = services.BuildServiceProvider();

        var dispatcher = new CrudKitEventDispatcher(sp);
        var orderId = Guid.NewGuid();
        await dispatcher.DispatchAsync([new OrderPlacedEvent(orderId)]);

        Assert.NotNull(handler.Received);
        Assert.Equal(orderId, handler.Received!.OrderId);
    }

    [Fact]
    public async Task DispatchAsync_CallsMultipleHandlersForSameEvent()
    {
        var handler1 = new OrderPlacedHandler();
        var handler2 = new SecondOrderPlacedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler1);
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler2);
        var sp = services.BuildServiceProvider();

        var dispatcher = new CrudKitEventDispatcher(sp);
        await dispatcher.DispatchAsync([new OrderPlacedEvent(Guid.NewGuid())]);

        Assert.NotNull(handler1.Received);
        Assert.True(handler2.WasCalled);
    }

    [Fact]
    public async Task DispatchAsync_DispatchesMultipleEventTypes()
    {
        var placedHandler = new OrderPlacedHandler();
        var cancelledHandler = new OrderCancelledHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(placedHandler);
        services.AddSingleton<IDomainEventHandler<OrderCancelledEvent>>(cancelledHandler);
        var sp = services.BuildServiceProvider();

        var dispatcher = new CrudKitEventDispatcher(sp);
        await dispatcher.DispatchAsync([
            new OrderPlacedEvent(Guid.NewGuid()),
            new OrderCancelledEvent(Guid.NewGuid())
        ]);

        Assert.NotNull(placedHandler.Received);
        Assert.True(cancelledHandler.WasCalled);
    }

    [Fact]
    public async Task DispatchAsync_NoHandlerRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var dispatcher = new CrudKitEventDispatcher(sp);
        // Should not throw when no handler is registered for the event type
        await dispatcher.DispatchAsync([new OrderPlacedEvent(Guid.NewGuid())]);
    }

    [Fact]
    public async Task DispatchAsync_EmptyList_DoesNothing()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var dispatcher = new CrudKitEventDispatcher(sp);
        await dispatcher.DispatchAsync([]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CrudKit.Api.Tests/ --filter "CrudKitEventDispatcherTests" -v m`
Expected: FAIL — CrudKitEventDispatcher doesn't exist.

- [ ] **Step 3: Implement CrudKitEventDispatcher**

```csharp
// src/CrudKit.Api/Events/CrudKitEventDispatcher.cs
using CrudKit.Core.Events;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Events;

/// <summary>
/// Default domain event dispatcher. Resolves IDomainEventHandler&lt;T&gt; from DI and invokes them.
/// Override by registering a custom IDomainEventDispatcher.
/// </summary>
public class CrudKitEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public CrudKitEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
    {
        foreach (var domainEvent in events)
        {
            var eventType = domainEvent.GetType();
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var handlers = _serviceProvider.GetServices(handlerType);

            foreach (var handler in handlers)
            {
                await (Task)handlerType
                    .GetMethod("HandleAsync")!
                    .Invoke(handler, [domainEvent, ct])!;
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/CrudKit.Api.Tests/ --filter "CrudKitEventDispatcherTests" -v m`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/CrudKit.Api/Events/CrudKitEventDispatcher.cs tests/CrudKit.Api.Tests/Events/CrudKitEventDispatcherTests.cs
git commit -m "feat: add CrudKitEventDispatcher — default domain event dispatcher"
```

---

## Task 6: Domain Event Dispatch in SaveChanges

**Files:**
- Modify: `src/CrudKit.EntityFrameworkCore/CrudKitDbContextHelper.cs` (SaveChanges + SaveChangesAsync)
- Modify: `src/CrudKit.EntityFrameworkCore/CrudKitDbContext.cs` (pass dispatcher)
- Modify: `src/CrudKit.Identity/CrudKitIdentityDbContext.cs` (pass dispatcher)
- Test: `tests/CrudKit.Api.Tests/Events/DomainEventDispatchIntegrationTests.cs`

- [ ] **Step 1: Write the failing integration test**

```csharp
// tests/CrudKit.Api.Tests/Events/DomainEventDispatchIntegrationTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Entities;
using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AppContext = CrudKit.Core.Context.AppContext;

namespace CrudKit.Api.Tests.Events;

// --- Test entities ---

public class AggregateOrderEntity : AuditableAggregateRoot, IStateMachine<AggregateOrderStatus>
{
    public string Customer { get; set; } = string.Empty;
    public AggregateOrderStatus Status { get; set; } = AggregateOrderStatus.Pending;

    public static IReadOnlyList<(AggregateOrderStatus From, AggregateOrderStatus To, string Action)> Transitions =>
    [
        (AggregateOrderStatus.Pending, AggregateOrderStatus.Confirmed, "confirm"),
    ];
}

public enum AggregateOrderStatus { Pending, Confirmed }

public record OrderConfirmedEvent(Guid OrderId) : IDomainEvent;

public class CreateAggregateOrderDto
{
    public string Customer { get; set; } = string.Empty;
}

public class UpdateAggregateOrderDto
{
    public string? Customer { get; set; }
}

// Hook that adds domain event on create
public class AggregateOrderHook : ICrudHooks<AggregateOrderEntity>
{
    public Task BeforeCreate(AggregateOrderEntity entity, AppContext ctx)
    {
        entity.AddDomainEvent(new OrderConfirmedEvent(entity.Id));
        return Task.CompletedTask;
    }
}

// Handler
public class OrderConfirmedHandler : IDomainEventHandler<OrderConfirmedEvent>
{
    public List<Guid> ReceivedIds { get; } = [];

    public Task HandleAsync(OrderConfirmedEvent domainEvent, CancellationToken ct = default)
    {
        ReceivedIds.Add(domainEvent.OrderId);
        return Task.CompletedTask;
    }
}

public class DomainEventDispatchIntegrationTests
{
    [Fact]
    public async Task DomainEvents_DispatchedAfterSaveChanges()
    {
        var handler = new OrderConfirmedHandler();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc =>
            {
                svc.AddScoped<ICrudHooks<AggregateOrderEntity>, AggregateOrderHook>();
                svc.AddSingleton<IDomainEventHandler<OrderConfirmedEvent>>(handler);
            },
            configureEndpoints: web =>
                web.MapCrudEndpoints<AggregateOrderEntity, CreateAggregateOrderDto, UpdateAggregateOrderDto>("aggregate-orders"),
            configureOptions: opts => opts.UseDomainEvents());

        var response = await app.Client.PostAsJsonAsync("/api/aggregate-orders",
            new { Customer = "Test Corp" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(handler.ReceivedIds);
    }

    [Fact]
    public async Task DomainEvents_ClearedAfterDispatch()
    {
        var handler = new OrderConfirmedHandler();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc =>
            {
                svc.AddScoped<ICrudHooks<AggregateOrderEntity>, AggregateOrderHook>();
                svc.AddSingleton<IDomainEventHandler<OrderConfirmedEvent>>(handler);
            },
            configureEndpoints: web =>
                web.MapCrudEndpoints<AggregateOrderEntity, CreateAggregateOrderDto, UpdateAggregateOrderDto>("aggregate-orders"),
            configureOptions: opts => opts.UseDomainEvents());

        // Create two orders — each should dispatch independently
        await app.Client.PostAsJsonAsync("/api/aggregate-orders", new { Customer = "A" });
        await app.Client.PostAsJsonAsync("/api/aggregate-orders", new { Customer = "B" });

        Assert.Equal(2, handler.ReceivedIds.Count);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CrudKit.Api.Tests/ --filter "DomainEventDispatchIntegrationTests" -v m`
Expected: FAIL — `UseDomainEvents()` doesn't exist. `AggregateOrderEntity` DbSet not registered.

- [ ] **Step 3: Add DbSet for test entity in ApiTestDbContext**

In `tests/CrudKit.Api.Tests/Helpers/ApiTestDbContext.cs`, add:

```csharp
public DbSet<AggregateOrderEntity> AggregateOrders => Set<AggregateOrderEntity>();
```

- [ ] **Step 4: Add UseDomainEvents to CrudKitApiOptions**

In `src/CrudKit.Api/Configuration/CrudKitApiOptions.cs`, add:

```csharp
/// <summary>Whether domain event dispatching is enabled.</summary>
public bool DomainEventsEnabled { get; private set; }

/// <summary>Custom dispatcher type, null means use default CrudKitEventDispatcher.</summary>
public Type? CustomDomainEventDispatcherType { get; private set; }

/// <summary>Assemblies to scan for IDomainEventHandler implementations.</summary>
public List<Assembly> DomainEventHandlerAssemblies { get; } = [];

/// <summary>
/// Enable domain event dispatching. Events added to IHasDomainEvents entities
/// are dispatched automatically after SaveChanges.
/// </summary>
public CrudKitApiOptions UseDomainEvents(Action<DomainEventOptions>? configure = null)
{
    DomainEventsEnabled = true;
    if (configure != null)
    {
        var eventOpts = new DomainEventOptions();
        configure(eventOpts);
        DomainEventHandlerAssemblies.AddRange(eventOpts.Assemblies);
    }
    return this;
}

/// <summary>
/// Enable domain events with a custom dispatcher implementation.
/// </summary>
public CrudKitApiOptions UseDomainEvents<TDispatcher>(Action<DomainEventOptions>? configure = null)
    where TDispatcher : class, IDomainEventDispatcher
{
    DomainEventsEnabled = true;
    CustomDomainEventDispatcherType = typeof(TDispatcher);
    if (configure != null)
    {
        var eventOpts = new DomainEventOptions();
        configure(eventOpts);
        DomainEventHandlerAssemblies.AddRange(eventOpts.Assemblies);
    }
    return this;
}
```

Add required `using` at top: `using System.Reflection;` and `using CrudKit.Core.Events;`

- [ ] **Step 5: Create DomainEventOptions**

```csharp
// src/CrudKit.Api/Configuration/DomainEventOptions.cs
using System.Reflection;

namespace CrudKit.Api.Configuration;

/// <summary>
/// Configuration for domain event handler scanning.
/// </summary>
public class DomainEventOptions
{
    internal List<Assembly> Assemblies { get; } = [];

    /// <summary>
    /// Scan the specified assembly for IDomainEventHandler implementations and register them in DI.
    /// </summary>
    public DomainEventOptions ScanHandlersFromAssembly(Assembly assembly)
    {
        Assemblies.Add(assembly);
        return this;
    }
}
```

- [ ] **Step 6: Register dispatcher and handlers in CrudKitAppExtensions**

In `src/CrudKit.Api/Extensions/CrudKitAppExtensions.cs`, after the global hooks registration block (around line 79), add:

```csharp
// Register domain event dispatcher
if (opts.DomainEventsEnabled)
{
    if (opts.CustomDomainEventDispatcherType != null)
        services.TryAddScoped(typeof(IDomainEventDispatcher), opts.CustomDomainEventDispatcherType);
    else
        services.TryAddScoped<IDomainEventDispatcher, CrudKitEventDispatcher>();

    // Assembly-scan for handlers
    foreach (var assembly in opts.DomainEventHandlerAssemblies)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>))
                .Select(i => new { Interface = i, Implementation = t }));

        foreach (var pair in handlerTypes)
            services.AddScoped(pair.Interface, pair.Implementation);
    }
}
```

Add required `using`: `using CrudKit.Api.Events;` and `using CrudKit.Core.Events;`

- [ ] **Step 7: Propagate DomainEventsEnabled to CrudKitEfOptions**

In `src/CrudKit.EntityFrameworkCore/CrudKitEfOptions.cs`, add:

```csharp
public bool DomainEventsEnabled { get; set; }
```

In `src/CrudKit.Api/Extensions/CrudKitAppExtensions.cs`, add to the `CrudKitEfOptions` initialization:

```csharp
DomainEventsEnabled = opts.DomainEventsEnabled,
```

- [ ] **Step 8: Modify CrudKitDbContext to accept IDomainEventDispatcher**

In `src/CrudKit.EntityFrameworkCore/CrudKitDbContext.cs`, add constructor parameter and field:

```csharp
private readonly IDomainEventDispatcher? _domainEventDispatcher;
```

Add `IDomainEventDispatcher? domainEventDispatcher = null` as the last constructor parameter and store it.

Update `SaveChangesAsync` to pass dispatcher:
```csharp
public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    => await CrudKitDbContextHelper.SaveChangesAsync(
        this, base.SaveChangesAsync, acceptAllChangesOnSuccess,
        _currentUser, _tenantContext, _timeProvider ?? TimeProvider.System,
        _efOptions, _auditWriter, _domainEventDispatcher, ct);
```

Same for `SaveChanges`:
```csharp
public override int SaveChanges(bool acceptAllChangesOnSuccess)
    => CrudKitDbContextHelper.SaveChanges(
        this, base.SaveChanges, acceptAllChangesOnSuccess,
        _currentUser, _tenantContext, _timeProvider ?? TimeProvider.System,
        _efOptions, _auditWriter, _domainEventDispatcher);
```

Add `using CrudKit.Core.Events;`

- [ ] **Step 9: Modify CrudKitDbContextHelper to dispatch domain events**

In `src/CrudKit.EntityFrameworkCore/CrudKitDbContextHelper.cs`:

Update `SaveChangesAsync` signature to add `IDomainEventDispatcher? dispatcher`:
```csharp
public static async Task<int> SaveChangesAsync(
    ICrudKitDbContext context,
    Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
    bool acceptAllChangesOnSuccess,
    ICurrentUser currentUser,
    ITenantContext? tenantContext,
    TimeProvider timeProvider,
    CrudKitEfOptions? efOptions,
    IAuditWriter? auditWriter,
    IDomainEventDispatcher? domainEventDispatcher,
    CancellationToken ct = default)
```

After the `baseSaveChangesAsync()` call and cascade ops, add domain event dispatch:

```csharp
// Dispatch domain events after successful save
if (domainEventDispatcher != null)
{
    var entitiesWithEvents = context.ChangeTracker.Entries<IHasDomainEvents>()
        .Where(e => e.Entity.DomainEvents.Count > 0)
        .Select(e => e.Entity)
        .ToList();

    var domainEvents = entitiesWithEvents
        .SelectMany(e => e.DomainEvents)
        .ToList();

    entitiesWithEvents.ForEach(e => e.ClearDomainEvents());

    if (domainEvents.Count > 0)
        await domainEventDispatcher.DispatchAsync(domainEvents, ct);
}
```

Add `using CrudKit.Core.Events;`

Do the same for the sync `SaveChanges` method (fire-and-forget or `.GetAwaiter().GetResult()` for the sync path).

- [ ] **Step 10: Modify CrudKitIdentityDbContext to accept IDomainEventDispatcher**

In `src/CrudKit.Identity/CrudKitIdentityDbContext.cs`, apply the same changes as CrudKitDbContext — add `IDomainEventDispatcher? domainEventDispatcher = null` to all constructor overloads, pass to helper.

- [ ] **Step 11: Run test to verify it passes**

Run: `dotnet test tests/CrudKit.Api.Tests/ --filter "DomainEventDispatchIntegrationTests" -v m`
Expected: PASS

- [ ] **Step 12: Run full test suite to check for regressions**

Run: `dotnet test CrudKit.slnx -v m`
Expected: All existing tests PASS — new dispatcher parameter is optional (null default).

- [ ] **Step 13: Commit**

```bash
git add src/CrudKit.Api/Configuration/CrudKitApiOptions.cs src/CrudKit.Api/Configuration/DomainEventOptions.cs src/CrudKit.Api/Extensions/CrudKitAppExtensions.cs src/CrudKit.EntityFrameworkCore/CrudKitEfOptions.cs src/CrudKit.EntityFrameworkCore/CrudKitDbContext.cs src/CrudKit.EntityFrameworkCore/CrudKitDbContextHelper.cs src/CrudKit.Identity/CrudKitIdentityDbContext.cs tests/CrudKit.Api.Tests/Helpers/ApiTestDbContext.cs tests/CrudKit.Api.Tests/Events/DomainEventDispatchIntegrationTests.cs
git commit -m "feat: dispatch domain events from SaveChanges with UseDomainEvents() configuration"
```

---

## Task 7: AutoSequence — Attribute and Sequence Entity

**Files:**
- Create: `src/CrudKit.Core/Attributes/AutoSequenceAttribute.cs`
- Create: `src/CrudKit.EntityFrameworkCore/Sequencing/CrudKitSequence.cs`
- Create: `src/CrudKit.EntityFrameworkCore/Sequencing/SequenceGenerator.cs`
- Test: `tests/CrudKit.Core.Tests/Attributes/AutoSequenceAttributeTests.cs`
- Test: `tests/CrudKit.EntityFrameworkCore.Tests/Sequencing/SequenceGeneratorTests.cs`

- [ ] **Step 1: Write attribute test**

```csharp
// tests/CrudKit.Core.Tests/Attributes/AutoSequenceAttributeTests.cs
using System.Reflection;
using CrudKit.Core.Attributes;
using Xunit;

namespace CrudKit.Core.Tests.Attributes;

public class AutoSequenceAttributeTests
{
    private class TestInvoice
    {
        [AutoSequence("INV-{year}-{seq:5}")]
        public string InvoiceNumber { get; set; } = "";

        public string Name { get; set; } = "";
    }

    [Fact]
    public void AutoSequence_StoresTemplate()
    {
        var prop = typeof(TestInvoice).GetProperty("InvoiceNumber");
        var attr = prop!.GetCustomAttribute<AutoSequenceAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("INV-{year}-{seq:5}", attr!.Template);
    }

    [Fact]
    public void AutoSequence_NotPresentOnOtherProperties()
    {
        var prop = typeof(TestInvoice).GetProperty("Name");
        var attr = prop!.GetCustomAttribute<AutoSequenceAttribute>();
        Assert.Null(attr);
    }

    [Fact]
    public void AutoSequence_TargetsProperties()
    {
        var attrUsage = typeof(AutoSequenceAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(attrUsage);
        Assert.True(attrUsage!.ValidOn.HasFlag(AttributeTargets.Property));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CrudKit.Core.Tests/ --filter "AutoSequenceAttributeTests" -v m`
Expected: FAIL — attribute doesn't exist.

- [ ] **Step 3: Create AutoSequenceAttribute**

```csharp
// src/CrudKit.Core/Attributes/AutoSequenceAttribute.cs
namespace CrudKit.Core.Attributes;

/// <summary>
/// Marks a string property for automatic sequence number generation.
/// The value is set automatically during entity creation (BeforeSave).
/// Template tokens: {year}, {month}, {day}, {seq:N} where N is zero-padding width.
/// Sequences are scoped per tenant + entity type + resolved prefix.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class AutoSequenceAttribute : Attribute
{
    public string Template { get; }

    /// <param name="template">Sequence template, e.g. "INV-{year}-{seq:5}"</param>
    public AutoSequenceAttribute(string template)
    {
        Template = template;
    }
}
```

- [ ] **Step 4: Run attribute test to verify it passes**

Run: `dotnet test tests/CrudKit.Core.Tests/ --filter "AutoSequenceAttributeTests" -v m`
Expected: PASS

- [ ] **Step 5: Create CrudKitSequence entity**

```csharp
// src/CrudKit.EntityFrameworkCore/Sequencing/CrudKitSequence.cs
namespace CrudKit.EntityFrameworkCore.Sequencing;

/// <summary>
/// Tracks the current sequence value per entity type, tenant, and prefix.
/// Used by the AutoSequence attribute for atomic number generation.
/// </summary>
public class CrudKitSequence
{
    public Guid Id { get; set; }

    /// <summary>Entity type name, e.g. "Invoice".</summary>
    public string EntityType { get; set; } = "";

    /// <summary>Tenant ID for multi-tenant isolation. Empty string for non-tenant scenarios.</summary>
    public string TenantId { get; set; } = "";

    /// <summary>Resolved prefix from template, e.g. "INV-2026" for "INV-{year}-{seq:5}".</summary>
    public string Prefix { get; set; } = "";

    /// <summary>Current sequence counter value.</summary>
    public long CurrentValue { get; set; }
}
```

- [ ] **Step 6: Write SequenceGenerator tests**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/Sequencing/SequenceGeneratorTests.cs
using CrudKit.EntityFrameworkCore.Sequencing;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Sequencing;

public class SequenceGeneratorTests
{
    [Theory]
    [InlineData("INV-{year}-{seq:5}", "INV-2026-", 5)]
    [InlineData("SO-{year}{month}-{seq:4}", "SO-202604-", 4)]
    [InlineData("WB-{seq:3}", "WB-", 3)]
    [InlineData("{year}/{month}/{day}-{seq:6}", "2026/04/09-", 6)]
    [InlineData("PAY-{seq:8}", "PAY-", 8)]
    public void ResolvePrefix_ParsesTemplateCorrectly(string template, string expectedPrefix, int expectedPadding)
    {
        var now = new DateOnly(2026, 4, 9);
        var (prefix, padding) = SequenceGenerator.ResolvePrefix(template, now);
        Assert.Equal(expectedPrefix, prefix);
        Assert.Equal(expectedPadding, padding);
    }

    [Theory]
    [InlineData("INV-2026-", 1, 5, "INV-2026-00001")]
    [InlineData("INV-2026-", 42, 5, "INV-2026-00042")]
    [InlineData("SO-202604-", 1, 4, "SO-202604-0001")]
    [InlineData("WB-", 999, 3, "WB-999")]
    [InlineData("PAY-", 1, 8, "PAY-00000001")]
    public void FormatSequenceValue_FormatsCorrectly(string prefix, long value, int padding, string expected)
    {
        var result = SequenceGenerator.FormatSequenceValue(prefix, value, padding);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolvePrefix_NoSeqToken_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            SequenceGenerator.ResolvePrefix("INV-{year}", new DateOnly(2026, 1, 1)));
    }
}
```

- [ ] **Step 7: Run test to verify it fails**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ --filter "SequenceGeneratorTests" -v m`
Expected: FAIL — SequenceGenerator doesn't exist.

- [ ] **Step 8: Implement SequenceGenerator**

```csharp
// src/CrudKit.EntityFrameworkCore/Sequencing/SequenceGenerator.cs
using System.Text.RegularExpressions;

namespace CrudKit.EntityFrameworkCore.Sequencing;

/// <summary>
/// Parses AutoSequence templates and generates formatted sequence values.
/// </summary>
public static partial class SequenceGenerator
{
    /// <summary>
    /// Resolves date tokens in the template and extracts the prefix (everything before {seq}).
    /// Returns the prefix and the zero-padding width.
    /// </summary>
    public static (string Prefix, int Padding) ResolvePrefix(string template, DateOnly date)
    {
        // Replace date tokens
        var resolved = template
            .Replace("{year}", date.Year.ToString())
            .Replace("{month}", date.Month.ToString("D2"))
            .Replace("{day}", date.Day.ToString("D2"));

        // Extract {seq:N} pattern
        var match = SeqPattern().Match(resolved);
        if (!match.Success)
            throw new ArgumentException($"Template must contain {{seq:N}} token. Got: '{template}'");

        var padding = int.Parse(match.Groups[1].Value);
        var prefix = resolved[..match.Index];

        return (prefix, padding);
    }

    /// <summary>
    /// Formats the final sequence string: prefix + zero-padded value.
    /// </summary>
    public static string FormatSequenceValue(string prefix, long value, int padding)
    {
        return $"{prefix}{value.ToString($"D{padding}")}";
    }

    [GeneratedRegex(@"\{seq:(\d+)\}")]
    private static partial Regex SeqPattern();
}
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ --filter "SequenceGeneratorTests" -v m`
Expected: PASS

Run: `dotnet test tests/CrudKit.Core.Tests/ --filter "AutoSequenceAttributeTests" -v m`
Expected: PASS

- [ ] **Step 10: Commit**

```bash
git add src/CrudKit.Core/Attributes/AutoSequenceAttribute.cs src/CrudKit.EntityFrameworkCore/Sequencing/CrudKitSequence.cs src/CrudKit.EntityFrameworkCore/Sequencing/SequenceGenerator.cs tests/CrudKit.Core.Tests/Attributes/AutoSequenceAttributeTests.cs tests/CrudKit.EntityFrameworkCore.Tests/Sequencing/SequenceGeneratorTests.cs
git commit -m "feat: add AutoSequence attribute, CrudKitSequence entity, and SequenceGenerator"
```

---

## Task 8: AutoSequence — DB Integration and ProcessBeforeSave

**Files:**
- Modify: `src/CrudKit.EntityFrameworkCore/CrudKitDbContextHelper.cs` (ConfigureModel + ProcessBeforeSave)
- Create: `src/CrudKit.EntityFrameworkCore/Sequencing/SequenceService.cs`
- Test: `tests/CrudKit.EntityFrameworkCore.Tests/Sequencing/SequenceServiceTests.cs`

- [ ] **Step 1: Write the integration test**

```csharp
// tests/CrudKit.EntityFrameworkCore.Tests/Sequencing/SequenceServiceTests.cs
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Sequencing;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Sequencing;

public class SequenceServiceTests
{
    private class SequenceTestEntity : IAuditableEntity
    {
        public Guid Id { get; set; }

        [AutoSequence("TST-{year}-{seq:4}")]
        public string Code { get; set; } = "";

        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    [Fact]
    public async Task NextValueAsync_FirstCall_Returns1()
    {
        using var ctx = DbHelper.CreateDb(b => b.Entity<CrudKitSequence>().ToTable("__crud_sequences"));
        var service = new SequenceService(ctx);

        var value = await service.NextValueAsync("TestEntity", "", "TST-2026-");
        Assert.Equal(1, value);
    }

    [Fact]
    public async Task NextValueAsync_SecondCall_Returns2()
    {
        using var ctx = DbHelper.CreateDb(b => b.Entity<CrudKitSequence>().ToTable("__crud_sequences"));
        var service = new SequenceService(ctx);

        await service.NextValueAsync("TestEntity", "", "TST-2026-");
        var second = await service.NextValueAsync("TestEntity", "", "TST-2026-");
        Assert.Equal(2, second);
    }

    [Fact]
    public async Task NextValueAsync_DifferentTenants_IndependentSequences()
    {
        using var ctx = DbHelper.CreateDb(b => b.Entity<CrudKitSequence>().ToTable("__crud_sequences"));
        var service = new SequenceService(ctx);

        var t1v1 = await service.NextValueAsync("Invoice", "tenant-a", "INV-2026-");
        var t2v1 = await service.NextValueAsync("Invoice", "tenant-b", "INV-2026-");
        var t1v2 = await service.NextValueAsync("Invoice", "tenant-a", "INV-2026-");

        Assert.Equal(1, t1v1);
        Assert.Equal(1, t2v1);  // Independent sequence
        Assert.Equal(2, t1v2);
    }

    [Fact]
    public async Task NextValueAsync_DifferentPrefixes_IndependentSequences()
    {
        using var ctx = DbHelper.CreateDb(b => b.Entity<CrudKitSequence>().ToTable("__crud_sequences"));
        var service = new SequenceService(ctx);

        var v2026 = await service.NextValueAsync("Invoice", "", "INV-2026-");
        var v2027 = await service.NextValueAsync("Invoice", "", "INV-2027-");

        Assert.Equal(1, v2026);
        Assert.Equal(1, v2027);  // New year = new sequence
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ --filter "SequenceServiceTests" -v m`
Expected: FAIL — SequenceService doesn't exist.

- [ ] **Step 3: Implement SequenceService**

```csharp
// src/CrudKit.EntityFrameworkCore/Sequencing/SequenceService.cs
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Sequencing;

/// <summary>
/// Provides atomic sequence number generation per entity type, tenant, and prefix.
/// Uses database-level locking to prevent duplicate numbers under concurrency.
/// </summary>
public class SequenceService
{
    private readonly DbContext _dbContext;

    public SequenceService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Gets the next sequence value atomically. Creates the sequence row if it doesn't exist.
    /// </summary>
    public async Task<long> NextValueAsync(
        string entityType,
        string tenantId,
        string prefix,
        CancellationToken ct = default)
    {
        var sequences = _dbContext.Set<CrudKitSequence>();

        var seq = await sequences.FirstOrDefaultAsync(
            s => s.EntityType == entityType && s.TenantId == tenantId && s.Prefix == prefix, ct);

        if (seq == null)
        {
            seq = new CrudKitSequence
            {
                Id = Guid.NewGuid(),
                EntityType = entityType,
                TenantId = tenantId,
                Prefix = prefix,
                CurrentValue = 1
            };
            sequences.Add(seq);
            await _dbContext.SaveChangesAsync(ct);
            return 1;
        }

        seq.CurrentValue++;
        await _dbContext.SaveChangesAsync(ct);
        return seq.CurrentValue;
    }
}
```

- [ ] **Step 4: Register CrudKitSequence table in ConfigureModel**

In `src/CrudKit.EntityFrameworkCore/CrudKitDbContextHelper.cs`, in the `ConfigureModel` method, add sequence table configuration:

```csharp
// Configure CrudKitSequence table
modelBuilder.Entity<CrudKitSequence>(b =>
{
    b.ToTable("__crud_sequences");
    b.HasKey(e => e.Id);
    b.Property(e => e.EntityType).HasMaxLength(200).IsRequired();
    b.Property(e => e.TenantId).HasMaxLength(200).IsRequired();
    b.Property(e => e.Prefix).HasMaxLength(200).IsRequired();
    b.HasIndex(e => new { e.EntityType, e.TenantId, e.Prefix }).IsUnique();
});
```

Add `using CrudKit.EntityFrameworkCore.Sequencing;`

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/CrudKit.EntityFrameworkCore.Tests/ --filter "SequenceServiceTests" -v m`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/CrudKit.EntityFrameworkCore/Sequencing/SequenceService.cs src/CrudKit.EntityFrameworkCore/CrudKitDbContextHelper.cs tests/CrudKit.EntityFrameworkCore.Tests/Sequencing/SequenceServiceTests.cs
git commit -m "feat: add SequenceService with atomic per-tenant sequence generation"
```

---

## Task 9: AutoSequence — Automatic Application in ProcessBeforeSave

**Files:**
- Modify: `src/CrudKit.EntityFrameworkCore/CrudKitDbContextHelper.cs` (ProcessBeforeSave)
- Test: `tests/CrudKit.Api.Tests/Sequencing/AutoSequenceEndpointTests.cs`

- [ ] **Step 1: Write the integration test**

```csharp
// tests/CrudKit.Api.Tests/Sequencing/AutoSequenceEndpointTests.cs
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Api.Tests.Sequencing;

public class SeqInvoiceEntity : IAuditableEntity
{
    public Guid Id { get; set; }

    [AutoSequence("INV-{year}-{seq:5}")]
    public string InvoiceNumber { get; set; } = "";

    public string Customer { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateSeqInvoiceDto
{
    [Required] public string Customer { get; set; } = "";
}

public class UpdateSeqInvoiceDto
{
    public string? Customer { get; set; }
}

public class AutoSequenceEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Create_AutoSequence_SetsNumberAutomatically()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
                web.MapCrudEndpoints<SeqInvoiceEntity, CreateSeqInvoiceDto, UpdateSeqInvoiceDto>("seq-invoices"));

        var response = await app.Client.PostAsJsonAsync("/api/seq-invoices",
            new { Customer = "Acme Corp" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var number = json.RootElement.GetProperty("invoiceNumber").GetString();

        Assert.NotNull(number);
        Assert.StartsWith($"INV-{DateTime.UtcNow.Year}-", number);
        Assert.Equal($"INV-{DateTime.UtcNow.Year}-00001", number);
    }

    [Fact]
    public async Task Create_AutoSequence_IncrementsSequentially()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
                web.MapCrudEndpoints<SeqInvoiceEntity, CreateSeqInvoiceDto, UpdateSeqInvoiceDto>("seq-invoices"));

        await app.Client.PostAsJsonAsync("/api/seq-invoices", new { Customer = "A" });
        var response2 = await app.Client.PostAsJsonAsync("/api/seq-invoices", new { Customer = "B" });
        var response3 = await app.Client.PostAsJsonAsync("/api/seq-invoices", new { Customer = "C" });

        var json2 = JsonDocument.Parse(await response2.Content.ReadAsStringAsync());
        var json3 = JsonDocument.Parse(await response3.Content.ReadAsStringAsync());

        Assert.Equal($"INV-{DateTime.UtcNow.Year}-00002",
            json2.RootElement.GetProperty("invoiceNumber").GetString());
        Assert.Equal($"INV-{DateTime.UtcNow.Year}-00003",
            json3.RootElement.GetProperty("invoiceNumber").GetString());
    }

    [Fact]
    public async Task Create_AutoSequence_DoesNotOverwriteExistingValue()
    {
        // If the user explicitly sets InvoiceNumber in the DTO, it should NOT be overwritten.
        // But since InvoiceNumber is not in CreateDto, this test verifies auto-generation.
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
                web.MapCrudEndpoints<SeqInvoiceEntity, CreateSeqInvoiceDto, UpdateSeqInvoiceDto>("seq-invoices"));

        var response = await app.Client.PostAsJsonAsync("/api/seq-invoices",
            new { Customer = "Test" });

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var number = json.RootElement.GetProperty("invoiceNumber").GetString();
        Assert.False(string.IsNullOrEmpty(number));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CrudKit.Api.Tests/ --filter "AutoSequenceEndpointTests" -v m`
Expected: FAIL — InvoiceNumber is empty because ProcessBeforeSave doesn't handle AutoSequence yet.

- [ ] **Step 3: Add DbSet for test entity in ApiTestDbContext**

In `tests/CrudKit.Api.Tests/Helpers/ApiTestDbContext.cs`, add:

```csharp
public DbSet<SeqInvoiceEntity> SeqInvoices => Set<SeqInvoiceEntity>();
```

Add `using CrudKit.Api.Tests.Sequencing;` at top.

- [ ] **Step 4: Add AutoSequence processing in ProcessBeforeSave**

In `src/CrudKit.EntityFrameworkCore/CrudKitDbContextHelper.cs`, in the `ProcessBeforeSave` method, after the existing entity processing (ID generation, timestamps), add for Added entries:

```csharp
// Process AutoSequence properties on new entities
var addedEntries = changeTracker.Entries()
    .Where(e => e.State == EntityState.Added)
    .ToList();

foreach (var entry in addedEntries)
{
    var entityType = entry.Entity.GetType();
    var seqProperties = entityType.GetProperties()
        .Where(p => p.GetCustomAttribute<AutoSequenceAttribute>() != null)
        .ToList();

    if (seqProperties.Count == 0) continue;

    foreach (var prop in seqProperties)
    {
        // Skip if already set (non-null, non-empty)
        var currentValue = prop.GetValue(entry.Entity) as string;
        if (!string.IsNullOrEmpty(currentValue)) continue;

        var attr = prop.GetCustomAttribute<AutoSequenceAttribute>()!;
        var now = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);
        var (prefix, padding) = SequenceGenerator.ResolvePrefix(attr.Template, now);
        var tenantId = tenantContext?.TenantId ?? "";
        var entityTypeName = entityType.Name;

        // Use SequenceService for atomic increment
        var dbContext = (DbContext)(object)changeTracker.Context;
        var seqService = new SequenceService(dbContext);
        var nextValue = seqService.NextValueAsync(entityTypeName, tenantId, prefix)
            .GetAwaiter().GetResult();

        var formatted = SequenceGenerator.FormatSequenceValue(prefix, nextValue, padding);
        prop.SetValue(entry.Entity, formatted);
    }
}
```

Add required usings: `using CrudKit.Core.Attributes;`, `using CrudKit.EntityFrameworkCore.Sequencing;`, `using System.Reflection;`

Note: `ProcessBeforeSave` is sync but `NextValueAsync` is async. Use `.GetAwaiter().GetResult()` here since we're already inside a sync context. For the async `SaveChangesAsync` path, the sequence is also resolved in `ProcessBeforeSave` which is called synchronously. This is acceptable for SQLite and most databases where the call is fast.

- [ ] **Step 5: Ensure ChangeTracker.Context is accessible**

The `ProcessBeforeSave` method receives `ChangeTracker changeTracker`. The `ChangeTracker` exposes `.Context` (added in EF Core 5+). Verify this compiles. If `ChangeTracker.Context` is internal, we may need to pass `DbContext` as an additional parameter.

Alternative: Change `ProcessBeforeSave` signature to also accept `DbContext context` if needed:

```csharp
public static List<(string Sql, object[] Params)> ProcessBeforeSave(
    ChangeTracker changeTracker,
    ICurrentUser currentUser,
    ITenantContext? tenantContext,
    TimeProvider timeProvider,
    DbContext? dbContext = null)  // Added for sequence support
```

Then use `dbContext` instead of `changeTracker.Context`.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/CrudKit.Api.Tests/ --filter "AutoSequenceEndpointTests" -v m`
Expected: PASS

- [ ] **Step 7: Run full test suite**

Run: `dotnet test CrudKit.slnx -v m`
Expected: All tests PASS.

- [ ] **Step 8: Commit**

```bash
git add src/CrudKit.EntityFrameworkCore/CrudKitDbContextHelper.cs tests/CrudKit.Api.Tests/Sequencing/AutoSequenceEndpointTests.cs tests/CrudKit.Api.Tests/Helpers/ApiTestDbContext.cs
git commit -m "feat: apply AutoSequence in ProcessBeforeSave — automatic per-tenant number generation"
```

---

## Task 10: Final Verification and Full Test Suite

**Files:** None (verification only)

- [ ] **Step 1: Build entire solution**

Run: `dotnet build CrudKit.slnx`
Expected: Build succeeded with 0 errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test CrudKit.slnx -v m`
Expected: All tests pass (existing + new tests).

- [ ] **Step 3: Verify new test count**

New tests added:
- `HookInterfaceTests`: 6 tests (Task 1)
- `ExistingEntityHookTests`: 2 tests (Task 2)
- `DomainEventInterfaceTests`: 5 tests (Task 3)
- `AggregateRootTests`: 12 tests (Task 4)
- `CrudKitEventDispatcherTests`: 5 tests (Task 5)
- `DomainEventDispatchIntegrationTests`: 2 tests (Task 6)
- `AutoSequenceAttributeTests`: 3 tests (Task 7)
- `SequenceGeneratorTests`: 7 tests (Task 7)
- `SequenceServiceTests`: 4 tests (Task 8)
- `AutoSequenceEndpointTests`: 3 tests (Task 9)

**Total new tests: ~49**

- [ ] **Step 4: Commit final state if any fixups were needed**

```bash
git add -A
git commit -m "chore: final verification — all tests passing"
```
