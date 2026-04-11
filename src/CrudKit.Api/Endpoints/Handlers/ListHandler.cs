using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps GET / endpoint for listing entities with pagination.</summary>
internal static class ListHandler
{
    public static void Map<TEntity>(RouteGroupBuilder group, string tag)
        where TEntity : class, IEntity
    {
        group.MapGet("/", async (HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var listParams = ListParams.FromQuery(httpCtx.Request.Query);
            var result = await repo.List(listParams, ct);
            var mapped = CrudEndpointMapper.TryMapPaginated(httpCtx.RequestServices, result);
            return Results.Ok(mapped);
        })
        .WithName($"List{tag}")
        .Produces<Paginated<TEntity>>(200)
        .ProducesProblem(500);
    }
}
