using System.Linq.Expressions;
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
/// Maps nested CRUD endpoints for master-detail (parent-child) relationships.
/// </summary>
public static class DetailEndpointMapper
{
    /// <summary>
    /// Maps detail endpoints: list, get, create, delete, and batch upsert for a child entity
    /// scoped under a master entity route.
    /// </summary>
    public static RouteGroupBuilder MapCrudDetailEndpoints<TMaster, TDetail, TCreateDetail>(
        this WebApplication app,
        string masterRoute,
        string detailRoute,
        string foreignKeyProperty)
        where TMaster : class, IEntity
        where TDetail : class, IEntity
        where TCreateDetail : class
    {
        var masterTag = typeof(TMaster).Name.Replace("Entity", "");
        var detailTag = typeof(TDetail).Name.Replace("Entity", "");
        var tag = $"{masterTag}{detailTag}";

        var group = app.MapGroup($"/api/{masterRoute}/{{masterId}}/{detailRoute}").WithTags(tag);
        group.AddEndpointFilter<AppErrorFilter>();

        var fkProp = typeof(TDetail).GetProperty(foreignKeyProperty,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(
                $"Property '{foreignKeyProperty}' not found on {typeof(TDetail).Name}.");

        // GET /api/{masterRoute}/{masterId}/{detailRoute} — List details by master
        group.MapGet("/", async (string masterId, IRepo<TMaster> masterRepo, IRepo<TDetail> detailRepo, CancellationToken ct) =>
        {
            // Verify master exists
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

            // Verify the detail belongs to the master
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

            // Auto-set the FK property on the DTO (if it exists) or on the entity after creation
            var dtoFkProp = typeof(TCreateDetail).GetProperty(foreignKeyProperty,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (dtoFkProp != null)
                dtoFkProp.SetValue(dto, masterId);

            var db = httpCtx.RequestServices.GetRequiredService<CrudKitDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var entity = await detailRepo.Create(dto, ct);

                // Ensure FK is set on the entity even if the DTO didn't have the property
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

            // Verify the detail belongs to the master before deleting
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
                // Delete all existing details for this master
                var existing = await detailRepo.FindByField(foreignKeyProperty, masterId, ct);
                foreach (var entity in existing)
                    await detailRepo.Delete(entity.Id, ct);

                // Create new details
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

        return group;
    }
}
