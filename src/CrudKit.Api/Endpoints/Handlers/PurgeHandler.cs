using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps DELETE /{id}/purge and DELETE /purge endpoints for permanently deleting soft-deleted entities.</summary>
internal static class PurgeHandler
{
    public static void Map<TEntity>(RouteGroupBuilder group, string tag)
        where TEntity : class, IEntity
    {
        // DELETE /api/{route}/{id}/purge -- Permanently delete a single soft-deleted record
        group.MapDelete("/{id}/purge", async (string id, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var guid = CrudEndpointMapper.ParseGuid(id, typeof(TEntity).Name);
            await repo.HardDelete(guid, ct);
            return Results.NoContent();
        })
        .WithName($"PurgeSingle{tag}")
        .WithTags(tag)
        .Produces(204)
        .ProducesProblem(400)
        .ProducesProblem(404);

        // DELETE /api/{route}/purge?olderThan=N -- Permanently delete old soft-deleted records
        group.MapDelete("/purge", async (IRepo<TEntity> repo, int olderThan, CancellationToken ct) =>
        {
            if (olderThan < 1)
                throw AppError.BadRequest("olderThan must be at least 1 day.");

            var cutoff = DateTime.UtcNow.AddDays(-olderThan);
            var purged = await repo.BulkPurge(cutoff, ct);
            return Results.Ok(new { purged });
        })
        .WithName($"Purge{tag}")
        .WithTags(tag)
        .Produces(200)
        .ProducesProblem(400);
    }
}
