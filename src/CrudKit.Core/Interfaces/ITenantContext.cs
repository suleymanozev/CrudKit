namespace CrudKit.Core.Interfaces;

/// <summary>
/// Provides the current tenant ID for multi-tenant isolation.
/// Resolved per-request by a tenant resolver middleware.
/// Separate from ICurrentUser — a request can be tenant-scoped without authentication.
/// </summary>
public interface ITenantContext
{
    string? TenantId { get; }
}
