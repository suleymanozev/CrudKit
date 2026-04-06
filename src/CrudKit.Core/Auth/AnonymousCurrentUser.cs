using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Auth;

/// <summary>
/// Used when no ICurrentUser is registered in DI or when the request has no auth token.
/// CrudKit.Api registers this as a TryAddScoped fallback.
/// </summary>
public class AnonymousCurrentUser : ICurrentUser
{
    public string? Id => null;
    public string? Username => null;
    public IReadOnlyList<string> Roles => Array.Empty<string>();
    public bool IsAuthenticated => false;
    public bool HasRole(string role) => false;
    public bool HasPermission(string entity, string action) => false;

    /// <summary>
    /// Empty list = no cross-tenant access for anonymous users.
    /// </summary>
    public IReadOnlyList<string>? AccessibleTenants => new List<string>();
}
