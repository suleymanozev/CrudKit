using CrudKit.Core.Tenancy;
using Microsoft.AspNetCore.Http;

namespace CrudKit.Api.Tenancy;

/// <summary>
/// Middleware that resolves tenant ID from the configured source and sets ITenantContext.
/// </summary>
public class TenantResolverMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TenantResolverOptions _options;

    public TenantResolverMiddleware(RequestDelegate next, TenantResolverOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        tenantContext.TenantId = _options.Resolver?.Invoke(context);

        if (tenantContext.TenantId == null && _options.RejectUnresolved)
        {
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                status = 400,
                code = "TENANT_REQUIRED",
                message = "Tenant could not be resolved from the request."
            });
            return;
        }

        await _next(context);
    }
}
