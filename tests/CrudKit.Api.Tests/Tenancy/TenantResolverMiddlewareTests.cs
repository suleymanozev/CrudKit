using System.Net;
using System.Text.Json;
using CrudKit.Api.Tests.Helpers;
using Xunit;

namespace CrudKit.Api.Tests.Tenancy;

public class TenantResolverMiddlewareTests
{
    private static string? GetString(JsonDocument doc, string property)
        => doc.RootElement.TryGetProperty(property, out var el) ? el.GetString() : null;

    private static int GetInt(JsonDocument doc, string property)
        => doc.RootElement.TryGetProperty(property, out var el) ? el.GetInt32() : 0;

    [Fact]
    public async Task Middleware_RejectsNullTenant_WhenRejectUnresolvedEnabled()
    {
        // Arrange: resolve from header but do not send the header → tenant is null
        await using var app = await TestWebApp.CreateAsync(
            configureOptions: opts =>
            {
                opts.UseMultiTenancy()
                    .ResolveTenantFromHeader("X-Tenant-Id")
                    .RejectUnresolvedTenant();
            },
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok());
            });

        // Act: send request without the header
        var response = await app.Client.GetAsync("/test");

        // Assert: 400 TENANT_REQUIRED
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, GetInt(doc, "status"));
        Assert.Equal("TENANT_REQUIRED", GetString(doc, "code"));
    }

    [Fact]
    public async Task Middleware_AllowsNullTenant_WhenRejectUnresolvedDisabled()
    {
        // Arrange: resolve from header but do NOT call RejectUnresolvedTenant()
        await using var app = await TestWebApp.CreateAsync(
            configureOptions: opts =>
            {
                opts.UseMultiTenancy()
                    .ResolveTenantFromHeader("X-Tenant-Id");
                // RejectUnresolvedTenant() is intentionally not called
            },
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok());
            });

        // Act: send request without the header — tenant remains null
        var response = await app.Client.GetAsync("/test");

        // Assert: request passes through (200 OK)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Middleware_AllowsRequest_WhenTenantHeaderIsPresent()
    {
        // Arrange: resolve from header with reject enabled
        await using var app = await TestWebApp.CreateAsync(
            configureOptions: opts =>
            {
                opts.UseMultiTenancy()
                    .ResolveTenantFromHeader("X-Tenant-Id")
                    .RejectUnresolvedTenant();
            },
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok());
            });

        // Act: send request WITH the header
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Add("X-Tenant-Id", "tenant-abc");
        var response = await app.Client.SendAsync(request);

        // Assert: request passes through (200 OK)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
