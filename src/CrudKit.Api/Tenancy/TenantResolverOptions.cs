using Microsoft.AspNetCore.Http;

namespace CrudKit.Api.Tenancy;

public class TenantResolverOptions
{
    public Func<HttpContext, string?>? Resolver { get; set; }

    /// <summary>
    /// When true, requests where the tenant cannot be resolved will receive a 400 response.
    /// </summary>
    public bool RejectUnresolved { get; set; }

    /// <summary>
    /// Cross-tenant access policy. null = no cross-tenant access configured.
    /// </summary>
    public CrossTenantPolicy? Policy { get; set; }
}
