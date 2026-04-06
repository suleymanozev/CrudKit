using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Tenancy;

/// <summary>
/// Default mutable ITenantContext. Resolvers set TenantId per-request.
/// Registered as Scoped in DI.
/// </summary>
public class TenantContext : ITenantContext
{
    public string? TenantId { get; set; }
}
