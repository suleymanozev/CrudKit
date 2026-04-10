using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Auth;

/// <summary>
/// ICurrentUser implementation for tests and development.
/// Grants all permissions. Used by DatabaseFixture in integration tests.
/// </summary>
public class FakeCurrentUser : ICurrentUser
{
    public string? Id { get; set; } = "dev-user-1";
    public string? Username { get; set; } = "developer";
    public IReadOnlyList<string> Roles { get; set; } = ["admin"];
    public bool IsAuthenticated => true;
    public bool HasRole(string role) => Roles.Contains(role);
    public bool HasPermission(string entity, string action) => true;

    /// <summary>
    /// null = all tenants accessible (superadmin-level, for testing).
    /// </summary>
    public IReadOnlyList<string>? AccessibleTenants { get; set; }
}
