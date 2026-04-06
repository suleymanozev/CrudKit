using CrudKit.Core.Enums;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;

namespace CrudKit.Core.Auth;

/// <summary>
/// ICurrentUser implementation for tests and development.
/// Grants all permissions. Used by DatabaseFixture in integration tests.
/// </summary>
public class FakeCurrentUser : ICurrentUser
{
    public string? Id { get; set; } = "dev-user-1";
    public string? Username { get; set; } = "developer";
    public IReadOnlyList<string> Roles { get; set; } = new List<string> { "admin" };
    public IReadOnlyList<Permission> Permissions { get; set; } = new List<Permission>();
    public bool IsAuthenticated => true;
    public bool HasRole(string role) => Roles.Contains(role);
    public bool HasPermission(string entity, string action) => true;
    public bool HasPermission(string entity, string action, PermScope scope) => true;
}
