using CrudKit.Api.Filters;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps PUT /{id} endpoint for updating an existing entity.</summary>
internal static class UpdateHandler
{
    public static void Map<TEntity, TUpdate>(RouteGroupBuilder group, string tag)
        where TEntity : class, IEntity
        where TUpdate : class
    {
        group.MapPut("/{id}", async (string id, TUpdate dto, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var guid = CrudEndpointMapper.ParseGuid(id, typeof(TEntity).Name);
            var db = CrudEndpointMapper.ResolveDbContextFor<TEntity>(httpCtx.RequestServices);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
                var globalHooks = httpCtx.RequestServices.GetServices<IGlobalCrudHook>().ToList();

                // Capture existing entity state before update (detached snapshot)
                TEntity? existingEntity = null;
                if (globalHooks.Count > 0 || hooks is not null)
                {
                    existingEntity = await db.Set<TEntity>().AsNoTracking()
                        .FirstOrDefaultAsync(e => e.Id == guid, ct);
                }

                var entity = await repo.Update(guid, dto, ct);

                var appCtx = CrudEndpointMapper.BuildAppContext(httpCtx);

                // Global before hooks run first
                foreach (var gh in globalHooks)
                    await gh.BeforeUpdate(entity, existingEntity, appCtx);

                if (hooks is not null)
                {
                    await hooks.BeforeUpdate(entity, existingEntity, appCtx);
                    await db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);

                if (hooks is not null)
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
    }
}
