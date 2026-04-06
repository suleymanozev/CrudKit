using CrudKit.Core.Enums;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;

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
    public IReadOnlyList<Permission> Permissions => Array.Empty<Permission>();
    public bool IsAuthenticated => false;
    public bool HasRole(string role) => false;
    public bool HasPermission(string entity, string action) => false;
    public bool HasPermission(string entity, string action, PermScope scope) => false;
}
