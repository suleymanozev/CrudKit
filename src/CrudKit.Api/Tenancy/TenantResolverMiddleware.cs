using CrudKit.Core.Interfaces;
using CrudKit.Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Tenancy;

/// <summary>
/// Middleware that resolves tenant ID from the configured source and sets ITenantContext.
/// Also enforces cross-tenant access control when a CrossTenantPolicy is configured.
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

        // Reject unresolved tenant if configured
        if (tenantContext.TenantId is null && _options.RejectUnresolved)
        {
            await WriteTenantError(context, 400, "TENANT_REQUIRED",
                "Tenant could not be resolved from the request.");
            return;
        }

        // Cross-tenant access validation
        if (tenantContext.TenantId is not null && _options.Policy is not null)
        {
            var currentUser = context.RequestServices.GetService<ICurrentUser>();
            if (currentUser is not null && currentUser.IsAuthenticated)
            {
                var accessibleTenants = currentUser.AccessibleTenants;

                // accessibleTenants == null means all tenants (superadmin-level)
                if (accessibleTenants is not null)
                {
                    if (!accessibleTenants.Contains(tenantContext.TenantId))
                    {
                        // User is trying to access a tenant not in their list.
                        // Check if their role has a cross-tenant policy rule.
                        var hasPolicy = _options.Policy.Rules
                            .Any(r => currentUser.HasRole(r.Role));

                        if (!hasPolicy)
                        {
                            await WriteTenantError(context, 403, "TENANT_ACCESS_DENIED",
                                $"You do not have access to tenant '{tenantContext.TenantId}'.");
                            return;
                        }
                    }

                    // Check read-only enforcement for cross-tenant roles
                    var matchingRule = _options.Policy.Rules
                        .FirstOrDefault(r => currentUser.HasRole(r.Role));

                    if (matchingRule?.AccessLevel == CrossTenantAccessLevel.ReadOnly)
                    {
                        var method = context.Request.Method;
                        if (method is "POST" or "PUT" or "DELETE" or "PATCH")
                        {
                            await WriteTenantError(context, 403, "CROSS_TENANT_READ_ONLY",
                                "Your cross-tenant access is read-only.");
                            return;
                        }
                    }
                }
            }
        }

        await _next(context);
    }

    private static async Task WriteTenantError(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { status = statusCode, code, message });
    }
}
