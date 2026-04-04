using CrudKit.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Filters;

/// <summary>
/// Endpoint filter that requires the user to be authenticated.
/// Returns 401 if the user is not authenticated.
/// </summary>
public class RequireAuthFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var currentUser = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

        if (!currentUser.IsAuthenticated)
            return Results.Json(new { status = 401, code = "UNAUTHORIZED", message = "Authentication required." }, statusCode: 401);

        return await next(ctx);
    }
}
