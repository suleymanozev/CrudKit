using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps DELETE /{id} endpoint for deleting an entity (soft or hard).</summary>
internal static class DeleteHandler
{
    public static void Map<TEntity>(RouteGroupBuilder group, string tag)
        where TEntity : class, IEntity
    {
        group.MapDelete("/{id}", async (string id, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var guid = CrudEndpointMapper.ParseGuid(id, typeof(TEntity).Name);
            await using var tx = await repo.BeginTransactionAsync(ct);
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
