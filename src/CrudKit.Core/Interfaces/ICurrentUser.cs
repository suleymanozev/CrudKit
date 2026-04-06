using CrudKit.Core.Enums;
using CrudKit.Core.Models;

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
    IReadOnlyList<Permission> Permissions { get; }
    bool IsAuthenticated { get; }
    bool HasRole(string role);
    bool HasPermission(string entity, string action);
    bool HasPermission(string entity, string action, PermScope scope);
}
