using System.Text.Json;
using CrudKit.Api.Models;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Filters;

/// <summary>
/// Endpoint filter that provides idempotency guarantees for mutating HTTP methods.
///
/// Behaviour:
/// - GET requests are always passed through unchanged.
/// - If the <c>Idempotency-Key</c> header is absent, the request is processed normally.
/// - Otherwise a compound cache key of <c>{userId}:{idempotency-key}</c> is formed.
///   If a non-expired record exists in the database for that key + tenant, the original
///   response is returned immediately with the header <c>X-Idempotency-Replayed: true</c>.
/// - If no record exists the request is processed, and the response is persisted so that
///   subsequent retries can be served from cache.
///
/// Consumers must register <see cref="IdempotencyRecord"/> in their DbContext.
/// </summary>
public class IdempotencyFilter : IEndpointFilter
{
    /// <summary>Default time-to-live for cached records.</summary>
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var httpContext = ctx.HttpContext;

        // Only apply to mutating methods
        if (HttpMethods.IsGet(httpContext.Request.Method) ||
            HttpMethods.IsHead(httpContext.Request.Method) ||
            HttpMethods.IsOptions(httpContext.Request.Method))
        {
            return await next(ctx);
        }

        // No header → proceed normally without caching
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

        // Build compound key so keys are scoped per user
        var compoundKey = $"{userId}:{rawKey}";

        var db = httpContext.RequestServices.GetRequiredService<CrudKitDbContext>();
        var now = DateTime.UtcNow;

        // Check for an existing, non-expired record
        var existing = await db.Set<IdempotencyRecord>()
            .FirstOrDefaultAsync(r =>
                r.Key == compoundKey &&
                r.TenantId == tenantId &&
                r.ExpiresAt > now);

        if (existing is not null)
        {
            // Return the cached response
            httpContext.Response.Headers["X-Idempotency-Replayed"] = "true";

            var body = existing.ResponseBody is not null
                ? JsonSerializer.Deserialize<object>(existing.ResponseBody)
                : null;

            return Results.Json(body, statusCode: existing.StatusCode);
        }

        // Process the request
        var result = await next(ctx);

        // Persist the response for future retries
        await PersistAsync(db, compoundKey, tenantId, userId, httpContext, result, now);

        return result;
    }

    // ---- Helpers ----

    private static async Task PersistAsync(
        CrudKitDbContext db,
        string compoundKey,
        string? tenantId,
        string? userId,
        HttpContext httpContext,
        object? result,
        DateTime now)
    {
        try
        {
            var (statusCode, body) = ExtractResponse(result);

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

            db.Set<IdempotencyRecord>().Add(record);
            await db.SaveChangesAsync();
        }
        catch
        {
            // Saving idempotency record must never break the original response
        }
    }

    private static (int StatusCode, object? Body) ExtractResponse(object? result)
    {
        // IResult implementations expose the status via IStatusCodeHttpResult
        if (result is IStatusCodeHttpResult statusResult)
        {
            var sc = statusResult.StatusCode ?? 200;

            // IValueHttpResult<T> carries the value payload
            if (result is IValueHttpResult valueResult)
                return (sc, valueResult.Value);

            return (sc, null);
        }

        return (200, result);
    }
}
