using System.Reflection;
using System.Text.Json;
using CrudKit.Api.Configuration;
using CrudKit.Api.Filters;
using CrudKit.Api.Helpers;
using CrudKit.Api.Models;
using CrudKit.Api.Services;
using CrudKit.Core.Attributes;
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
/// and its overloads. Carries enough context for fluent <c>.WithChild</c> chaining.
/// </summary>
public class CrudEndpointGroup<TMaster> where TMaster : class, IEntity
{
    /// <summary>The route group created for the master entity.</summary>
    public RouteGroupBuilder Group { get; }

    /// <summary>The host application (needed to map additional route groups).</summary>
    public WebApplication App { get; }

    /// <summary>The route prefix used for the master entity (e.g. "products").</summary>
    public string Route { get; }

    /// <summary>
    /// Tracks child entity types already registered via <see cref="WithChild{TDetail,TCreateDetail}"/>.
    /// Auto-discovery in <see cref="CrudEndpointMapper.MapCrudEndpoints{TEntity}(WebApplication,string)"/>
    /// skips types present in this set to avoid duplicate route registration.
    /// </summary>
    internal HashSet<Type> RegisteredDetailTypes { get; } = new();

    /// <summary>
    /// Stores the <see cref="RouteGroupBuilder"/> created by auto-discovery for each child type
    /// so that a subsequent <see cref="WithChild{TDetail,TCreateDetail}"/> call can reuse the
    /// same group instead of creating a duplicate route registration.
    /// </summary>
    internal Dictionary<Type, RouteGroupBuilder> AutoDiscoveredGroups { get; } = new();

    /// <summary>
    /// Stores the <see cref="RouteGroupBuilder"/> created by any <c>WithChild</c> call for each child type.
    /// Used by <see cref="WithChild{TDetail,TCreateDetail,TUpdateDetail}"/> to reuse the group
    /// created by <see cref="WithChild{TDetail,TCreateDetail}"/>.
    /// </summary>
    internal Dictionary<Type, RouteGroupBuilder> ChildGroups { get; } = new();

    internal CrudEndpointGroup(RouteGroupBuilder group, WebApplication app, string route)
    {
        Group = group;
        App = app;
        Route = route;
    }

    /// <summary>
    /// Configures per-operation authorization for the CRUD endpoint group.
    /// Supports global roles, per-operation roles/permissions, and convention-based permissions.
    /// </summary>
    public CrudEndpointGroup<TMaster> Authorize(Action<EndpointAuthorizationBuilder> configure)
    {
        var auth = new EndpointAuthorizationBuilder();
        configure(auth);
        Group.AddEndpointFilter(new CrudAuthorizationFilter(auth, Route));
        return this;
    }

    /// <summary>
    /// Adds custom endpoints under the same route group (/api/{route}).
    /// Custom endpoints share the same AppErrorFilter and route prefix.
    /// </summary>
    public CrudEndpointGroup<TMaster> WithCustomEndpoints(Action<RouteGroupBuilder> configure)
    {
        configure(Group);
        return this;
    }

