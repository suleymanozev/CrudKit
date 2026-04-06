namespace CrudKit.Core.Interfaces;

/// <summary>
/// Represents the currently authenticated user.
/// Implemented by the application layer (JWT, OAuth, Cookie, custom, etc.).
/// CrudKit accesses user information exclusively through this interface.
/// </summary>
public interface ICurrentUser
{
    string? Id { get; }
    string? Username { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsAuthenticated { get; }
    bool HasRole(string role);
    bool HasPermission(string entity, string action);

    /// <summary>
    /// Tenant IDs this user can access. null = all tenants (superadmin).
    /// Empty list = no cross-tenant access. Only relevant when cross-tenant policy allows the user's role.
    /// </summary>
    IReadOnlyList<string>? AccessibleTenants { get; }
}
