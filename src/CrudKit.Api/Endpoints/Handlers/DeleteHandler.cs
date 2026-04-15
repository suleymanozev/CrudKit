using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

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

            await repo.Delete(guid, ct);
            await tx.CommitAsync(ct);

            return Results.NoContent();
        })
        .WithName($"Delete{tag}")
        .Produces(204)
        .ProducesProblem(404)
        .ProducesProblem(500);
    }
}
