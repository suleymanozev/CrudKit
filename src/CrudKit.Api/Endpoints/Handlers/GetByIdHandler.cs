using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps GET /{id} endpoint for retrieving a single entity by ID.</summary>
internal static class GetByIdHandler
{
    public static void Map<TEntity>(RouteGroupBuilder group, string tag)
        where TEntity : class, IEntity
    {
        group.MapGet("/{id}", async (string id, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var guid = CrudEndpointMapper.ParseGuid(id, typeof(TEntity).Name);
            var entity = await repo.FindByIdOrDefault(guid, ct);
            if (entity is null)
                throw AppError.NotFound($"{typeof(TEntity).Name} with id '{id}' was not found.");

            var mapped = CrudEndpointMapper.TryMapSingle(httpCtx.RequestServices, entity);
            return Results.Ok(mapped);
        })
        .WithName($"Get{tag}")
        .Produces<TEntity>(200)
        .ProducesProblem(404)
        .ProducesProblem(500);
    }
}
