using CrudKit.Api.Filters;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps PUT /{id} endpoint for updating an existing entity.</summary>
internal static class UpdateHandler
{
    public static void Map<TEntity, TUpdate>(RouteGroupBuilder group, string tag)
        where TEntity : class, IEntity
        where TUpdate : class
    {
        group.MapPut("/{id}", async (string id, TUpdate dto, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var guid = CrudEndpointMapper.ParseGuid(id, typeof(TEntity).Name);
            await using var tx = await repo.BeginTransactionAsync(ct);
            var entity = await repo.Update(guid, dto, ct);
            await tx.CommitAsync(ct);
            return Results.Ok(entity);
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
