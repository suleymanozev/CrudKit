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

/// <summary>Maps POST / endpoint for creating a new entity.</summary>
internal static class CreateHandler
{
    public static void Map<TEntity, TCreate>(RouteGroupBuilder group, string tag, string route)
        where TEntity : class, IEntity
        where TCreate : class
    {
        group.MapPost("/", async (TCreate dto, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var db = CrudEndpointMapper.ResolveDbContextFor<TEntity>(httpCtx.RequestServices);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
                var globalHooks = httpCtx.RequestServices.GetServices<IGlobalCrudHook>().ToList();
                var entity = await repo.Create(dto, ct);

                var appCtx = CrudEndpointMapper.BuildAppContext(httpCtx);

                // Global before hooks run first
                foreach (var gh in globalHooks)
                    await gh.BeforeCreate(entity, appCtx);

                if (hooks is not null)
                {
                    await hooks.BeforeCreate(entity, appCtx);
                    await db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);

                if (hooks is not null)
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
    }
}
