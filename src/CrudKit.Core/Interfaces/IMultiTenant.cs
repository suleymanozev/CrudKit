namespace CrudKit.Core.Interfaces;

/// <summary>For entities that carry a TenantId in multi-tenant applications.</summary>
public interface IMultiTenant
{
    string TenantId { get; set; }
}
