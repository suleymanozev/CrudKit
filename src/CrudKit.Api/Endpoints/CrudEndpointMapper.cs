using System.Reflection;
using CrudKit.Api.Filters;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Endpoints;

/// <summary>
/// Maps a full set of CRUD endpoints for a given entity type.
/// Each mutating operation runs inside a database transaction and invokes lifecycle hooks.
/// </summary>
public static class CrudEndpointMapper
{
    /// <summary>
    /// Maps GET (list), GET (by id), POST, PUT, DELETE endpoints for the entity.
    /// Conditionally maps restore (ISoftDeletable) and transition (IStateMachine) endpoints.
    /// </summary>
    public static RouteGroupBuilder MapCrudEndpoints<TEntity, TCreate, TUpdate>(
        this WebApplication app,
        string route)
        where TEntity : class, IEntity
        where TCreate : class
        where TUpdate : class
    {
        var tag = typeof(TEntity).Name.Replace("Entity", "");
        var group = app.MapGroup($"/api/{route}").WithTags(tag);
        group.AddEndpointFilter<AppErrorFilter>();

        // GET /api/{route} — List
        group.MapGet("/", async (HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var listParams = ListParams.FromQuery(httpCtx.Request.Query);
            var result = await repo.List(listParams, ct);
            var mapped = TryMapPaginated(httpCtx.RequestServices, result);
            return Results.Ok(mapped);
        })
        .WithName($"List{tag}")
        .Produces<Paginated<TEntity>>(200)
        .ProducesProblem(500);

        // GET /api/{route}/{id} — Get by ID
        group.MapGet("/{id}", async (string id, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var entity = await repo.FindByIdOrDefault(id, ct);
            if (entity is null)
                return Results.Json(new { status = 404, code = "NOT_FOUND", message = $"{typeof(TEntity).Name} with id '{id}' was not found." }, statusCode: 404);

            var mapped = TryMapSingle(httpCtx.RequestServices, entity);
            return Results.Ok(mapped);
        })
        .WithName($"Get{tag}")
        .Produces<TEntity>(200)
        .ProducesProblem(404)
        .ProducesProblem(500);

        // POST /api/{route} — Create
        group.MapPost("/", async (TCreate dto, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var db = httpCtx.RequestServices.GetRequiredService<CrudKitDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
                var entity = await repo.Create(dto, ct);

                var appCtx = BuildAppContext(httpCtx);
                if (hooks != null)
                {
                    await hooks.BeforeCreate(entity, appCtx);
                    await db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);

                if (hooks != null)
                    await hooks.AfterCreate(entity, appCtx);

                return Results.Created($"/api/{route}/{entity.Id}", entity);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .WithName($"Create{tag}")
        .AddEndpointFilter<ValidationFilter<TCreate>>()
        .Produces<TEntity>(201)
        .ProducesProblem(400)
        .ProducesProblem(500);

        // PUT /api/{route}/{id} — Update
        group.MapPut("/{id}", async (string id, TUpdate dto, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var db = httpCtx.RequestServices.GetRequiredService<CrudKitDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
                var entity = await repo.Update(id, dto, ct);

                var appCtx = BuildAppContext(httpCtx);
                if (hooks != null)
                {
                    await hooks.BeforeUpdate(entity, appCtx);
                    await db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);

                if (hooks != null)
                    await hooks.AfterUpdate(entity, appCtx);

                return Results.Ok(entity);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .WithName($"Update{tag}")
        .AddEndpointFilter<ValidationFilter<TUpdate>>()
        .Produces<TEntity>(200)
        .ProducesProblem(400)
        .ProducesProblem(404)
        .ProducesProblem(500);

        // DELETE /api/{route}/{id} — Delete
        group.MapDelete("/{id}", async (string id, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var db = httpCtx.RequestServices.GetRequiredService<CrudKitDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
                var appCtx = BuildAppContext(httpCtx);

                if (hooks != null)
                {
                    var entity = await repo.FindByIdOrDefault(id, ct);
                    if (entity is null)
                        throw AppError.NotFound($"{typeof(TEntity).Name} with id '{id}' was not found.");
                    await hooks.BeforeDelete(entity, appCtx);
                }

                await repo.Delete(id, ct);
                await tx.CommitAsync(ct);

                if (hooks != null)
                {
                    // AfterDelete receives a minimal entity with just the Id
                    var stub = Activator.CreateInstance<TEntity>();
                    stub.Id = id;
                    await hooks.AfterDelete(stub, appCtx);
                }

                return Results.NoContent();
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .WithName($"Delete{tag}")
        .Produces(204)
        .ProducesProblem(404)
        .ProducesProblem(500);

        // POST /api/{route}/{id}/restore — Restore (ISoftDeletable only)
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity)))
        {
            group.MapPost("/{id}/restore", async (string id, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
            {
                var db = httpCtx.RequestServices.GetRequiredService<CrudKitDbContext>();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
                    var appCtx = BuildAppContext(httpCtx);

                    await repo.Restore(id, ct);

                    if (hooks != null)
                    {
                        var entity = await repo.FindById(id, ct);
                        await hooks.BeforeRestore(entity, appCtx);
                        await db.SaveChangesAsync(ct);
                    }

                    await tx.CommitAsync(ct);
                    return Results.Ok();
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            })
            .WithName($"Restore{tag}")
            .Produces(200)
            .ProducesProblem(404)
            .ProducesProblem(500);
        }

        // POST /api/{route}/{id}/transition/{action} — State transition (IStateMachine only)
        MapTransitionEndpoint<TEntity>(group, route, tag);

        return group;
    }

    /// <summary>
    /// Uses reflection to check if TEntity implements IStateMachine and maps the transition endpoint.
    /// </summary>
    private static void MapTransitionEndpoint<TEntity>(RouteGroupBuilder group, string route, string tag)
        where TEntity : class, IEntity
    {
        // Find IStateMachine<TState> on TEntity
        var smInterface = typeof(TEntity).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStateMachine<>));
        if (smInterface is null) return;

        var stateType = smInterface.GetGenericArguments()[0];

        // Read static Transitions property
        var transitionsProp = typeof(TEntity).GetProperty("Transitions",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (transitionsProp is null) return;

        // Status property on the entity
        var statusProp = typeof(TEntity).GetProperty("Status",
            BindingFlags.Public | BindingFlags.Instance);
        if (statusProp is null) return;

        group.MapPost("/{id}/transition/{action}", async (string id, string action, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var db = httpCtx.RequestServices.GetRequiredService<CrudKitDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // Load entity for update (tracked)
                var dbSet = db.Set<TEntity>();
                var entity = await dbSet.FirstOrDefaultAsync(e => e.Id == id, ct)
                    ?? throw AppError.NotFound($"{typeof(TEntity).Name} with id '{id}' was not found.");

                var currentStatus = statusProp.GetValue(entity)!;
                var transitions = transitionsProp.GetValue(null)!;

                // Check if the requested action is valid from the current state
                var found = false;
                object? targetStatus = null;

                // Iterate the transitions list via reflection
                foreach (var t in (System.Collections.IEnumerable)transitions)
                {
                    var tType = t.GetType();
                    var from = tType.GetField("Item1")!.GetValue(t)!;
                    var to = tType.GetField("Item2")!.GetValue(t)!;
                    var act = (string)tType.GetField("Item3")!.GetValue(t)!;

                    if (from.Equals(currentStatus) && string.Equals(act, action, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        targetStatus = to;
                        break;
                    }
                }

                if (!found)
                    throw AppError.BadRequest($"Invalid transition '{action}' from state '{currentStatus}'.");

                statusProp.SetValue(entity, targetStatus);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return Results.Ok(entity);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .WithName($"Transition{tag}")
        .Produces<TEntity>(200)
        .ProducesProblem(400)
        .ProducesProblem(404)
        .ProducesProblem(500);
    }

    /// <summary>
    /// Attempts to find a registered IEntityMapper for TEntity and map a single entity.
    /// Returns the mapped response if a mapper is found, otherwise the raw entity.
    /// </summary>
    private static object TryMapSingle<TEntity>(IServiceProvider services, TEntity entity)
        where TEntity : class, IEntity
    {
        var mapper = ResolveEntityMapper<TEntity>(services);
        if (mapper is null) return entity;

        // Call Map(entity) via reflection since we don't know TResponse at compile time
        var mapMethod = mapper.GetType().GetMethod("Map")!;
        return mapMethod.Invoke(mapper, [entity])!;
    }

    /// <summary>
    /// Attempts to find a registered IEntityMapper for TEntity and map a paginated result.
    /// Returns the mapped paginated response if a mapper is found, otherwise the raw result.
    /// </summary>
    private static object TryMapPaginated<TEntity>(IServiceProvider services, Paginated<TEntity> result)
        where TEntity : class, IEntity
    {
        var mapper = ResolveEntityMapper<TEntity>(services);
        if (mapper is null) return result;

        var mapMethod = mapper.GetType().GetMethod("Map")!;
        var mappedData = result.Data.Select(e => mapMethod.Invoke(mapper, [e])!).ToList();

        // Build a dictionary-based result to preserve pagination metadata
        return new
        {
            data = mappedData,
            total = result.Total,
            page = result.Page,
            perPage = result.PerPage,
            totalPages = result.TotalPages
        };
    }

    /// <summary>
    /// Scans DI service descriptors for any registered IEntityMapper&lt;TEntity, ?&gt; implementation.
    /// </summary>
    private static object? ResolveEntityMapper<TEntity>(IServiceProvider services)
        where TEntity : class, IEntity
    {
        // Try to find any service registration that implements IEntityMapper<TEntity, ?>
        // by scanning the service collection stored in the root provider.
        var mapperInterfaceBase = typeof(IEntityMapper<,>);

        // Approach: get all service descriptors from the IServiceCollection snapshot
        // available via IServiceProvider. We look for registrations matching IEntityMapper<TEntity, *>.
        // The concrete registered service type tells us the TResponse.
        using var scope = services.CreateScope();
        var serviceCollection = services.GetService<IServiceProviderIsService>();
        if (serviceCollection is null) return null;

        // Check known registered types by scanning all interfaces of registered services
        // A simpler approach: try common patterns by looking at the service provider itself
        var entityType = typeof(TEntity);

        // Enumerate service descriptors if accessible
        var descriptors = services.GetService<IServiceCollection>();

        // Since IServiceCollection isn't registered by default, use reflection to check
        // the IServiceProviderIsService for IEntityMapper<TEntity, ?> with all types
        // that have been registered.

        // Practical approach: enumerate all types implementing IEntityMapper<TEntity, *>
        // from loaded assemblies where the type has been registered
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                var iface = type.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType
                        && i.GetGenericTypeDefinition() == mapperInterfaceBase
                        && i.GetGenericArguments()[0] == entityType);

                if (iface is null) continue;

                // Try to resolve this specific interface from DI
                var resolved = services.GetService(iface);
                if (resolved is not null) return resolved;
            }
        }

        return null;
    }

    private static CrudKit.Core.Context.AppContext BuildAppContext(HttpContext httpCtx)
    {
        return new CrudKit.Core.Context.AppContext
        {
            Services = httpCtx.RequestServices,
            CurrentUser = httpCtx.RequestServices.GetRequiredService<ICurrentUser>()
        };
    }
}
