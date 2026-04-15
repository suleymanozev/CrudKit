using CrudKit.Api.Services;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps GET /export endpoint for exporting entities as CSV.</summary>
internal static class ExportHandler
{
    public static void Map<TEntity>(RouteGroupBuilder group, string tag, string route)
        where TEntity : class, IEntity
    {
        group.MapGet("/export", async (HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var apiOpts = httpCtx.RequestServices.GetRequiredService<Configuration.CrudKitApiOptions>();
            var listParams = ListParams.FromQuery(httpCtx.Request.Query, apiOpts.MinPageSize, apiOpts.MaxPageSize);
            var format = httpCtx.Request.Query["format"].FirstOrDefault() ?? "csv";
            if (format is not "csv")
                throw AppError.BadRequest($"Unsupported export format: '{format}'. Supported: csv");

            var maxRows = apiOpts.MaxExportRows;

            // Count first to prevent memory DoS
            var count = await repo.Count(listParams.Filters, ct);
            if (count > maxRows)
                throw AppError.BadRequest($"Export would return {count} rows, exceeding the limit of {maxRows}. Narrow your filters.");

            listParams.Page = 1;
            listParams.PerPage = maxRows;
            var result = await repo.List(listParams, ct);

            var csv = CsvExportService.Export(result.Data);
            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(csv),
                "text/csv",
                $"{route}-export.csv");
        })
        .WithName($"Export{tag}")
        .WithTags(tag)
        .Produces(200)
        .ProducesProblem(400);
    }
}
