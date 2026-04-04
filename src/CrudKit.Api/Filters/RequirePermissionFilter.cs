using CrudKit.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Filters;

/// <summary>
/// Endpoint filter that requires the user to have a specific permission on an entity.
/// Returns 403 if the user does not have the required permission.
/// </summary>
public class RequirePermissionFilter : IEndpointFilter
{
    private readonly string _entity;
    private readonly string _action;

    public RequirePermissionFilter(string entity, string action)
    {
        _entity = entity;
        _action = action;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var currentUser = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

        if (!currentUser.HasPermission(_entity, _action))
            return Results.Json(new { status = 403, code = "FORBIDDEN", message = $"Permission '{_action}' on '{_entity}' is required." }, statusCode: 403);

        return await next(ctx);
    }
}