    /// <summary>
    /// Maps nested CRUD endpoints for a child entity scoped under the master route.
    /// Returns the same group for further fluent chaining.
    /// </summary>
    public CrudEndpointGroup<TMaster> WithChild<TDetail, TCreateDetail>(
        string detailRoute,
        string foreignKeyProperty)
        where TDetail : class, IEntity
        where TCreateDetail : class
    {
        RegisteredDetailTypes.Add(typeof(TDetail));

        var masterTag = typeof(TMaster).Name.Replace("Entity", "");
        var detailTag = typeof(TDetail).Name.Replace("Entity", "");
        var tag = $"{masterTag}{detailTag}";

        // Reuse the auto-discovered group when one exists to avoid duplicate route registration
        // (two MapGroup calls with the same pattern cause AmbiguousMatchException at runtime).
        // When reusing, List/Get/Delete are already registered by auto-discovery, so only
        // POST and BatchUpsert need to be added.
        var autoGroupExists = AutoDiscoveredGroups.TryGetValue(typeof(TDetail), out var existingGroup);
        RouteGroupBuilder group;
        if (autoGroupExists)
        {
            group = existingGroup!;
        }
        else
        {
            group = App.MapGroup($"/api/{Route}/{{masterId}}/{detailRoute}").WithTags(tag);
            group.AddEndpointFilter<AppErrorFilter>();
        }

        var fkProp = typeof(TDetail).GetProperty(foreignKeyProperty,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(
                $"Property '{foreignKeyProperty}' not found on {typeof(TDetail).Name}.");

        var masterRoute = Route;

        if (!autoGroupExists)
        {
        // GET /api/{masterRoute}/{masterId}/{detailRoute} — List details by master
        group.MapGet("/", async (string masterId, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            var masterGuid = CrudEndpointMapper.ParseGuid(masterId, typeof(TMaster).Name);
            if (!await masterRepo.Exists(masterGuid, ct))
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
            var masterGuid = CrudEndpointMapper.ParseGuid(masterId, typeof(TMaster).Name);
            var detailGuid = CrudEndpointMapper.ParseGuid(id, typeof(TDetail).Name);
            if (!await masterRepo.Exists(masterGuid, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var entity = await detailRepo.FindByIdOrDefault(detailGuid, ct);
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
        } // end if (!autoGroupExists) for List/Get

        // POST /api/{masterRoute}/{masterId}/{detailRoute} — Create detail
        group.MapPost("/", async (string masterId, TCreateDetail dto, HttpContext httpCtx, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            var masterGuid = CrudEndpointMapper.ParseGuid(masterId, typeof(TMaster).Name);
            if (!await masterRepo.Exists(masterGuid, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var dtoFkProp = typeof(TCreateDetail).GetProperty(foreignKeyProperty,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (dtoFkProp != null)
                dtoFkProp.SetValue(dto, CrudEndpointMapper.ConvertFkValue(masterId, dtoFkProp.PropertyType));

            var db = CrudEndpointMapper.ResolveDbContextFor<TDetail>(httpCtx.RequestServices);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var entity = await detailRepo.Create(dto, ct);

                if (dtoFkProp == null)
                {
                    fkProp.SetValue(entity, CrudEndpointMapper.ConvertFkValue(masterId, fkProp.PropertyType));
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

        if (!autoGroupExists)
        {
        // DELETE /api/{masterRoute}/{masterId}/{detailRoute}/{id} — Delete detail
        group.MapDelete("/{id}", async (string masterId, string id, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            var masterGuid = CrudEndpointMapper.ParseGuid(masterId, typeof(TMaster).Name);
            var detailGuid = CrudEndpointMapper.ParseGuid(id, typeof(TDetail).Name);
            if (!await masterRepo.Exists(masterGuid, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var entity = await detailRepo.FindByIdOrDefault(detailGuid, ct);
            if (entity is null)
                throw AppError.NotFound($"{typeof(TDetail).Name} with id '{id}' was not found.");

            var fkValue = fkProp.GetValue(entity)?.ToString();
            if (fkValue != masterId)
                throw AppError.NotFound($"{typeof(TDetail).Name} with id '{id}' was not found.");

            await detailRepo.Delete(detailGuid, ct);
            return Results.NoContent();
        })
        .WithName($"Delete{tag}")
        .Produces(204)
        .ProducesProblem(404)
        .ProducesProblem(500);
        } // end if (!autoGroupExists) for Delete

        // PUT /api/{masterRoute}/{masterId}/{detailRoute}/batch — Batch upsert (replace all)
        group.MapPut("/batch", async (string masterId, List<TCreateDetail> dtos, HttpContext httpCtx, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            var masterGuid = CrudEndpointMapper.ParseGuid(masterId, typeof(TMaster).Name);
            if (!await masterRepo.Exists(masterGuid, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var db = CrudEndpointMapper.ResolveDbContextFor<TDetail>(httpCtx.RequestServices);
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
                        dtoFkProp.SetValue(dto, CrudEndpointMapper.ConvertFkValue(masterId, dtoFkProp.PropertyType));

                    var entity = await detailRepo.Create(dto, ct);

                    if (dtoFkProp == null)
                    {
                        fkProp.SetValue(entity, CrudEndpointMapper.ConvertFkValue(masterId, fkProp.PropertyType));
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

        // Store group reference for potential reuse by WithChild<TDetail,TCreateDetail,TUpdateDetail>
        ChildGroups.TryAdd(typeof(TDetail), group);

        return this;
    }

    /// <summary>
    /// Maps nested CRUD endpoints for a child entity with update support.
    /// Registers all endpoints from <see cref="WithChild{TDetail,TCreateDetail}"/> plus a PUT /{id} update endpoint.
    /// </summary>
    public CrudEndpointGroup<TMaster> WithChild<TDetail, TCreateDetail, TUpdateDetail>(
        string detailRoute,
        string foreignKeyProperty)
        where TDetail : class, IEntity
        where TCreateDetail : class
        where TUpdateDetail : class
    {
        // Register List, Get, Create, Delete, Batch via the 2-type-param overload
        WithChild<TDetail, TCreateDetail>(detailRoute, foreignKeyProperty);

        var masterTag = typeof(TMaster).Name.Replace("Entity", "");
        var detailTag = typeof(TDetail).Name.Replace("Entity", "");
        var tag = $"{masterTag}{detailTag}";

        var fkProp = typeof(TDetail).GetProperty(foreignKeyProperty,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;

        // Reuse the group created by WithChild<TDetail,TCreateDetail> above
        var group = ChildGroups[typeof(TDetail)];

        var masterRoute = Route;

        // PUT /api/{masterRoute}/{masterId}/{detailRoute}/{id} — Update detail
        group.MapPut("/{id}", async (string masterId, string id, TUpdateDetail dto,
            HttpContext httpCtx, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            var masterGuid = CrudEndpointMapper.ParseGuid(masterId, typeof(TMaster).Name);
            var detailGuid = CrudEndpointMapper.ParseGuid(id, typeof(TDetail).Name);
            if (!await masterRepo.Exists(masterGuid, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var existing = await detailRepo.FindByIdOrDefault(detailGuid, ct);
            if (existing is null)
                throw AppError.NotFound($"{typeof(TDetail).Name} with id '{id}' was not found.");

            var fkValue = fkProp.GetValue(existing)?.ToString();
            if (fkValue != masterId)
                throw AppError.NotFound($"{typeof(TDetail).Name} with id '{id}' was not found.");

            var db = CrudEndpointMapper.ResolveDbContextFor<TDetail>(httpCtx.RequestServices);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var entity = await detailRepo.Update(detailGuid, dto, ct);
                await tx.CommitAsync(ct);
                return Results.Ok(entity);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .WithName($"Update{tag}")
        .AddEndpointFilter<ValidationFilter<TUpdateDetail>>()
        .Produces<TDetail>(200)
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
            var guid = ParseGuid(id, typeof(TEntity).Name);
            var entity = await repo.FindByIdOrDefault(guid, ct);
            if (entity is null)
                return Results.Json(new { status = 404, code = "NOT_FOUND", message = $"{typeof(TEntity).Name} with id '{id}' was not found." }, statusCode: 404);

            var mapped = TryMapSingle(httpCtx.RequestServices, entity);
            return Results.Ok(mapped);
        })
        .WithName($"Get{tag}")
        .Produces<TEntity>(200)
        .ProducesProblem(404)
        .ProducesProblem(500);

        // GET /api/{route}/export — Export (3-level: [NotExportable] class > [Exportable] class > global UseExport())
        var exportOptsRo = app.Services.GetService<Configuration.CrudKitApiOptions>();
        if (FeatureResolver.IsExportEnabled<TEntity>(exportOptsRo?.ExportEnabled ?? false))
        {
            group.MapGet("/export", async (HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
            {
                var listParams = ListParams.FromQuery(httpCtx.Request.Query);
                var format = httpCtx.Request.Query["format"].FirstOrDefault() ?? "csv";

                var apiOpts = httpCtx.RequestServices.GetService<Configuration.CrudKitApiOptions>();
                var maxRows = apiOpts?.MaxExportRows ?? 50_000;

                // Count first to prevent memory DoS
                var count = await repo.BulkCount(listParams.Filters, ct);
                if (count > maxRows)
                    throw AppError.BadRequest($"Export would return {count} rows, exceeding the limit of {maxRows}. Narrow your filters.");

                listParams.Page = 1;
                listParams.PerPage = maxRows;
                var result = await repo.List(listParams, ct);

                if (format == "csv")
                {
                    var csv = CsvExportService.Export(result.Data);
                    return Results.File(
                        System.Text.Encoding.UTF8.GetBytes(csv),
                        "text/csv",
                        $"{route}-export.csv");
                }

                return Results.BadRequest(new { message = "Unsupported format. Use 'csv'." });
            })
            .WithName($"Export{tag}")
            .WithTags(tag)
            .Produces(200)
            .ProducesProblem(400);
        }

        // Apply entity-level auth attributes (default — can be overridden by fluent Authorize())
        ApplyEntityAuth<TEntity>(group, route);

        // Auto-discover [ChildOf] children and register their detail endpoints
        var endpointGroupRo = new CrudEndpointGroup<TEntity>(group, app, route);
        AutoRegisterChildEndpoints<TEntity>(app, route, endpointGroupRo);
        return endpointGroupRo;
    }

    /// <summary>
    /// Maps read-only endpoints without an explicit route.
    /// Route is derived from [CrudEntity(Table=...)] or falls back to entity name + "s".
    /// </summary>
    public static CrudEndpointGroup<TEntity> MapCrudEndpoints<TEntity>(
        this WebApplication app)
        where TEntity : class, IEntity
    {
        var route = ResolveRoute<TEntity>();
        return app.MapCrudEndpoints<TEntity>(route);
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
            var guid = ParseGuid(id, typeof(TEntity).Name);
            var entity = await repo.FindByIdOrDefault(guid, ct);
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
            var db = CrudEndpointMapper.ResolveDbContextFor<TEntity>(httpCtx.RequestServices);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
                var globalHooks = httpCtx.RequestServices.GetServices<IGlobalCrudHook>().ToList();
                var entity = await repo.Create(dto, ct);

                var appCtx = BuildAppContext(httpCtx);

                // Global before hooks run first
                foreach (var gh in globalHooks)
                    await gh.BeforeCreate(entity, appCtx);

                if (hooks != null)
                {
                    await hooks.BeforeCreate(entity, appCtx);
                    await db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);

                if (hooks != null)
                    await hooks.AfterCreate(entity, appCtx);

                // Global after hooks run last
                foreach (var gh in globalHooks)
                    await gh.AfterCreate(entity, appCtx);

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
                    existingEntity = await db.Set<TEntity>().AsNoTracking()
                        .FirstOrDefaultAsync(e => e.Id == guid, ct);
                }

                var entity = await repo.Update(guid, dto, ct);

                var appCtx = BuildAppContext(httpCtx);

                // Global before hooks run first
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

                // Global after hooks run last
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
            var guid = ParseGuid(id, typeof(TEntity).Name);
            var db = CrudEndpointMapper.ResolveDbContextFor<TEntity>(httpCtx.RequestServices);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
                var globalHooks = httpCtx.RequestServices.GetServices<IGlobalCrudHook>().ToList();
                var appCtx = BuildAppContext(httpCtx);

                // Load entity for Before hooks (needed by both global and entity-specific)
                TEntity? entityForBefore = null;
                if (hooks != null || globalHooks.Count > 0)
                {
                    entityForBefore = await repo.FindByIdOrDefault(guid, ct);
                    if (entityForBefore is null)
                        throw AppError.NotFound($"{typeof(TEntity).Name} with id '{id}' was not found.");
                }

                // Global before hooks run first
                if (entityForBefore != null)
                {
                    foreach (var gh in globalHooks)
                        await gh.BeforeDelete(entityForBefore, appCtx);
                }

                if (hooks != null && entityForBefore != null)
                    await hooks.BeforeDelete(entityForBefore, appCtx);

                await repo.Delete(guid, ct);
                await tx.CommitAsync(ct);

                // AfterDelete receives a minimal entity with just the Id
                var stub = Activator.CreateInstance<TEntity>();
                stub.Id = guid;

                if (hooks != null)
                    await hooks.AfterDelete(stub, appCtx);

                // Global after hooks run last
                foreach (var gh in globalHooks)
                    await gh.AfterDelete(stub, appCtx);

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
                var guid = ParseGuid(id, typeof(TEntity).Name);
                var db = CrudEndpointMapper.ResolveDbContextFor<TEntity>(httpCtx.RequestServices);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
                    var appCtx = BuildAppContext(httpCtx);

                    await repo.Restore(guid, ct);

                    if (hooks != null)
                    {
                        var entity = await repo.FindById(guid, ct);
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
            // Note: Restore is not part of the standard CRUD lifecycle for IGlobalCrudHook
            // (no BeforeRestore/AfterRestore on IGlobalCrudHook — entity-specific hooks handle it)
            .WithName($"Restore{tag}")
            .Produces(200)
            .ProducesProblem(404)
            .ProducesProblem(500);

            // DELETE /api/{route}/{id}/purge — Permanently delete a single soft-deleted record
            group.MapDelete("/{id}/purge", async (string id, IRepo<TEntity> repo, CancellationToken ct) =>
            {
                var guid = ParseGuid(id, typeof(TEntity).Name);
                await repo.HardDelete(guid, ct);
                return Results.NoContent();
            })
            .WithName($"PurgeSingle{tag}")
            .WithTags(tag)
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(404);

            // DELETE /api/{route}/purge?olderThan=N — Permanently delete old soft-deleted records
            group.MapDelete("/purge", async (HttpContext httpCtx, int olderThan, CancellationToken ct) =>
            {
                if (olderThan < 1)
                    throw AppError.BadRequest("olderThan must be at least 1 day.");

                var db = CrudEndpointMapper.ResolveDbContextFor<TEntity>(httpCtx.RequestServices);
                var cutoff = DateTime.UtcNow.AddDays(-olderThan);

                // Disable only soft-delete filter — tenant filter stays active (no cross-tenant leak)
                var softDeleteFilter = httpCtx.RequestServices.GetService<IDataFilter<ISoftDeletable>>();
                using (softDeleteFilter?.Disable())
                {
                    var query = db.Set<TEntity>()
                        .Where(e => ((ISoftDeletable)e).DeletedAt != null && ((ISoftDeletable)e).DeletedAt < cutoff);

                    var purged = await query.ExecuteDeleteAsync(ct);
                    return Results.Ok(new { purged });
                }
            })
            .WithName($"Purge{tag}")
            .WithTags(tag)
            .Produces(200)
            .ProducesProblem(400);
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

        // GET /api/{route}/export — Export (3-level: [NotExportable] class > [Exportable] class > global UseExport())
        var exportOpts = app.Services.GetService<Configuration.CrudKitApiOptions>();
        if (FeatureResolver.IsExportEnabled<TEntity>(exportOpts?.ExportEnabled ?? false))
        {
            group.MapGet("/export", async (HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
            {
                var listParams = ListParams.FromQuery(httpCtx.Request.Query);
                var format = httpCtx.Request.Query["format"].FirstOrDefault() ?? "csv";

                var apiOpts = httpCtx.RequestServices.GetService<Configuration.CrudKitApiOptions>();
                var maxRows = apiOpts?.MaxExportRows ?? 50_000;

                // Count first to prevent memory DoS
                var count = await repo.BulkCount(listParams.Filters, ct);
                if (count > maxRows)
                    throw AppError.BadRequest($"Export would return {count} rows, exceeding the limit of {maxRows}. Narrow your filters.");

                listParams.Page = 1;
                listParams.PerPage = maxRows;
                var result = await repo.List(listParams, ct);

                if (format == "csv")
                {
                    var csv = CsvExportService.Export(result.Data);
                    return Results.File(
                        System.Text.Encoding.UTF8.GetBytes(csv),
                        "text/csv",
                        $"{route}-export.csv");
                }

                return Results.BadRequest(new { message = "Unsupported format. Use 'csv'." });
            })
            .WithName($"Export{tag}")
            .WithTags(tag)
            .Produces(200)
            .ProducesProblem(400);
        }

        // POST /api/{route}/import — Import (3-level: [NotImportable] class > [Importable] class > global UseImport())
        var importOpts = app.Services.GetService<Configuration.CrudKitApiOptions>();
        if (FeatureResolver.IsImportEnabled<TEntity>(importOpts?.ImportEnabled ?? false))
        {
            group.MapPost("/import", async (HttpContext httpCtx, CancellationToken ct) =>
            {
                var db = CrudEndpointMapper.ResolveDbContextFor<TEntity>(httpCtx.RequestServices);
                var form = await httpCtx.Request.ReadFormAsync(ct);
                var file = form.Files.FirstOrDefault();
                if (file == null || file.Length == 0)
                    throw AppError.BadRequest("No file uploaded.");

                var apiOpts = httpCtx.RequestServices.GetService<Configuration.CrudKitApiOptions>();
                if (apiOpts != null && file.Length > apiOpts.MaxImportFileSize)
                    throw AppError.BadRequest($"File size ({file.Length / 1024 / 1024} MB) exceeds the limit of {apiOpts.MaxImportFileSize / 1024 / 1024} MB.");

                using var reader = new StreamReader(file.OpenReadStream());
                var content = await reader.ReadToEndAsync(ct);

                var (rows, parseErrors) = CsvImportService.Parse<TEntity>(content);

                var importResult = new ImportResult { Total = rows.Count + parseErrors.Count };
                importResult.Errors.AddRange(parseErrors);

                // Create each valid row via direct entity creation
                var importAppCtx = BuildAppContext(httpCtx);
                var importGlobalHooks = httpCtx.RequestServices.GetServices<IGlobalCrudHook>().ToList();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    foreach (var (row, rowIndex) in rows.Select((r, i) => (r, i + 2))) // +2: header=1, first data=2
                    {
                        try
                        {
                            var entity = Activator.CreateInstance<TEntity>();
                            foreach (var (key, value) in row)
                            {
                                var prop = typeof(TEntity).GetProperty(key,
                                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                                if (prop != null && prop.CanWrite && value != null)
                                    prop.SetValue(entity, value);
                            }

                            // Call global before hooks per entity
                            foreach (var gh in importGlobalHooks)
                                await gh.BeforeCreate(entity, importAppCtx);

                            db.Set<TEntity>().Add(entity);
                            await db.SaveChangesAsync(ct);

                            // Call global after hooks per entity
                            foreach (var gh in importGlobalHooks)
                                await gh.AfterCreate(entity, importAppCtx);

                            importResult.Created++;
                        }
                        catch (Exception ex)
                        {
                            importResult.Failed++;
                            importResult.Errors.Add(new ImportError
                            {
                                Row = rowIndex,
                                Message = ex is AppError appErr ? appErr.Message : ex.Message
                            });
                        }
                    }

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }

                return Results.Ok(importResult);
            })
            .WithName($"Import{tag}")
            .WithTags(tag)
            .Produces<ImportResult>(200)
            .ProducesProblem(400)
            .DisableAntiforgery();
        }

        // Apply entity-level auth attributes (default — can be overridden by fluent Authorize())
        ApplyEntityAuth<TEntity>(group, route);

        // Auto-discover [ChildOf] children and register their detail endpoints
        var endpointGroup = new CrudEndpointGroup<TEntity>(group, app, route);
        AutoRegisterChildEndpoints<TEntity>(app, route, endpointGroup);
        return endpointGroup;
    }

    /// <summary>
    /// Maps full CRUD endpoints without an explicit route.
    /// Route is derived from [CrudEntity(Table=...)] or falls back to entity name + "s".
    /// </summary>
    public static CrudEndpointGroup<TEntity> MapCrudEndpoints<TEntity, TCreate, TUpdate>(
        this WebApplication app)
        where TEntity : class, IEntity
        where TCreate : class
        where TUpdate : class
    {
        var route = ResolveRoute<TEntity>();
        return app.MapCrudEndpoints<TEntity, TCreate, TUpdate>(route);
    }

    /// <summary>
    /// Resolves the route from [CrudEntity(Table=...)] or falls back to entity name + "s".
    /// Converts underscores to dashes for URL convention.
    /// </summary>
    private static string ResolveRoute<TEntity>()
    {
        var attr = typeof(TEntity).GetCustomAttribute<CrudEntityAttribute>();
        var table = attr?.Table;
        if (string.IsNullOrEmpty(table))
            table = typeof(TEntity).Name.ToLowerInvariant() + "s";
        // Convert underscores to dashes for URL convention
        return table.Replace('_', '-');
    }

    /// <summary>
    /// Converts a PascalCase type name to kebab-case.
    /// Example: OrderLine → order-line.
    /// </summary>
    internal static string ToKebabCase(string name)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
                result.Append('-');
            result.Append(char.ToLowerInvariant(name[i]));
        }
        return result.ToString();
    }

    /// <summary>
    /// Registers List, Get, Delete, and (when a matching <c>[CreateDtoFor]</c> is found) POST
    /// endpoints for a child entity discovered via <c>[ChildOf]</c>.
    /// Auto-discovered at startup inside <see cref="MapCrudEndpoints{TEntity}(WebApplication,string)"/>.
    /// Endpoint names use an <c>__auto</c> suffix so they never collide with names registered
    /// by an explicit <c>WithChild</c> call on the same pair of types.
    /// </summary>
    internal static void RegisterChildEndpoints<TMaster, TDetail>(
        WebApplication app, string masterRoute, string detailRoute, string foreignKey,
        CrudEndpointGroup<TMaster>? endpointGroup = null)
        where TMaster : class, IEntity
        where TDetail : class, IEntity
    {
        var masterTag = typeof(TMaster).Name.Replace("Entity", "");
        var detailTag = typeof(TDetail).Name.Replace("Entity", "");
        var tag = $"{masterTag}{detailTag}";

        var group = app.MapGroup($"/api/{masterRoute}/{{masterId}}/{detailRoute}").WithTags(tag);
        group.AddEndpointFilter<AppErrorFilter>();

        // Store group reference so WithChild can reuse it instead of creating a duplicate
        endpointGroup?.AutoDiscoveredGroups.TryAdd(typeof(TDetail), group);

        var fkProp = typeof(TDetail).GetProperty(foreignKey,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(
                $"[ChildOf] FK property '{foreignKey}' not found on {typeof(TDetail).Name}.");

        // GET /api/{masterRoute}/{masterId}/{detailRoute} — List details by master
        group.MapGet("/", async (string masterId, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            var masterGuid = ParseGuid(masterId, typeof(TMaster).Name);
            if (!await masterRepo.Exists(masterGuid, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var details = await detailRepo.FindByField(foreignKey, masterId, ct);
            return Results.Ok(details);
        })
        .WithName($"List{tag}__auto")
        .Produces<List<TDetail>>(200)
        .ProducesProblem(404)
        .ProducesProblem(500);

        // GET /api/{masterRoute}/{masterId}/{detailRoute}/{id} — Get single detail
        group.MapGet("/{id}", async (string masterId, string id, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            var masterGuid = ParseGuid(masterId, typeof(TMaster).Name);
            var detailGuid = ParseGuid(id, typeof(TDetail).Name);
            if (!await masterRepo.Exists(masterGuid, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var entity = await detailRepo.FindByIdOrDefault(detailGuid, ct);
            if (entity is null)
                return Results.Json(new { status = 404, code = "NOT_FOUND", message = $"{typeof(TDetail).Name} with id '{id}' was not found." }, statusCode: 404);

            var fkValue = fkProp.GetValue(entity)?.ToString();
            if (fkValue != masterId)
                return Results.Json(new { status = 404, code = "NOT_FOUND", message = $"{typeof(TDetail).Name} with id '{id}' was not found." }, statusCode: 404);

            return Results.Ok(entity);
        })
        .WithName($"Get{tag}__auto")
        .Produces<TDetail>(200)
        .ProducesProblem(404)
        .ProducesProblem(500);

        // DELETE /api/{masterRoute}/{masterId}/{detailRoute}/{id} — Delete detail
        group.MapDelete("/{id}", async (string masterId, string id, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            var masterGuid = ParseGuid(masterId, typeof(TMaster).Name);
            var detailGuid = ParseGuid(id, typeof(TDetail).Name);
            if (!await masterRepo.Exists(masterGuid, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var entity = await detailRepo.FindByIdOrDefault(detailGuid, ct);
            if (entity is null)
                throw AppError.NotFound($"{typeof(TDetail).Name} with id '{id}' was not found.");

            var fkValue = fkProp.GetValue(entity)?.ToString();
            if (fkValue != masterId)
                throw AppError.NotFound($"{typeof(TDetail).Name} with id '{id}' was not found.");

            await detailRepo.Delete(detailGuid, ct);
            return Results.NoContent();
        })
        .WithName($"Delete{tag}__auto")
        .Produces(204)
        .ProducesProblem(404)
        .ProducesProblem(500);

        // Auto-discover [CreateDtoFor(typeof(TDetail))] and register POST if found
        var createDtoType = FindCreateDtoFor(typeof(TDetail));
        if (createDtoType != null)
        {
            var registerCreateMethod = typeof(CrudEndpointMapper)
                .GetMethod(nameof(RegisterChildCreateEndpoint), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(typeof(TMaster), typeof(TDetail), createDtoType);
            registerCreateMethod.Invoke(null, [group, masterRoute, detailRoute, foreignKey]);
        }

        // Auto-discover [UpdateDtoFor(typeof(TDetail))] and register PUT if found
        var updateDtoType = FindUpdateDtoFor(typeof(TDetail));
        if (updateDtoType != null)
        {
            var registerUpdateMethod = typeof(CrudEndpointMapper)
                .GetMethod(nameof(RegisterChildUpdateEndpoint), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(typeof(TMaster), typeof(TDetail), updateDtoType);
            registerUpdateMethod.Invoke(null, [group, masterRoute, detailRoute, foreignKey]);
        }
    }

    /// <summary>
    /// Scans all loaded assemblies for a type decorated with <c>[CreateDtoFor(typeof(childType))]</c>.
    /// Returns the first match, or null if none is found.
    /// Assembly load exceptions are swallowed to allow partial-trust environments.
    /// </summary>
    private static Type? FindCreateDtoFor(Type childType)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<CreateDtoForAttribute>();
                if (attr?.EntityType == childType) return type;
            }
        }
        return null;
    }

    /// <summary>
    /// Scans all loaded assemblies for a type decorated with <c>[UpdateDtoFor(typeof(childType))]</c>.
    /// Returns the first match, or null if none is found.
    /// </summary>
    private static Type? FindUpdateDtoFor(Type childType)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<UpdateDtoForAttribute>();
                if (attr?.EntityType == childType) return type;
            }
        }
        return null;
    }

    /// <summary>
    /// Registers a POST (create) endpoint for a child entity using the discovered <typeparamref name="TCreateDto"/>.
    /// Called via reflection from <see cref="RegisterChildEndpoints{TMaster,TDetail}"/> when a
    /// <c>[CreateDtoFor(typeof(TDetail))]</c>-decorated DTO is found at startup.
    /// The FK on the DTO is auto-set from the URL <c>masterId</c> segment before creation.
    /// </summary>
    private static void RegisterChildCreateEndpoint<TMaster, TDetail, TCreateDto>(
        RouteGroupBuilder group, string masterRoute, string detailRoute, string foreignKey)
        where TMaster : class, IEntity
        where TDetail : class, IEntity
        where TCreateDto : class
    {
        var masterTag = typeof(TMaster).Name.Replace("Entity", "");
        var detailTag = typeof(TDetail).Name.Replace("Entity", "");
        var tag = $"{masterTag}{detailTag}";

        // Resolve the FK property on the DTO once at registration time (may be null — handled at runtime)
        var fkProp = typeof(TCreateDto).GetProperty(foreignKey,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        // POST /api/{masterRoute}/{masterId}/{detailRoute} — Create detail via auto-discovered DTO
        group.MapPost("/", async (string masterId, TCreateDto dto, HttpContext httpCtx,
            IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            var masterGuid = ParseGuid(masterId, typeof(TMaster).Name);
            if (!await masterRepo.Exists(masterGuid, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            // Auto-set FK on DTO from the URL segment
            if (fkProp != null)
            {
                var fkValue = ConvertFkValue(masterId, fkProp.PropertyType);
                fkProp.SetValue(dto, fkValue);
            }

            var db = ResolveDbContextFor<TDetail>(httpCtx.RequestServices);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var entity = await detailRepo.Create(dto, ct);
                await tx.CommitAsync(ct);
                return Results.Created($"/api/{masterRoute}/{masterId}/{detailRoute}/{entity.Id}", entity);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .AddEndpointFilter<ValidationFilter<TCreateDto>>()
        .WithName($"Create{tag}__auto")
        .Accepts<TCreateDto>("application/json")
        .Produces<TDetail>(201)
        .ProducesProblem(400)
        .ProducesProblem(404)
        .ProducesProblem(500);
    }

    /// <summary>
    /// Registers a PUT (update) endpoint for a child entity using the discovered <typeparamref name="TUpdateDto"/>.
    /// Called via reflection from <see cref="RegisterChildEndpoints{TMaster,TDetail}"/> when a
    /// <c>[UpdateDtoFor(typeof(TDetail))]</c>-decorated DTO is found at startup.
    /// </summary>
    private static void RegisterChildUpdateEndpoint<TMaster, TDetail, TUpdateDto>(
        RouteGroupBuilder group, string masterRoute, string detailRoute, string foreignKey)
        where TMaster : class, IEntity
        where TDetail : class, IEntity
        where TUpdateDto : class
    {
        var masterTag = typeof(TMaster).Name.Replace("Entity", "");
        var detailTag = typeof(TDetail).Name.Replace("Entity", "");
        var tag = $"{masterTag}{detailTag}";

        var fkProp = typeof(TDetail).GetProperty(foreignKey,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;

        group.MapPut("/{id}", async (string masterId, string id, TUpdateDto dto,
            HttpContext httpCtx, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            var masterGuid = ParseGuid(masterId, typeof(TMaster).Name);
            var detailGuid = ParseGuid(id, typeof(TDetail).Name);
            if (!await masterRepo.Exists(masterGuid, ct))
                throw AppError.NotFound($"{typeof(TMaster).Name} with id '{masterId}' was not found.");

            var existing = await detailRepo.FindByIdOrDefault(detailGuid, ct);
            if (existing is null)
                throw AppError.NotFound($"{typeof(TDetail).Name} with id '{id}' was not found.");

            var fkValue = fkProp.GetValue(existing)?.ToString();
            if (fkValue != masterId)
                throw AppError.NotFound($"{typeof(TDetail).Name} with id '{id}' was not found.");

            var db = ResolveDbContextFor<TDetail>(httpCtx.RequestServices);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var entity = await detailRepo.Update(detailGuid, dto, ct);
                await tx.CommitAsync(ct);
                return Results.Ok(entity);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .WithName($"Update{tag}__auto")
        .AddEndpointFilter<ValidationFilter<TUpdateDto>>()
        .Produces<TDetail>(200)
        .ProducesProblem(400)
        .ProducesProblem(404)
        .ProducesProblem(500);
    }

    /// <summary>
    /// Scans all loaded assemblies for types decorated with <c>[ChildOf(typeof(TEntity))]</c>
    /// and calls <see cref="RegisterChildEndpoints{TMaster,TDetail}"/> for each discovered child.
    /// Types already present in <paramref name="group"/>.RegisteredDetailTypes (registered via
    /// an explicit <c>WithChild</c> call) are skipped to avoid duplicate route registration.
    /// </summary>
    private static void AutoRegisterChildEndpoints<TEntity>(WebApplication app, string route, CrudEndpointGroup<TEntity> group)
        where TEntity : class, IEntity
    {
        var parentType = typeof(TEntity);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var childType in types)
            {
                var childOfAttr = childType.GetCustomAttributes<ChildOfAttribute>()
                    .FirstOrDefault(a => a.ParentType == parentType);

                if (childOfAttr is null) continue;
                if (!typeof(IEntity).IsAssignableFrom(childType)) continue;

                // Skip types already registered explicitly via WithChild
                if (group.RegisteredDetailTypes.Contains(childType)) continue;

                group.RegisteredDetailTypes.Add(childType);

                var detailRoute = childOfAttr.Route
                    ?? ToKebabCase(childType.Name) + "s";

                var foreignKey = childOfAttr.ForeignKey
                    ?? parentType.Name + "Id";

                // Invoke the generic helper via reflection since TDetail is only known at runtime.
                // Pass the endpointGroup so the route group reference can be stored for WithChild reuse.
                var method = typeof(CrudEndpointMapper)
                    .GetMethod(nameof(RegisterChildEndpoints),
                        BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(parentType, childType);

                method.Invoke(null, [app, route, detailRoute, foreignKey, group]);
            }
        }
    }

    /// <summary>
    /// Reads auth-related attributes from the entity type and applies matching filters
    /// to the route group. Entity-level auth is the minimum default; fluent Authorize()
    /// stacks on top (more restrictive).
    /// </summary>
    private static void ApplyEntityAuth<TEntity>(RouteGroupBuilder group, string route)
    {
        var entityType = typeof(TEntity);

        // [RequireAuth] — simple authentication
        if (entityType.GetCustomAttribute<RequireAuthAttribute>() != null)
        {
            group.AddEndpointFilter<RequireAuthFilter>();
        }

        // [RequireRole("admin")] — role for all operations
        var roleAttr = entityType.GetCustomAttribute<RequireRoleAttribute>();
        if (roleAttr != null)
        {
            group.AddEndpointFilter(new RequireRoleFilter(roleAttr.Role));
        }

        // [RequirePermissions] — convention permissions
        if (entityType.GetCustomAttribute<RequirePermissionsAttribute>() != null)
        {
            var authBuilder = new Configuration.EndpointAuthorizationBuilder();
            authBuilder.RequirePermissions();
            group.AddEndpointFilter(new CrudAuthorizationFilter(authBuilder, route));
        }

        // [AuthorizeOperation("Read", "user")] — per-operation
        var opAttrs = entityType.GetCustomAttributes<AuthorizeOperationAttribute>();
        if (opAttrs.Any())
        {
            var authBuilder = new Configuration.EndpointAuthorizationBuilder();
            foreach (var attr in opAttrs)
            {
                var op = attr.Operation.ToLowerInvariant() switch
                {
                    "read" => authBuilder.Read,
                    "create" => authBuilder.Create,
                    "update" => authBuilder.Update,
                    "delete" => authBuilder.Delete,
                    "restore" => authBuilder.Restore,
                    "transition" => authBuilder.Transition,
                    "export" => authBuilder.Export,
                    "import" => authBuilder.Import,
                    _ => (Configuration.OperationAuth?)null
                };
                op?.RequireRole(attr.Role);
            }
            group.AddEndpointFilter(new CrudAuthorizationFilter(authBuilder, route));
        }
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
            var guid = ParseGuid(id, typeof(TEntity).Name);
            var db = CrudEndpointMapper.ResolveDbContextFor<TEntity>(httpCtx.RequestServices);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // Load entity for update (tracked)
                var dbSet = db.Set<TEntity>();
                var entity = await dbSet.FirstOrDefaultAsync(e => e.Id == guid, ct)
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
        var mapperInterfaceBase = typeof(IResponseMapper<,>);

        using var scope = services.CreateScope();
        var serviceCollection = services.GetService<IServiceProviderIsService>();
        if (serviceCollection is null) return null;

        var entityType = typeof(TEntity);

        var descriptors = services.GetService<IServiceCollection>();

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

    /// <summary>
    /// Parses a string route parameter to a Guid. Throws NotFound on invalid format.
    /// </summary>
    internal static Guid ParseGuid(string value, string entityName)
    {
        if (Guid.TryParse(value, out var guid))
            return guid;
        throw AppError.NotFound($"{entityName} with id '{value}' was not found.");
    }

    /// <summary>
    /// Converts a string value to the target property type for FK assignment.
    /// Handles Guid, string, and other convertible types.
    /// </summary>
    internal static object? ConvertFkValue(string value, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying == typeof(string)) return value;
        if (underlying == typeof(Guid)) return Guid.Parse(value);
        return Convert.ChangeType(value, underlying);
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
            CurrentUser = httpCtx.RequestServices.GetRequiredService<ICurrentUser>(),
            TenantContext = httpCtx.RequestServices.GetService<ITenantContext>()
        };
    }

    /// <summary>
    /// Resolve the correct ICrudKitDbContext for entity type <typeparamref name="TEntity"/>
    /// using the context registry when available, falling back to the default ICrudKitDbContext.
    /// </summary>
    internal static ICrudKitDbContext ResolveDbContextFor<TEntity>(IServiceProvider services) where TEntity : class
    {
        var registry = services.GetService<CrudKitContextRegistry>();
        if (registry != null)
            return registry.ResolveFor<TEntity>(services);
        return services.GetRequiredService<ICrudKitDbContext>();
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
