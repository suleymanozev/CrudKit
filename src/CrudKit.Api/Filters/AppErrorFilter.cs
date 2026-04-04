using CrudKit.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrudKit.Api.Filters;

public class AppErrorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var env = ctx.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        var logger = ctx.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("CrudKit.Api");

        try
        {
            return await next(ctx);
        }
        catch (AppError ex)
        {
            logger.LogWarning(ex, "AppError {StatusCode} {Code}: {Message}",
                ex.StatusCode, ex.Code, ex.Message);

            return Results.Json(new
            {
                status = ex.StatusCode,
                code = ex.Code,
                message = ex.Message,
                details = ex.Details
            }, statusCode: ex.StatusCode);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Concurrency conflict");

            return Results.Json(new
            {
                status = 409,
                code = "CONFLICT",
                message = "The record was modified by another request. Fetch the latest version and retry."
            }, statusCode: 409);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception: {Method} {Path}",
                ctx.HttpContext.Request.Method, ctx.HttpContext.Request.Path);

            var detail = env.IsDevelopment()
                ? ex.ToString()
                : "An unexpected error occurred.";

            return Results.Json(new
            {
                status = 500,
                code = "INTERNAL_ERROR",
                message = detail
            }, statusCode: 500);
        }
    }
}
