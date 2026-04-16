using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps POST /{id}/restore endpoint for restoring soft-deleted entities.</summary>
internal static class RestoreHandler
{
    public static void Map<TEntity>(RouteGroupBuilder group, string tag)
        where TEntity : class, IEntity
    {
        group.MapPost("/{id}/restore", async (string id, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var guid = CrudEndpointMapper.ParseGuid(id, typeof(TEntity).Name);
            await using var tx = await repo.BeginTransactionAsync(ct);
            await repo.Restore(guid, ct);
            await tx.CommitAsync(ct);
            return Results.Ok();
        })
        .WithName($"Restore{tag}")
        .Produces(200)
        .ProducesProblem(404)
        .ProducesProblem(500);
    }
}
