using CrudKit.Api.Configuration;
using CrudKit.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Filters;

/// <summary>
/// Determines the type of CRUD operation being performed.
/// </summary>
internal enum CrudOperation { Read, Create, Update, Delete, Restore, Transition, Export, Import }

/// <summary>
/// Endpoint filter that inspects the HTTP method and path to determine which CRUD operation
/// is being performed, then applies the matching authorization rule from
/// <see cref="EndpointAuthorizationBuilder"/>.
/// </summary>
public class CrudAuthorizationFilter : IEndpointFilter
{
    private readonly EndpointAuthorizationBuilder _auth;
    private readonly string _route;

    public CrudAuthorizationFilter(EndpointAuthorizationBuilder auth, string route)
    {
        _auth = auth;
        _route = route;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var method = ctx.HttpContext.Request.Method;
        var path = ctx.HttpContext.Request.Path.Value ?? "";
        var operation = DetermineOperation(method, path);

        // Per-operation auth takes priority
        var opAuth = GetOperationAuth(operation);
        if (opAuth is not null && opAuth.HasAuth)
        {
            var result = CheckOperationAuth(ctx.HttpContext, opAuth);
            if (result is not null) return result;
        }

        // Global role applies to all operations
        if (_auth.GlobalRole is not null)
        {
            var user = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
            if (!user.IsAuthenticated)
                return Unauthorized();
            if (!user.HasRole(_auth.GlobalRole))
                return Forbidden($"Role '{_auth.GlobalRole}' is required.");
        }

        // Convention permissions: {route}:{action}
        if (_auth.UseConventionPermissions)
        {
            var user = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
            if (!user.IsAuthenticated)
                return Unauthorized();
            var action = OperationToAction(operation);
            if (!user.HasPermission(_route, action))
                return Forbidden($"Permission '{_route}:{action}' is required.");
        }

        return await next(ctx);
    }

    private static IResult? CheckOperationAuth(HttpContext httpCtx, OperationAuth opAuth)
    {
        var user = httpCtx.RequestServices.GetRequiredService<ICurrentUser>();

        if (!user.IsAuthenticated)
            return Unauthorized();

        if (opAuth.Role is not null && !user.HasRole(opAuth.Role))
            return Forbidden($"Role '{opAuth.Role}' is required.");

        if (opAuth.Permission is not null &&
            !user.HasPermission(opAuth.Permission.Value.Entity, opAuth.Permission.Value.Action))
            return Forbidden($"Permission '{opAuth.Permission.Value.Entity}:{opAuth.Permission.Value.Action}' is required.");

        return null;
    }

    private OperationAuth? GetOperationAuth(CrudOperation operation)
    {
        return operation switch
        {
            CrudOperation.Read => _auth.Read,
            CrudOperation.Create => _auth.Create,
            CrudOperation.Update => _auth.Update,
            CrudOperation.Delete => _auth.Delete,
            CrudOperation.Restore => _auth.Restore,
            CrudOperation.Transition => _auth.Transition,
            CrudOperation.Export => _auth.Export,
            CrudOperation.Import => _auth.Import,
            _ => null
        };
    }

    internal static CrudOperation DetermineOperation(string method, string path)
    {
        // Check specific path patterns first (order matters)
        if (path.Contains("/restore", StringComparison.OrdinalIgnoreCase))
            return CrudOperation.Restore;
        if (path.Contains("/transition/", StringComparison.OrdinalIgnoreCase))
            return CrudOperation.Transition;
        if (path.Contains("/export", StringComparison.OrdinalIgnoreCase))
            return CrudOperation.Export;
        if (path.Contains("/import", StringComparison.OrdinalIgnoreCase))
            return CrudOperation.Import;
        if (path.Contains("/bulk-delete", StringComparison.OrdinalIgnoreCase))
            return CrudOperation.Delete;
        if (path.Contains("/bulk-update", StringComparison.OrdinalIgnoreCase))
            return CrudOperation.Update;

        // Fall back to HTTP method
        return method.ToUpperInvariant() switch
        {
            "GET" => CrudOperation.Read,
            "POST" => CrudOperation.Create,
            "PUT" => CrudOperation.Update,
            "DELETE" => CrudOperation.Delete,
            _ => CrudOperation.Read
        };
    }

    private static string OperationToAction(CrudOperation operation)
    {
        return operation switch
        {
            CrudOperation.Read => "read",
            CrudOperation.Create => "create",
            CrudOperation.Update => "update",
            CrudOperation.Delete => "delete",
            CrudOperation.Restore => "restore",
            CrudOperation.Transition => "transition",
            CrudOperation.Export => "export",
            CrudOperation.Import => "import",
            _ => "read"
        };
    }

    private static IResult Unauthorized() =>
        Results.Json(new { status = 401, code = "UNAUTHORIZED", message = "Authentication required." }, statusCode: 401);

    private static IResult Forbidden(string message) =>
        Results.Json(new { status = 403, code = "FORBIDDEN", message }, statusCode: 403);
}
