using System.Text.Json;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps GET /bulk-count, POST /bulk-delete, and POST /bulk-update endpoints.</summary>
internal static class BulkHandler
{
    public static void Map<TEntity>(RouteGroupBuilder group, string tag, bool isDeleteEnabled, bool isUpdateEnabled)
        where TEntity : class, IEntity
    {
        // GET /api/{route}/bulk-count -- Count with filters
        group.MapGet("/bulk-count", async (HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var apiOpts = httpCtx.RequestServices.GetRequiredService<Configuration.CrudKitApiOptions>();
            var listParams = ListParams.FromQuery(httpCtx.Request.Query, apiOpts.MinPageSize, apiOpts.MaxPageSize);
            var count = await repo.BulkCount(listParams.Filters, ct);
            return Results.Ok(new { count });
        })
        .WithName($"BulkCount{tag}")
        .Produces<object>(200)
        .ProducesProblem(500);

        // POST /api/{route}/bulk-delete -- Bulk delete with filters
        if (isDeleteEnabled)
        group.MapPost("/bulk-delete", async (BulkDeleteRequest request, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var filters = ParseFilters(request.Filters);
            var options = httpCtx.RequestServices.GetRequiredService<Configuration.CrudKitApiOptions>();

            var count = await repo.BulkCount(filters, ct);
            if (count > options.BulkLimit)
                throw AppError.BadRequest($"Bulk operation affects {count} records, which exceeds the limit of {options.BulkLimit}.");

            var affected = await repo.BulkDelete(filters, ct);
            return Results.Ok(new { affected });
        })
        .WithName($"BulkDelete{tag}")
        .Produces<object>(200)
        .ProducesProblem(400)
        .ProducesProblem(500);

        // POST /api/{route}/bulk-update -- Bulk update with filters
        if (isUpdateEnabled)
        group.MapPost("/bulk-update", async (BulkUpdateRequest request, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var filters = ParseFilters(request.Filters);
            var values = ConvertValues(request.Values);
            var options = httpCtx.RequestServices.GetRequiredService<Configuration.CrudKitApiOptions>();

            var count = await repo.BulkCount(filters, ct);
            if (count > options.BulkLimit)
                throw AppError.BadRequest($"Bulk operation affects {count} records, which exceeds the limit of {options.BulkLimit}.");

            var affected = await repo.BulkUpdate(filters, values, ct);
            return Results.Ok(new { affected });
        })
        .WithName($"BulkUpdate{tag}")
        .Produces<object>(200)
        .ProducesProblem(400)
        .ProducesProblem(500);
    }

    private static Dictionary<string, FilterOp> ParseFilters(Dictionary<string, string>? raw)
    {
        if (raw is null || raw.Count == 0) return new();
        return raw.ToDictionary(kv => kv.Key, kv => FilterOp.Parse(kv.Value));
    }

    /// <summary>
    /// Converts values from JSON deserialization (which may contain JsonElement) to primitive types.
    /// </summary>
    private static Dictionary<string, object?> ConvertValues(Dictionary<string, object?>? raw)
    {
        if (raw is null || raw.Count == 0) return new();
        var result = new Dictionary<string, object?>(raw.Count);
        foreach (var (key, value) in raw)
        {
            result[key] = value switch
            {
                JsonElement je => je.ValueKind switch
                {
                    JsonValueKind.String => je.GetString(),
                    JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => je.GetRawText()
                },
                _ => value
            };
        }
        return result;
    }
}
