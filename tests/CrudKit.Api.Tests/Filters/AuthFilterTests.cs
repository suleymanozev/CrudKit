using System.Net;
using System.Text.Json;
using CrudKit.Api.Filters;
using CrudKit.Core.Auth;
using CrudKit.Api.Tests.Helpers;
using Xunit;

namespace CrudKit.Api.Tests.Filters;

public class AuthFilterTests
{
    private static string? GetString(JsonDocument doc, string property)
        => doc.RootElement.TryGetProperty(property, out var el) ? el.GetString() : null;

    private static int GetInt(JsonDocument doc, string property)
        => doc.RootElement.TryGetProperty(property, out var el) ? el.GetInt32() : 0;

    [Fact]
    public async Task RequireAuth_AnonymousUser_Returns401()
    {
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new AnonymousCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok())
                   .AddEndpointFilter<RequireAuthFilter>();
            });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(401, GetInt(doc, "status"));
        Assert.Equal("UNAUTHORIZED", GetString(doc, "code"));
    }

    [Fact]
    public async Task RequireAuth_AuthenticatedUser_Returns200()
    {
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new FakeCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok())
                   .AddEndpointFilter<RequireAuthFilter>();
            });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RequireRole_WrongRole_Returns403()
    {
        // FakeCurrentUser has role "admin" only; requesting "superuser" should fail
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new FakeCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok())
                   .AddEndpointFilter(new RequireRoleFilter("superuser"));
            });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(403, GetInt(doc, "status"));
        Assert.Equal("FORBIDDEN", GetString(doc, "code"));
    }

    [Fact]
    public async Task RequireRole_CorrectRole_Returns200()
    {
        // FakeCurrentUser has role "admin"
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new FakeCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok())
                   .AddEndpointFilter(new RequireRoleFilter("admin"));
            });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RequirePermission_AnonymousUser_Returns403()
    {
        // AnonymousCurrentUser returns false for all HasPermission calls
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new AnonymousCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok())
                   .AddEndpointFilter(new RequirePermissionFilter("Order", "read"));
            });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(403, GetInt(doc, "status"));
        Assert.Equal("FORBIDDEN", GetString(doc, "code"));
    }

    [Fact]
    public async Task RequirePermission_FakeUser_Returns200()
    {
        // FakeCurrentUser returns true for all HasPermission calls
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new FakeCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok())
                   .AddEndpointFilter(new RequirePermissionFilter("Order", "read"));
            });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
