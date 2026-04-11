---
sidebar_position: 5
title: Lifecycle Hooks
---

# Lifecycle Hooks

Hooks let you intercept CRUD operations to run custom logic — normalizing data, firing events, invalidating caches, or enforcing row-level security.

## Per-Entity Hooks (ICrudHooks\<T\>)

Intercept Create, Update, Delete, and Restore for a specific entity type.

```csharp
public class ProductHooks : ICrudHooks<Product>
{
    public Task BeforeCreate(Product entity, AppContext ctx)
    {
        entity.Sku = entity.Sku.ToUpperInvariant();
        return Task.CompletedTask;
    }

    public Task AfterCreate(Product entity, AppContext ctx)
    {
        // send event, invalidate cache, etc.
        return Task.CompletedTask;
    }
}

builder.Services.AddScoped<ICrudHooks<Product>, ProductHooks>();
```

All hook methods have empty default implementations. Override only what you need.

**Execution order:** `Validate` → `Before*` → DB operation → `After*`

## ICrudHooks\<T\> Interface

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

The 3-param overloads default to calling the 2-param versions, so existing hooks are backward compatible. Use the 3-param overload when you need to compare old vs. new values:

```csharp
public class ProductHooks : ICrudHooks<Product>
{
    public Task BeforeUpdate(Product entity, Product? existingEntity, AppContext ctx)
    {
        if (existingEntity is not null && existingEntity.Price != entity.Price)
        {
            // Price changed — log, notify, etc.
        }
        return Task.CompletedTask;
    }
}
```

## Row-Level Security with ApplyScope

```csharp
public class OrderHooks : ICrudHooks<Order>
{
    public IQueryable<Order> ApplyScope(IQueryable<Order> query, AppContext ctx)
    {
        if (!ctx.CurrentUser.HasRole("admin"))
            return query.Where(o => o.CreatedById == ctx.CurrentUser.Id);
        return query;
    }
}
```

`ApplyScope` is applied to `List`, `FindById`, and `FindByIdOrDefault`. A record outside the scope returns `404` on `FindById`.

## Custom Includes with ApplyIncludes

```csharp
public class OrderHooks : ICrudHooks<Order>
{
    public IQueryable<Order> ApplyIncludes(IQueryable<Order> query)
        => query.Include(o => o.Lines).ThenInclude(l => l.Product);
}
```

`ApplyIncludes` is applied before `[DefaultInclude]` attributes.

## Global Hooks (IGlobalCrudHook)

Run for all entities on every CRUD operation. Register via `opts.UseGlobalHook<T>()`. Multiple global hooks run in registration order.

```csharp
public class SearchIndexHook : IGlobalCrudHook
{
    private readonly ISearchService _search;
    public SearchIndexHook(ISearchService search) => _search = search;

    public async Task AfterCreate(object entity, AppContext ctx)
        => await _search.Index(entity);

    public async Task AfterUpdate(object entity, AppContext ctx)
        => await _search.Index(entity);

    public async Task AfterDelete(object entity, AppContext ctx)
        => await _search.Remove(entity);
}

// Register
builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.UseGlobalHook<SearchIndexHook>();
    opts.UseGlobalHook<CacheInvalidationHook>();
});
```

**Execution order:** `Global Before` → `Entity Before` → DB op → `Entity After` → `Global After`

## IGlobalCrudHook Interface

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
