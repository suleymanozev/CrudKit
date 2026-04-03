namespace CrudKit.Core.Interfaces;

/// <summary>Multi-tenant uygulamalarda TenantId taşıyan entity'ler için.</summary>
public interface IMultiTenant
{
    string TenantId { get; set; }
}
