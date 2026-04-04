using CrudKit.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Filters;

/// <summary>
/// Endpoint filter that requires the user to have a specific role.
/// Returns 403 if the user does not have the required role.
/// </summary>
public class RequireRoleFilter : IEndpointFilter
{
    private readonly string _role;

    public RequireRoleFilter(string role)
    {
        _role = role;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var currentUser = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

        if (!currentUser.HasRole(_role))
            return Results.Json(new { status = 403, code = "FORBIDDEN", message = $"Role '{_role}' is required." }, statusCode: 403);

        return await next(ctx);
    }
}
