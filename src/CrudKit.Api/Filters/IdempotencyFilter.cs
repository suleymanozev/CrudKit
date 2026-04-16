using System.Text.Json;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Filters;

/// <summary>
/// Endpoint filter that provides idempotency guarantees for mutating HTTP methods.
/// Uses <see cref="IIdempotencyStore"/> for persistence — no direct DbContext access.
/// </summary>
public class IdempotencyFilter : IEndpointFilter
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var httpContext = ctx.HttpContext;

        if (HttpMethods.IsGet(httpContext.Request.Method) ||
            HttpMethods.IsHead(httpContext.Request.Method) ||
            HttpMethods.IsOptions(httpContext.Request.Method))
        {
            return await next(ctx);
        }

        if (!httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues) ||
            string.IsNullOrWhiteSpace(keyValues))
        {
            return await next(ctx);
        }

        var rawKey = keyValues.ToString();
        var currentUser = httpContext.RequestServices.GetRequiredService<ICurrentUser>();
        var tenantContext = httpContext.RequestServices.GetService<ITenantContext>();
        var userId = currentUser.Id;
        var tenantId = tenantContext?.TenantId;

        var compoundKey = $"{userId}:{rawKey}";

        var store = httpContext.RequestServices.GetRequiredService<IIdempotencyStore>();

        var existing = await store.FindAsync(compoundKey, tenantId);

        if (existing is not null)
        {
            httpContext.Response.Headers["X-Idempotency-Replayed"] = "true";

            var body = existing.ResponseBody is not null
                ? JsonSerializer.Deserialize<object>(existing.ResponseBody)
                : null;

            return Results.Json(body, statusCode: existing.StatusCode);
        }

        var result = await next(ctx);

        await PersistAsync(store, compoundKey, tenantId, userId, httpContext, result);

        return result;
    }

    private static async Task PersistAsync(
        IIdempotencyStore store,
        string compoundKey,
        string? tenantId,
        string? userId,
        HttpContext httpContext,
        object? result)
    {
        try
        {
            var (statusCode, body) = ExtractResponse(result);
            var now = DateTime.UtcNow;

            var record = new IdempotencyRecord
            {
                Id = Guid.NewGuid().ToString(),
                Key = compoundKey,
                Path = httpContext.Request.Path.ToString(),
                Method = httpContext.Request.Method,
                StatusCode = statusCode,
                ResponseBody = body is not null ? JsonSerializer.Serialize(body) : null,
                UserId = userId,
                TenantId = tenantId,
                CreatedAt = now,
                ExpiresAt = now.Add(DefaultTtl)
            };

            await store.SaveAsync(record);
        }
        catch
        {
            // Saving idempotency record must never break the original response
        }
    }

    private static (int StatusCode, object? Body) ExtractResponse(object? result)
    {
        if (result is IStatusCodeHttpResult statusResult)
        {
            var sc = statusResult.StatusCode ?? 200;
            if (result is IValueHttpResult valueResult)
                return (sc, valueResult.Value);
            return (sc, null);
        }
        return (200, result);
    }
}
