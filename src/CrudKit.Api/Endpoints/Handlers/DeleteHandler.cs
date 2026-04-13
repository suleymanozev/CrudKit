using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps DELETE /{id} endpoint for deleting an entity (soft or hard).</summary>
internal static class DeleteHandler
{
    public static void Map<TEntity>(RouteGroupBuilder group, string tag)
        where TEntity : class, IEntity
    {
        group.MapDelete("/{id}", async (string id, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var guid = CrudEndpointMapper.ParseGuid(id, typeof(TEntity).Name);
            var db = CrudEndpointMapper.ResolveDbContextFor<TEntity>(httpCtx.RequestServices);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
            var globalHooks = httpCtx.RequestServices.GetServices<IGlobalCrudHook>().ToList();
            var appCtx = CrudEndpointMapper.BuildAppContext(httpCtx);

            // Load entity for Before hooks (needed by both global and entity-specific)
            TEntity? entityForBefore = null;
            if (hooks is not null || globalHooks.Count > 0)
            {
                entityForBefore = await repo.FindByIdOrDefault(guid, ct);
                if (entityForBefore is null)
                    throw AppError.NotFound($"{typeof(TEntity).Name} with id '{id}' was not found.");
            }

            // Global before hooks run first
            if (entityForBefore is not null)
            {
                foreach (var gh in globalHooks)
                    await gh.BeforeDelete(entityForBefore, appCtx);
            }

            if (hooks is not null && entityForBefore is not null)
                await hooks.BeforeDelete(entityForBefore, appCtx);

            await repo.Delete(guid, ct);
            await tx.CommitAsync(ct);

            // AfterDelete receives a minimal entity with just the Id
            var stub = Activator.CreateInstance<TEntity>();
            stub.Id = guid;

            if (hooks is not null)
                await hooks.AfterDelete(stub, appCtx);

            // Global after hooks run last
            foreach (var gh in globalHooks)
                await gh.AfterDelete(stub, appCtx);

            return Results.NoContent();
        })
        .WithName($"Delete{tag}")
        .Produces(204)
        .ProducesProblem(404)
        .ProducesProblem(500);
    }
}
