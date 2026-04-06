using Microsoft.AspNetCore.Http;

namespace CrudKit.Api.Tenancy;

public class TenantResolverOptions
{
    public Func<HttpContext, string?>? Resolver { get; set; }
}
