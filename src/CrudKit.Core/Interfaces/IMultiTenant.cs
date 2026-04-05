namespace CrudKit.Core.Interfaces;

/// <summary>For entities that carry a TenantId in multi-tenant applications.</summary>
public interface IMultiTenant
{
    string TenantId { get; set; }
}

/// <summary>Multi-tenant entity with a navigation property to the tenant.</summary>
public interface IMultiTenant<TTenant> : IMultiTenant
    where TTenant : class
{
    TTenant? Tenant { get; set; }
}

/// <summary>Multi-tenant entity with a typed tenant key and navigation property.</summary>
public interface IMultiTenant<TTenant, TTenantKey> : IMultiTenant
    where TTenant : class
    where TTenantKey : notnull
{
    new TTenantKey TenantId { get; set; }
    TTenant? Tenant { get; set; }
}
