using CrudKit.Core.Enums;
using CrudKit.Core.Models;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Mevcut oturumdaki kullanıcı bilgisi.
/// Bu interface'i uygulama tarafı implemente eder (JWT, OAuth, Cookie, custom, vb.).
/// CrudKit sadece bu interface üzerinden kullanıcı bilgisine erişir.
/// </summary>
public interface ICurrentUser
{
    string? Id { get; }
    string? Username { get; }
    string? TenantId { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<Permission> Permissions { get; }
    bool IsAuthenticated { get; }
    bool HasRole(string role);
    bool HasPermission(string entity, string action);
    bool HasPermission(string entity, string action, PermScope scope);
}
