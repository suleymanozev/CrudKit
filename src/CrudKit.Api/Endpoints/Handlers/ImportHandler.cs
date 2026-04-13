using System.Reflection;
using CrudKit.Api.Models;
using CrudKit.Api.Services;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps POST /import endpoint for importing entities from CSV.</summary>
internal static class ImportHandler
{
    public static void Map<TEntity>(RouteGroupBuilder group, string tag)
        where TEntity : class, IEntity
    {
        group.MapPost("/import", async (HttpContext httpCtx, CancellationToken ct) =>
        {
            var db = CrudEndpointMapper.ResolveDbContextFor<TEntity>(httpCtx.RequestServices);
            var form = await httpCtx.Request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                throw AppError.BadRequest("No file uploaded.");

            var apiOpts = httpCtx.RequestServices.GetRequiredService<Configuration.CrudKitApiOptions>();
            if (file.Length > apiOpts.MaxImportFileSize)
                throw AppError.BadRequest($"File size ({file.Length / 1024 / 1024} MB) exceeds the limit of {apiOpts.MaxImportFileSize / 1024 / 1024} MB.");

            using var reader = new StreamReader(file.OpenReadStream());
            var content = await reader.ReadToEndAsync(ct);

            var (rows, parseErrors) = CsvImportService.Parse<TEntity>(content);

            var importResult = new ImportResult { Total = rows.Count + parseErrors.Count };
            importResult.Errors.AddRange(parseErrors);

            // Create each valid row via direct entity creation
            var importAppCtx = CrudEndpointMapper.BuildAppContext(httpCtx);
            var importGlobalHooks = httpCtx.RequestServices.GetServices<IGlobalCrudHook>().ToList();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            foreach (var (row, rowIndex) in rows.Select((r, i) => (r, i + 2))) // +2: header=1, first data=2
            {
                try
                {
                    var entity = Activator.CreateInstance<TEntity>();
                    foreach (var (key, value) in row)
                    {
                        var prop = typeof(TEntity).GetProperty(key,
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                        if (prop is not null && prop.CanWrite && value is not null)
                            prop.SetValue(entity, value);
                    }

                    // Call global before hooks per entity
                    foreach (var gh in importGlobalHooks)
                        await gh.BeforeCreate(entity, importAppCtx);

                    db.Set<TEntity>().Add(entity);
                    await db.SaveChangesAsync(ct);

                    // Call global after hooks per entity
                    foreach (var gh in importGlobalHooks)
                        await gh.AfterCreate(entity, importAppCtx);

                    importResult.Created++;
                }
                catch (Exception ex)
                {
                    importResult.Failed++;
                    importResult.Errors.Add(new ImportError
                    {
                        Row = rowIndex,
                        Message = ex is AppError appErr ? appErr.Message : ex.Message
                    });
                }
            }

            await tx.CommitAsync(ct);

            return Results.Ok(importResult);
        })
        .WithName($"Import{tag}")
        .WithTags(tag)
        .Produces<ImportResult>(200)
        .ProducesProblem(400)
        .DisableAntiforgery();
    }
}
