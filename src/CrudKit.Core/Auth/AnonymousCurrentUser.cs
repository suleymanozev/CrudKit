using CrudKit.Core.Enums;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;

namespace CrudKit.Core.Auth;

/// <summary>
/// ICurrentUser DI'a kayıtlı değilse veya token yoksa kullanılır.
/// CrudKit.Api TryAddScoped ile fallback olarak register eder.
/// </summary>
public class AnonymousCurrentUser : ICurrentUser
{
    public string? Id => null;
    public string? Username => null;
    public string? TenantId => null;
    public IReadOnlyList<string> Roles => Array.Empty<string>();
    public IReadOnlyList<Permission> Permissions => Array.Empty<Permission>();
    public bool IsAuthenticated => false;
    public bool HasRole(string role) => false;
    public bool HasPermission(string entity, string action) => false;
    public bool HasPermission(string entity, string action, PermScope scope) => false;
}
