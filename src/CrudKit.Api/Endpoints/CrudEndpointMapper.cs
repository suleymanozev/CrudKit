using System.Reflection;
using System.Text.Json;
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
/// Wrapper returned by <see cref="CrudEndpointMapper.MapCrudEndpoints{TEntity}(WebApplication, string)"/>
/// and its overloads. Carries enough context for fluent <c>.WithDetail</c> chaining.
/// </summary>
public class CrudEndpointGroup<TMaster> where TMaster : class, IEntity
{
    /// <summary>The route group created for the master entity.</summary>
    public RouteGroupBuilder Group { get; }

    /// <summary>The host application (needed to map additional route groups).</summary>
    public WebApplication App { get; }

    /// <summary>The route prefix used for the master entity (e.g. "products").</summary>
    public string Route { get; }

    internal CrudEndpointGroup(RouteGroupBuilder group, WebApplication app, string route)
    {
        Group = group;
        App = app;
        Route = route;
    }

    /// <summary>
    /// Maps nested CRUD endpoints for a detail (child) entity scoped under the master route.
    /// Returns the same group for further fluent chaining.
    /// </summary>
    public CrudEndpointGroup<TMaster> WithDetail<TDetail, TCreateDetail>(
        string detailRoute,
        string foreignKeyProperty)
        where TDetail : class, IEntity
        where TCreateDetail : class
    {
        var masterTag = typeof(TMaster).Name.Replace("Entity", "");
        var detailTag = typeof(TDetail).Name.Replace("Entity", "");
        var tag = $"{masterTag}{detailTag}";

        var group = App.MapGroup($"/api/{Route}/{{masterId}}/{detailRoute}").WithTags(tag);
        group.AddEndpointFilter<AppErrorFilter>();

        var fkProp = typeof(TDetail).GetProperty(foreignKeyProperty,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(
                $"Property '{foreignKeyProperty}' not found on {typeof(TDetail).Name}.");

        var masterRoute = Route;

        // GET /api/{masterRoute}/{masterId}/{detailRoute} — List details by master
        group.MapGet("/", async (string masterId, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            if (!await masterRepo.Exists(masterId, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var details = await detailRepo.FindByField(foreignKeyProperty, masterId, ct);
            return Results.Ok(details);
        })
        .WithName($"List{tag}")
        .Produces<List<TDetail>>(200)
        .ProducesProblem(404)
        .ProducesProblem(500);

        // GET /api/{masterRoute}/{masterId}/{detailRoute}/{id} — Get single detail
        group.MapGet("/{id}", async (string masterId, string id, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            if (!await masterRepo.Exists(masterId, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var entity = await detailRepo.FindByIdOrDefault(id, ct);
            if (entity is null)
                return Results.Json(new { status = 404, code = "NOT_FOUND", message = $"{typeof(TDetail).Name} with id '{id}' was not found." }, statusCode: 404);

            var fkValue = fkProp.GetValue(entity)?.ToString();
            if (fkValue != masterId)
                return Results.Json(new { status = 404, code = "NOT_FOUND", message = $"{typeof(TDetail).Name} with id '{id}' was not found." }, statusCode: 404);

            return Results.Ok(entity);
        })
        .WithName($"Get{tag}")
        .Produces<TDetail>(200)
        .ProducesProblem(404)
        .ProducesProblem(500);

        // POST /api/{masterRoute}/{masterId}/{detailRoute} — Create detail
        group.MapPost("/", async (string masterId, TCreateDetail dto, HttpContext httpCtx, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            if (!await masterRepo.Exists(masterId, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var dtoFkProp = typeof(TCreateDetail).GetProperty(foreignKeyProperty,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (dtoFkProp != null)
                dtoFkProp.SetValue(dto, masterId);

            var db = httpCtx.RequestServices.GetRequiredService<CrudKitDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var entity = await detailRepo.Create(dto, ct);

                if (dtoFkProp == null)
                {
                    fkProp.SetValue(entity, masterId);
                    await db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);
                return Results.Created($"/api/{masterRoute}/{masterId}/{detailRoute}/{entity.Id}", entity);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .WithName($"Create{tag}")
        .AddEndpointFilter<ValidationFilter<TCreateDetail>>()
        .Produces<TDetail>(201)
        .ProducesProblem(400)
        .ProducesProblem(404)
        .ProducesProblem(500);

        // DELETE /api/{masterRoute}/{masterId}/{detailRoute}/{id} — Delete detail
        group.MapDelete("/{id}", async (string masterId, string id, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            if (!await masterRepo.Exists(masterId, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var entity = await detailRepo.FindByIdOrDefault(id, ct);
            if (entity is null)
                throw AppError.NotFound($"{typeof(TDetail).Name} with id '{id}' was not found.");

            var fkValue = fkProp.GetValue(entity)?.ToString();
            if (fkValue != masterId)
                throw AppError.NotFound($"{typeof(TDetail).Name} with id '{id}' was not found.");

            await detailRepo.Delete(id, ct);
            return Results.NoContent();
        })
        .WithName($"Delete{tag}")
        .Produces(204)
        .ProducesProblem(404)
        .ProducesProblem(500);

        // PUT /api/{masterRoute}/{masterId}/{detailRoute}/batch — Batch upsert (replace all)
        group.MapPut("/batch", async (string masterId, List<TCreateDetail> dtos, HttpContext httpCtx, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            if (!await masterRepo.Exists(masterId, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var db = httpCtx.RequestServices.GetRequiredService<CrudKitDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var existing = await detailRepo.FindByField(foreignKeyProperty, masterId, ct);
                foreach (var e in existing)
                    await detailRepo.Delete(e.Id, ct);

                var created = new List<TDetail>();
                var dtoFkProp = typeof(TCreateDetail).GetProperty(foreignKeyProperty,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                foreach (var dto in dtos)
                {
                    if (dtoFkProp != null)
                        dtoFkProp.SetValue(dto, masterId);

                    var entity = await detailRepo.Create(dto, ct);

                    if (dtoFkProp == null)
                    {
                        fkProp.SetValue(entity, masterId);
                        await db.SaveChangesAsync(ct);
                    }

                    created.Add(entity);
                }

                await tx.CommitAsync(ct);
                return Results.Ok(created);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .WithName($"BatchUpsert{tag}")
        .Produces<List<TDetail>>(200)
        .ProducesProblem(400)
        .ProducesProblem(404)
        .ProducesProblem(500);

        return this;
    }
}

/// <summary>
/// Maps a full set of CRUD endpoints for a given entity type.
/// Each mutating operation runs inside a database transaction and invokes lifecycle hooks.
/// </summary>
public static class CrudEndpointMapper
{
    /// <summary>
    /// Maps read-only endpoints: GET / (list) and GET /{id} (get by id).
    /// Use for entities that should not be created, updated, or deleted via API.
    /// </summary>
    public static CrudEndpointGroup<TEntity> MapCrudEndpoints<TEntity>(
        this WebApplication app,
        string route)
        where TEntity : class, IEntity
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

        return new CrudEndpointGroup<TEntity>(group, app, route);
    }

    /// <summary>
    /// Maps GET (list), GET (by id), POST, PUT, DELETE endpoints for the entity.
    /// Conditionally maps restore (ISoftDeletable) and transition (IStateMachine) endpoints.
    /// </summary>
    public static CrudEndpointGroup<TEntity> MapCrudEndpoints<TEntity, TCreate, TUpdate>(
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
        .Accepts<TCreate>("application/json")
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
        .Accepts<TUpdate>("application/json")
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

        // GET /api/{route}/bulk-count — Count with filters
        group.MapGet("/bulk-count", async (HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var listParams = ListParams.FromQuery(httpCtx.Request.Query);
            var count = await repo.BulkCount(listParams.Filters, ct);
            return Results.Ok(new { count });
        })
        .WithName($"BulkCount{tag}")
        .Produces<object>(200)
        .ProducesProblem(500);

        // POST /api/{route}/bulk-delete — Bulk delete with filters
        group.MapPost("/bulk-delete", async (BulkDeleteRequest request, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var filters = ParseFilters(request.Filters);
            var options = httpCtx.RequestServices.GetRequiredService<Configuration.CrudKitApiOptions>();

            var count = await repo.BulkCount(filters, ct);
            if (count > options.BulkLimit)
                throw AppError.BadRequest($"Bulk operation affects {count} records, which exceeds the limit of {options.BulkLimit}.");

            var affected = await repo.BulkDelete(filters, ct);
            return Results.Ok(new { affected });
        })
        .WithName($"BulkDelete{tag}")
        .Produces<object>(200)
        .ProducesProblem(400)
        .ProducesProblem(500);

        // POST /api/{route}/bulk-update — Bulk update with filters
        group.MapPost("/bulk-update", async (BulkUpdateRequest request, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var filters = ParseFilters(request.Filters);
            var values = ConvertValues(request.Values);
            var options = httpCtx.RequestServices.GetRequiredService<Configuration.CrudKitApiOptions>();

            var count = await repo.BulkCount(filters, ct);
            if (count > options.BulkLimit)
                throw AppError.BadRequest($"Bulk operation affects {count} records, which exceeds the limit of {options.BulkLimit}.");

            var affected = await repo.BulkUpdate(filters, values, ct);
            return Results.Ok(new { affected });
        })
        .WithName($"BulkUpdate{tag}")
        .Produces<object>(200)
        .ProducesProblem(400)
        .ProducesProblem(500);

        return new CrudEndpointGroup<TEntity>(group, app, route);
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
    /// Attempts to find a registered IResponseMapper for TEntity and map a single entity.
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
    /// Attempts to find a registered IResponseMapper for TEntity and map a paginated result.
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
    /// Scans DI service descriptors for any registered IResponseMapper&lt;TEntity, ?&gt; implementation.
    /// </summary>
    private static object? ResolveEntityMapper<TEntity>(IServiceProvider services)
        where TEntity : class, IEntity
    {
        // Try to find any service registration that implements IResponseMapper<TEntity, ?>
        // by scanning the service collection stored in the root provider.
        var mapperInterfaceBase = typeof(IResponseMapper<,>);

        // Approach: get all service descriptors from the IServiceCollection snapshot
        // available via IServiceProvider. We look for registrations matching IResponseMapper<TEntity, *>.
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
        // the IServiceProviderIsService for IResponseMapper<TEntity, ?> with all types
        // that have been registered.

        // Practical approach: enumerate all types implementing IResponseMapper<TEntity, *>
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

    private static Dictionary<string, FilterOp> ParseFilters(Dictionary<string, string>? raw)
    {
        if (raw is null || raw.Count == 0) return new();
        return raw.ToDictionary(kv => kv.Key, kv => FilterOp.Parse(kv.Value));
    }

    /// <summary>
    /// Converts values from JSON deserialization (which may contain JsonElement) to primitive types.
    /// </summary>
    private static Dictionary<string, object?> ConvertValues(Dictionary<string, object?>? raw)
    {
        if (raw is null || raw.Count == 0) return new();
        var result = new Dictionary<string, object?>(raw.Count);
        foreach (var (key, value) in raw)
        {
            result[key] = value switch
            {
                JsonElement je => je.ValueKind switch
                {
                    JsonValueKind.String => je.GetString(),
                    JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => je.GetRawText()
                },
                _ => value
            };
        }
        return result;
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

/// <summary>
/// Request body for the bulk delete endpoint.
/// </summary>
public class BulkDeleteRequest
{
    public Dictionary<string, string>? Filters { get; set; }
}

/// <summary>
/// Request body for the bulk update endpoint.
/// </summary>
public class BulkUpdateRequest
{
    public Dictionary<string, string>? Filters { get; set; }
    public Dictionary<string, object?>? Values { get; set; }
}
