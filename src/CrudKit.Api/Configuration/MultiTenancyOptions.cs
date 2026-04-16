using CrudKit.Api.Tenancy;

namespace CrudKit.Api.Configuration;

/// <summary>
/// Fluent builder for multi-tenancy configuration.
/// Returned by <see cref="CrudKitApiOptions.UseMultiTenancy()"/> to allow chaining
/// tenant resolution options that only make sense when multi-tenancy is enabled.
/// </summary>
public class MultiTenancyOptions
{
    private readonly CrudKitApiOptions _parent;

    internal MultiTenancyOptions(CrudKitApiOptions parent) => _parent = parent;

    /// <summary>Resolve tenant from HTTP header (e.g. "X-Tenant-Id").</summary>
    public MultiTenancyOptions ResolveTenantFromHeader(string headerName)
    {
        _parent.TenantResolver = ctx => ctx.Request.Headers[headerName].FirstOrDefault();
        return this;
    }

    /// <summary>Resolve tenant from JWT claim (e.g. "tenant_id").</summary>
    public MultiTenancyOptions ResolveTenantFromClaim(string claimType)
    {
        _parent.TenantResolver = ctx => ctx.User?.FindFirst(claimType)?.Value;
        return this;
    }

    /// <summary>Resolve tenant from first subdomain (e.g. "acme.app.com" → "acme").</summary>
    public MultiTenancyOptions ResolveTenantFromSubdomain()
    {
        _parent.TenantResolver = ctx =>
        {
            var host = ctx.Request.Host.Host;
            var parts = host.Split('.');
            return parts.Length >= 3 ? parts[0] : null;
        };
        return this;
    }

    /// <summary>Resolve tenant from route parameter (e.g. "{tenantId}" in route template).</summary>
    public MultiTenancyOptions ResolveTenantFromRoute(string routeParameterName)
    {
        _parent.TenantResolver = ctx => ctx.Request.RouteValues.TryGetValue(routeParameterName, out var val) ? val?.ToString() : null;
        return this;
    }

    /// <summary>Resolve tenant from query string parameter (e.g. "?tenant=acme").</summary>
    public MultiTenancyOptions ResolveTenantFromQuery(string queryParameterName)
    {
        _parent.TenantResolver = ctx => ctx.Request.Query[queryParameterName].FirstOrDefault();
        return this;
    }

    /// <summary>
    /// When enabled, requests where the tenant cannot be resolved will receive a 400 response
    /// with code TENANT_REQUIRED instead of proceeding with a null tenant context.
    /// </summary>
    public MultiTenancyOptions RejectUnresolvedTenant()
    {
        _parent.TenantRejectUnresolved = true;
        return this;
    }

    /// <summary>
    /// Configures which roles can access data across tenants.
    /// By default, no cross-tenant access is allowed.
    /// </summary>
    public MultiTenancyOptions CrossTenantPolicy(Action<CrossTenantPolicy> configure)
    {
        var policy = new CrossTenantPolicy();
        configure(policy);
        _parent.CrossTenantPolicyInstance = policy;
        return this;
    }
}
