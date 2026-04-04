using CrudKit.Api.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CrudKit.Api.Extensions;

/// <summary>
/// Convenience extension methods for adding auth filters to route groups.
/// </summary>
public static class RouteGroupExtensions
{
    /// <summary>
    /// Adds <see cref="RequireAuthFilter"/> to all endpoints in the group.
    /// Returns 401 if the user is not authenticated.
    /// </summary>
    public static RouteGroupBuilder RequireAuth(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter<RequireAuthFilter>();
        return group;
    }

    /// <summary>
    /// Adds <see cref="RequireRoleFilter"/> to all endpoints in the group.
    /// Returns 403 if the user does not have the specified role.
    /// </summary>
    public static RouteGroupBuilder RequireRole(this RouteGroupBuilder group, string role)
    {
        group.AddEndpointFilter(new RequireRoleFilter(role));
        return group;
    }

    /// <summary>
    /// Adds <see cref="RequirePermissionFilter"/> to all endpoints in the group.
    /// Returns 403 if the user does not have the specified permission.
    /// </summary>
    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder group, string entity, string action)
    {
        group.AddEndpointFilter(new RequirePermissionFilter(entity, action));
        return group;
    }
}
