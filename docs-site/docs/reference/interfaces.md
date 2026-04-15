---
sidebar_position: 2
title: Interfaces
---

# Interfaces

## IEntity / IEntity\<TKey\>

Base interface for all entities. `IRepo<T>` constraint requires `IEntity`. Any entity implementing this can participate in CRUD operations.

```csharp
public interface IEntity<TKey> where TKey : notnull
{
    TKey Id { get; set; }
}

public interface IEntity : IEntity<Guid> { }
```

## IAuditableEntity

Extends `IEntity` with `CreatedAt` and `UpdatedAt` timestamps. Set automatically by `ProcessBeforeSave`.

```csharp
public interface IAuditableEntity : IEntity
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
```

## ISoftDeletable

Marks an entity as soft-deletable. `CrudKitDbContext` applies a global query filter excluding records where `DeletedAt != null`. Implemented by `FullAuditableEntity` and its variants.

```csharp
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
    Guid? DeleteBatchId { get; set; }
}
```

`DeleteBatchId` enables [smart cascade restore](../features/soft-delete#smart-cascade-restore) — children deleted individually keep their own batch ID and are not restored when the parent is restored.

## IMultiTenant / IMultiTenant\<TTenant\> / IMultiTenant\<TTenant, TTenantKey\>

Marks an entity as tenant-scoped. `CrudKitDbContext` automatically applies `WHERE TenantId = X` and sets `TenantId` on create.

```csharp
// Basic — string TenantId
public class Order : FullAuditableEntity, IMultiTenant
{
    public string TenantId { get; set; } = string.Empty;
}

// With navigation property
public class Order : FullAuditableEntity, IMultiTenant<Tenant>
{
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }
}

// With typed tenant key
public class Order : FullAuditableEntity, IMultiTenant<Tenant, Guid>
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
}
```

## IConcurrent

Enables optimistic concurrency detection via `RowVersion`. `CrudKitDbContext` configures `RowVersion` as a concurrency token. Conflicts return `409`.

```csharp
public interface IConcurrent
{
    uint RowVersion { get; set; }
}
```

## IStateMachine\<TState\>

Adds state machine behavior to an entity. Defines valid `(From, To, Action)` transitions. CrudKit maps `POST /{id}/transition/{action}` automatically.

```csharp
public interface IStateMachine<TState> where TState : struct, Enum
{
    TState Status { get; set; }
    static abstract IReadOnlyList<(TState From, TState To, string Action)> Transitions { get; }
}
```

## ICrudHooks\<T\>

Per-entity lifecycle hooks. All methods have empty default implementations — override only what you need.

```csharp
public interface ICrudHooks<T> where T : class, IEntity
{
    Task BeforeCreate(T entity, AppContext ctx);
    Task AfterCreate(T entity, AppContext ctx);

    // 2-param (backward compatible)
    Task BeforeUpdate(T entity, AppContext ctx);
    Task AfterUpdate(T entity, AppContext ctx);

    // 3-param — receives the existing entity state before the update
    Task BeforeUpdate(T entity, T? existingEntity, AppContext ctx);
    Task AfterUpdate(T entity, T? existingEntity, AppContext ctx);

    Task BeforeDelete(T entity, AppContext ctx);
    Task AfterDelete(T entity, AppContext ctx);
    Task BeforeRestore(T entity, AppContext ctx);
    Task AfterRestore(T entity, AppContext ctx);
    IQueryable<T> ApplyScope(IQueryable<T> query, AppContext ctx);
    IQueryable<T> ApplyIncludes(IQueryable<T> query);
}
```

The 3-param `BeforeUpdate`/`AfterUpdate` overloads default to calling their 2-param versions. See [Lifecycle Hooks](../features/hooks) for examples.

## IGlobalCrudHook

Runs for all entities on every CRUD operation. Register via `opts.UseGlobalHook<T>()`. Multiple global hooks run in registration order.

```csharp
public interface IGlobalCrudHook
{
    Task BeforeCreate(object entity, AppContext ctx);
    Task AfterCreate(object entity, AppContext ctx);
    Task BeforeUpdate(object entity, AppContext ctx);
    Task AfterUpdate(object entity, AppContext ctx);
    Task BeforeDelete(object entity, AppContext ctx);
    Task AfterDelete(object entity, AppContext ctx);
}
```

## IDomainEvent

Marker interface for domain events. Implement as a record for immutability.

```csharp
public interface IDomainEvent { }

public record InvoiceApprovedEvent(Guid InvoiceId) : IDomainEvent;
```

## IHasDomainEvents

Implemented by all `AggregateRoot` base classes. Provides `AddDomainEvent()` and tracks pending events for dispatch after `SaveChanges`.

```csharp
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void AddDomainEvent(IDomainEvent domainEvent);
    void ClearDomainEvents();
}
```

## IDomainEventHandler\<T\>

Handles a specific domain event type. Resolved from DI by the dispatcher.

```csharp
public interface IDomainEventHandler<T> where T : IDomainEvent
{
    Task HandleAsync(T domainEvent, CancellationToken ct = default);
}
```

## IDomainEventDispatcher

Dispatches domain events to their handlers. CrudKit provides `CrudKitEventDispatcher` as the default. Override with `UseDomainEvents<TDispatcher>()`.

```csharp
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default);
}
```

See [Domain Events](../features/domain-events) for full usage.

## IAuditWriter

Custom audit log destination. Implement this to write audit entries to any storage backend.

```csharp
public interface IAuditWriter
{
    Task WriteAsync(IReadOnlyList<AuditEntry> entries, CancellationToken ct);
}
```

## ITenantContext

Provides the resolved tenant ID for the current request. Populated by the configured tenant resolver. Separate from `ICurrentUser` — a request can be tenant-scoped without authentication.

## ICurrentUser

Represents the authenticated user for the current request. Must be implemented by the application layer.

```csharp
public interface ICurrentUser
{
    string? Id { get; }
    string? Username { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<Permission> Permissions { get; }
    bool IsAuthenticated { get; }
    bool HasRole(string role);
    bool HasPermission(string entity, string action);
    IReadOnlyList<string>? AccessibleTenants { get; }  // null = all tenants
}
```

## IResponseMapper, ICreateMapper, IUpdateMapper, ICrudMapper

Mapper interfaces for converting between entities and DTOs. Implement manually for custom mapping logic.

- `IResponseMapper<T, TResponse>` — entity → response DTO
- `ICreateMapper<T, TCreate>` — create DTO → entity
- `IUpdateMapper<T, TUpdate>` — apply update DTO to existing entity
- `ICrudMapper<T, TCreate, TUpdate, TResponse>` — combines all three

## ISequenceCustomizer\<TEntity\>

Customize auto-sequence generation per entity. Resolved from DI automatically when processing `[AutoSequence]` properties.

```csharp
public interface ISequenceCustomizer<TEntity> where TEntity : class
{
    // Override the template from [AutoSequence]. Return null to use the attribute template.
    string? ResolveTemplate(string? tenantId) => null;

    // Resolve custom placeholders in the template (e.g. {prefix} → "INV").
    Dictionary<string, string>? ResolvePlaceholders(string? tenantId) => null;
}
```

Both methods have default implementations returning `null` — override only what you need. See [Auto Sequence](../features/auto-sequence) for full usage.

## IDataFilter\<T\>

Runtime toggle for global query filters on a specific entity type. Inject as a scoped service and use `Disable<TFilter>()` inside a `using` block — the filter is restored automatically when the block exits.

```csharp
public interface IDataFilter<T>
{
    IDisposable Disable<TFilter>() where TFilter : class;
}
```

```csharp
// Temporarily include soft-deleted records for entity T
using (_dataFilter.Disable<ISoftDeletable>())
{
    var all = await _repo.ListAsync();
}
```

The tenant filter (`ITenantFilter`) cannot be disabled — it is always enforced. Cross-tenant access is configured via `CrossTenantPolicy` at startup.

## IModule

Self-registers services and endpoints for one bounded context. Discovered via assembly scan or manual registration.

```csharp
public interface IModule
{
    string Name { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
    void MapEndpoints(WebApplication app);
}
```

## ICrudKitDbContext

Marker interface implemented by `CrudKitDbContext` and `CrudKitIdentityDbContext`. Used internally by `CrudKitContextRegistry` to discover registered DbContexts and route entity operations to the correct context.

```csharp
public interface ICrudKitDbContext
{
    // Marker — no members. Implemented by CrudKitDbContext.
}
```

## IRepo\<T\>

Built-in generic repository. All CRUD operations go through this interface. Not user-extensible — use `WithCustomEndpoints` and direct `DbContext` access for custom queries.

```csharp
public interface IRepo<T> where T : class, IEntity
{
    Task<T> FindById(Guid id, CancellationToken ct = default);
    Task<T?> FindByIdOrDefault(Guid id, CancellationToken ct = default);
    Task<List<T>> FindByField(string fieldName, object value, CancellationToken ct = default);
    Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default);
    Task<T> Create(object createDto, CancellationToken ct = default);
    Task<T> Update(Guid id, object updateDto, CancellationToken ct = default);
    Task Delete(Guid id, CancellationToken ct = default);
    Task Restore(Guid id, CancellationToken ct = default);
    Task HardDelete(Guid id, CancellationToken ct = default);
    Task<bool> Exists(Guid id, CancellationToken ct = default);
    Task<long> Count(CancellationToken ct = default);
    Task<long> BulkCount(Dictionary<string, FilterOp> filters, CancellationToken ct = default);
    Task<int> BulkDelete(Dictionary<string, FilterOp> filters, CancellationToken ct = default);
    Task<int> BulkUpdate(Dictionary<string, FilterOp> filters, Dictionary<string, object?> values, CancellationToken ct = default);
}
```

## IEndpointConfigurer\<TEntity\>

Implement to add custom endpoints to an entity's route group. Auto-discovered by scanning the entity's assembly — no DI registration needed.

```csharp
public interface IEndpointConfigurer<TEntity> where TEntity : class, IEntity
{
    void Configure(CrudEndpointGroup<TEntity> group);
}
```

Usage:

```csharp
public class InvoiceEndpointConfigurer : IEndpointConfigurer<Invoice>
{
    public void Configure(CrudEndpointGroup<Invoice> group)
    {
        group.WithCustomEndpoints(g =>
        {
            g.MapPost("/from-quote/{quoteId}", handler);
        });
    }
}
```
