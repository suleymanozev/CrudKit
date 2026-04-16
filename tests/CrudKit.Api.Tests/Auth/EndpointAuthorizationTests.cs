using System.Net;
using System.Text.Json;

using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;

using Xunit;

namespace CrudKit.Api.Tests.Auth;

/// <summary>
/// A current user with configurable roles and permissions.
/// </summary>
internal class ConfigurableCurrentUser : ICurrentUser
{
    public string? Id { get; set; } = "test-user";
    public string? Username { get; set; } = "tester";
    public IReadOnlyList<string> Roles { get; set; } = new List<string>();
    public bool IsAuthenticated => true;
    public IReadOnlyList<string>? AccessibleTenants { get; set; }

    private readonly HashSet<string> _permissionKeys = new();

    public void AddPermission(string entity, string action)
        => _permissionKeys.Add($"{entity}:{action}");

    public bool HasRole(string role) => Roles.Contains(role);
    public bool HasPermission(string entity, string action) => _permissionKeys.Contains($"{entity}:{action}");
}

public class EndpointAuthorizationTests
{
    private static int GetStatus(JsonDocument doc)
        => doc.RootElement.TryGetProperty("status", out var el) ? el.GetInt32() : 0;

    private static string? GetCode(JsonDocument doc)
        => doc.RootElement.TryGetProperty("code", out var el) ? el.GetString() : null;

    [Fact]
    public async Task Authorize_GlobalRole_Blocks403()
    {
        // User has "user" role, endpoint requires "admin"
        var user = new ConfigurableCurrentUser { Roles = new List<string> { "user" } };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products")
                    .Authorize(auth => auth.RequireRole("admin"));
            });

        var response = await app.Client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(403, GetStatus(doc));
        Assert.Equal("FORBIDDEN", GetCode(doc));
    }

    [Fact]
    public async Task Authorize_GlobalRole_Allows200()
    {
        // User has "admin" role, endpoint requires "admin"
        var user = new ConfigurableCurrentUser { Roles = new List<string> { "admin" } };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("auth-products")
                    .Authorize(auth => auth.RequireRole("admin"));
            });

        var response = await app.Client.GetAsync("/api/auth-products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_PerOperation_ReadAllowed_CreateBlocked()
    {
        // User has "user" role; Read requires "user", Create requires "admin"
        var user = new ConfigurableCurrentUser { Roles = new List<string> { "user" } };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("per-op-products")
                    .Authorize(auth =>
                    {
                        auth.Read.RequireRole("user");
                        auth.Create.RequireRole("admin");
                    });
            });

        // GET (Read) should succeed
        var getResponse = await app.Client.GetAsync("/api/per-op-products");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // POST (Create) should be blocked
        var postResponse = await app.Client.PostAsJsonAsync("/api/per-op-products",
            new { Name = "Test", Price = 9.99 });
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
        var doc = JsonDocument.Parse(await postResponse.Content.ReadAsStringAsync());
        Assert.Equal("FORBIDDEN", GetCode(doc));
    }

    [Fact]
    public async Task Authorize_ConventionPermissions_Checked()
    {
        // User without any permissions -> should be blocked
        var user = new ConfigurableCurrentUser { Roles = new List<string>() };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("conv-products")
                    .Authorize(auth => auth.RequirePermissions());
            });

        var response = await app.Client.GetAsync("/api/conv-products");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("conv-products:read", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Authorize_ConventionPermissions_GrantedPermission_Allows200()
    {
        // User with the correct convention permission
        var user = new ConfigurableCurrentUser { Roles = new List<string>() };
        user.AddPermission("perm-products", "read");

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("perm-products")
                    .Authorize(auth => auth.RequirePermissions());
            });

        var response = await app.Client.GetAsync("/api/perm-products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_Anonymous_Returns401()
    {
        // Anonymous user should get 401
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new AnonymousCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("anon-products")
                    .Authorize(auth => auth.RequireRole("admin"));
            });

        var response = await app.Client.GetAsync("/api/anon-products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(401, GetStatus(doc));
        Assert.Equal("UNAUTHORIZED", GetCode(doc));
    }

    [Fact]
    public async Task WithCustomEndpoints_WorksInSameGroup()
    {
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new FakeCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("custom-products")
                    .WithCustomEndpoints(group =>
                    {
                        group.MapGet("/stats", () => Results.Ok(new { total = 42 }));
                    });
            });

        // Custom endpoint should be accessible
        var response = await app.Client.GetAsync("/api/custom-products/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(42, doc.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task WithCustomEndpoints_SharesAppErrorFilter()
    {
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new FakeCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("err-products")
                    .WithCustomEndpoints(group =>
                    {
                        group.MapGet("/fail", IResult () =>
                        {
                            throw CrudKit.Core.Models.AppError.BadRequest("Custom error test");
                        });
                    });
            });

        var response = await app.Client.GetAsync("/api/err-products/fail");

        // AppErrorFilter should catch the AppError and return a structured error
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("BAD_REQUEST", GetCode(doc));
    }
}
