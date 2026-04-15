using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps DELETE /{id}/purge and DELETE /purge endpoints for permanently deleting soft-deleted entities.</summary>
internal static class PurgeHandler
{
    public static void Map<TEntity>(RouteGroupBuilder group, string tag)
        where TEntity : class, IEntity
    {
        // DELETE /api/{route}/{id}/purge -- Permanently delete a single soft-deleted record
        group.MapDelete("/{id}/purge", async (string id, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var guid = CrudEndpointMapper.ParseGuid(id, typeof(TEntity).Name);

            await WritePurgeAudit<TEntity>(httpCtx, [guid.ToString()], ct);
            await repo.HardDelete(guid, ct);

            return Results.NoContent();
        })
        .WithName($"PurgeSingle{tag}")
        .WithTags(tag)
        .Produces(204)
        .ProducesProblem(400)
        .ProducesProblem(404);

        // DELETE /api/{route}/purge?olderThan=N -- Permanently delete old soft-deleted records
        group.MapDelete("/purge", async (HttpContext httpCtx, int olderThan, CancellationToken ct) =>
        {
            if (olderThan < 1)
                throw AppError.BadRequest("olderThan must be at least 1 day.");

            var db = CrudEndpointMapper.ResolveDbContextFor<TEntity>(httpCtx.RequestServices);
            var cutoff = DateTime.UtcNow.AddDays(-olderThan);

            // Disable only soft-delete filter -- tenant filter stays active (no cross-tenant leak)
            var softDeleteFilter = httpCtx.RequestServices.GetService<IDataFilter<ISoftDeletable>>();
            using (softDeleteFilter?.Disable())
            {
                // Collect IDs before purging for audit trail
                var ids = await db.Set<TEntity>()
                    .Where(e => ((ISoftDeletable)e).DeletedAt != null && ((ISoftDeletable)e).DeletedAt < cutoff)
                    .Select(e => e.Id.ToString())
                    .ToListAsync(ct);

                if (ids.Count > 0)
                    await WritePurgeAudit<TEntity>(httpCtx, ids, ct);

                var purged = await db.Set<TEntity>()
                    .Where(e => ((ISoftDeletable)e).DeletedAt != null && ((ISoftDeletable)e).DeletedAt < cutoff)
                    .ExecuteDeleteAsync(ct);

                return Results.Ok(new { purged });
            }
        })
        .WithName($"Purge{tag}")
        .WithTags(tag)
        .Produces(200)
        .ProducesProblem(400);
    }

    private static async Task WritePurgeAudit<TEntity>(HttpContext httpCtx, List<string> entityIds, CancellationToken ct)
    {
        var auditWriter = httpCtx.RequestServices.GetService<IAuditWriter>();
        if (auditWriter is null) return;

        var currentUser = httpCtx.RequestServices.GetService<ICurrentUser>();
        var correlationId = Guid.NewGuid().ToString();

        var entries = entityIds.Select(id => new AuditEntry
        {
            EntityType = typeof(TEntity).Name,
            EntityId = id,
            Action = "Purge",
            UserId = currentUser?.Id,
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow,
        }).ToList();

        await auditWriter.WriteAsync(entries, ct);
    }
}
