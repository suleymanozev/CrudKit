using CrudKit.Api.Filters;
using CrudKit.Core.Interfaces;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps POST / endpoint for creating a new entity.</summary>
internal static class CreateHandler
{
    public static void Map<TEntity, TCreate>(RouteGroupBuilder group, string tag, string route)
        where TEntity : class, IEntity
        where TCreate : class
    {
        group.MapPost("/", async (TCreate dto, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            await using var tx = await repo.BeginTransactionAsync(ct);
            var entity = await repo.Create(dto, ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/{route}/{entity.Id}", entity);
        })
        .WithName($"Create{tag}")
        .AddEndpointFilter<ValidationFilter<TCreate>>()
        .Accepts<TCreate>("application/json")
        .Produces<TEntity>(201)
        .ProducesProblem(400)
        .ProducesProblem(500);
    }
}
