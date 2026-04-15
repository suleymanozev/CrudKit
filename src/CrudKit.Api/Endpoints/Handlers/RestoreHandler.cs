using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps POST /{id}/restore endpoint for restoring soft-deleted entities.</summary>
internal static class RestoreHandler
{
    public static void Map<TEntity>(RouteGroupBuilder group, string tag)
        where TEntity : class, IEntity
    {
        group.MapPost("/{id}/restore", async (string id, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var guid = CrudEndpointMapper.ParseGuid(id, typeof(TEntity).Name);
            var db = CrudEndpointMapper.ResolveDbContextFor<TEntity>(httpCtx.RequestServices);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
            var appCtx = CrudEndpointMapper.BuildAppContext(httpCtx);

            if (hooks is not null)
            {
                var deletedEntity = await repo.FindDeletedById(guid, ct)
                    ?? throw AppError.NotFound($"{typeof(TEntity).Name} with id '{id}' was not found.");
                await hooks.BeforeRestore(deletedEntity, appCtx);
            }

            await repo.Restore(guid, ct);

            if (hooks is not null)
            {
                var entity = await repo.FindById(guid, ct);
                await hooks.AfterRestore(entity, appCtx);
            }

            await tx.CommitAsync(ct);
            return Results.Ok();
        })
        // Note: Restore is not part of the standard CRUD lifecycle for IGlobalCrudHook
        // (no BeforeRestore/AfterRestore on IGlobalCrudHook -- entity-specific hooks handle it)
        .WithName($"Restore{tag}")
        .Produces(200)
        .ProducesProblem(404)
        .ProducesProblem(500);
    }
}
