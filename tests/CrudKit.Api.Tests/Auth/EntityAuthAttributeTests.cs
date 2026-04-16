using System.Net;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Auth;

using Xunit;

namespace CrudKit.Api.Tests.Auth;

public class EntityAuthAttributeTests
{
    private static int GetStatus(JsonDocument doc)
        => doc.RootElement.TryGetProperty("status", out var el) ? el.GetInt32() : 0;

    private static string? GetCode(JsonDocument doc)
        => doc.RootElement.TryGetProperty("code", out var el) ? el.GetString() : null;

    [Fact]
    public async Task Entity_RequireAuth_Returns401_WhenAnonymous()
    {
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new AnonymousCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<SecuredEntity, CreateSecuredDto, UpdateSecuredDto>("secured-items");
            });

        var response = await app.Client.GetAsync("/api/secured-items");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(401, GetStatus(doc));
        Assert.Equal("UNAUTHORIZED", GetCode(doc));
    }

    [Fact]
    public async Task Entity_RequireAuth_Returns200_WhenAuthenticated()
    {
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new FakeCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<SecuredEntity, CreateSecuredDto, UpdateSecuredDto>("auth-secured");
            });

        var response = await app.Client.GetAsync("/api/auth-secured");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Entity_RequireRole_Returns403_WhenWrongRole()
    {
        // User has "user" role, entity requires "admin"
        var user = new ConfigurableCurrentUser { Roles = new List<string> { "user" } };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<AdminEntity, CreateAdminDto, UpdateAdminDto>("admin-items");
            });

        var response = await app.Client.GetAsync("/api/admin-items");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(403, GetStatus(doc));
        Assert.Equal("FORBIDDEN", GetCode(doc));
    }

    [Fact]
    public async Task Entity_RequireRole_Returns200_WhenCorrectRole()
    {
        var user = new ConfigurableCurrentUser { Roles = new List<string> { "admin" } };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<AdminEntity, CreateAdminDto, UpdateAdminDto>("admin-ok");
            });

        var response = await app.Client.GetAsync("/api/admin-ok");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Entity_RequirePermissions_ChecksConvention()
    {
        // User without permissions -> should be blocked
        var user = new ConfigurableCurrentUser { Roles = new List<string>() };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<PermissionEntity, CreatePermissionDto, UpdatePermissionDto>("perm-items");
            });

        var response = await app.Client.GetAsync("/api/perm-items");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("perm-items:read", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Entity_RequirePermissions_Returns200_WhenGranted()
    {
        var user = new ConfigurableCurrentUser { Roles = new List<string>() };
        user.AddPermission("perm-ok", "read");

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<PermissionEntity, CreatePermissionDto, UpdatePermissionDto>("perm-ok");
            });

        var response = await app.Client.GetAsync("/api/perm-ok");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Entity_AuthorizeOperation_PerOperation()
    {
        // User has "user" role; entity requires "user" for Read, "admin" for Delete
        var user = new ConfigurableCurrentUser { Roles = new List<string> { "user" } };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<OpAuthEntity, CreateOpAuthDto, UpdateOpAuthDto>("op-items");
            });

        // GET (Read) requires "user" role -> should succeed
        var getResponse = await app.Client.GetAsync("/api/op-items");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // DELETE requires "admin" role -> should fail with 403
        var deleteResponse = await app.Client.DeleteAsync($"/api/op-items/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
        var doc = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync());
        Assert.Equal("FORBIDDEN", GetCode(doc));
    }

    [Fact]
    public async Task Fluent_Authorize_StacksWithEntityAuth()
    {
        // Entity has [RequireAuth] (minimum: must be logged in)
        // Fluent adds role requirement for Delete -> delete needs both auth + admin role
        var user = new ConfigurableCurrentUser { Roles = new List<string> { "user" } };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<SecuredEntity, CreateSecuredDto, UpdateSecuredDto>("stack-items")
                    .Authorize(auth => auth.Delete.RequireRole("admin"));
            });

        // GET should succeed (RequireAuth passes, fluent only restricts Delete)
        var getResponse = await app.Client.GetAsync("/api/stack-items");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // DELETE should fail because fluent requires "admin" role but user has "user"
        var deleteResponse = await app.Client.DeleteAsync($"/api/stack-items/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task MapCrudEndpoints_RouteLess_DerivedFromTable()
    {
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new FakeCurrentUser(),
            configureEndpoints: web =>
            {
                // No explicit route — should derive "auto-routed" from [CrudEntity(Resource = "auto-routed")]
                web.MapCrudEndpoints<AutoRoutedEntity, CreateAutoRoutedDto, UpdateAutoRoutedDto>();
            });

        // Resource name is used directly as the route segment
        var response = await app.Client.GetAsync("/api/auto-routed");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MapCrudEndpoints_RouteLess_ReadOnly()
    {
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new FakeCurrentUser(),
            configureEndpoints: web =>
            {
                // Read-only, no route — should derive "auto-routed" from [CrudEntity(Resource = "auto-routed")]
                web.MapCrudEndpoints<AutoRoutedEntity>();
            });

        var response = await app.Client.GetAsync("/api/auto-routed");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
