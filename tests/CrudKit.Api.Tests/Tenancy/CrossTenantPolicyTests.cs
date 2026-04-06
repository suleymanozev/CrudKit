using System.Net;
using System.Text.Json;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Auth;
using Xunit;

namespace CrudKit.Api.Tests.Tenancy;

public class CrossTenantPolicyTests
{
    private static string? GetString(JsonDocument doc, string property)
        => doc.RootElement.TryGetProperty(property, out var el) ? el.GetString() : null;

    [Fact]
    public async Task SuperAdmin_AccessibleTenantsNull_CanAccessAnyTenant()
    {
        // Arrange: user with null AccessibleTenants (superadmin)
        var user = new FakeCurrentUser { AccessibleTenants = null, Roles = new List<string> { "admin" } };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureOptions: opts =>
            {
                opts.UseMultiTenancy()
                    .ResolveTenantFromHeader("X-Tenant-Id")
                    .CrossTenantPolicy(p => p.Allow("admin"));
            },
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok());
            });

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Add("X-Tenant-Id", "any-tenant");
        var response = await app.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Support_CanAccessListedTenant()
    {
        // Arrange: user with AccessibleTenants containing "acme"
        var user = new FakeCurrentUser
        {
            AccessibleTenants = new List<string> { "acme" },
            Roles = new List<string> { "support" }
        };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureOptions: opts =>
            {
                opts.UseMultiTenancy()
                    .ResolveTenantFromHeader("X-Tenant-Id")
                    .CrossTenantPolicy(p => p.Allow("support"));
            },
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok());
            });

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Add("X-Tenant-Id", "acme");
        var response = await app.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Support_DeniedUnlistedTenant()
    {
        // Arrange: user with AccessibleTenants = ["acme"], trying to access "globex"
        var user = new FakeCurrentUser
        {
            AccessibleTenants = new List<string> { "acme" },
            Roles = new List<string> { "user" } // no cross-tenant role
        };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureOptions: opts =>
            {
                opts.UseMultiTenancy()
                    .ResolveTenantFromHeader("X-Tenant-Id")
                    .CrossTenantPolicy(p => p.Allow("support"));
            },
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok());
            });

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Add("X-Tenant-Id", "globex");
        var response = await app.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("TENANT_ACCESS_DENIED", GetString(doc, "code"));
    }

    [Fact]
    public async Task Support_ReadOnly_CanGet()
    {
        // Arrange: user with ReadOnly cross-tenant access
        var user = new FakeCurrentUser
        {
            AccessibleTenants = new List<string> { "acme" },
            Roles = new List<string> { "support" }
        };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureOptions: opts =>
            {
                opts.UseMultiTenancy()
                    .ResolveTenantFromHeader("X-Tenant-Id")
                    .CrossTenantPolicy(p => p.AllowReadOnly("support"));
            },
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok());
            });

        // Act: GET request
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Add("X-Tenant-Id", "acme");
        var response = await app.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Support_ReadOnly_CannotPost()
    {
        // Arrange: user with ReadOnly cross-tenant access
        var user = new FakeCurrentUser
        {
            AccessibleTenants = new List<string> { "acme" },
            Roles = new List<string> { "support" }
        };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureOptions: opts =>
            {
                opts.UseMultiTenancy()
                    .ResolveTenantFromHeader("X-Tenant-Id")
                    .CrossTenantPolicy(p => p.AllowReadOnly("support"));
            },
            configureEndpoints: web =>
            {
                web.MapPost("/test", () => Results.Ok());
            });

        // Act: POST request
        var request = new HttpRequestMessage(HttpMethod.Post, "/test");
        request.Headers.Add("X-Tenant-Id", "acme");
        var response = await app.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CROSS_TENANT_READ_ONLY", GetString(doc, "code"));
    }

    [Fact]
    public async Task NoCrossTenantPolicy_UserWithAccessibleTenants_StillWorks()
    {
        // Arrange: no CrossTenantPolicy configured, user has AccessibleTenants
        var user = new FakeCurrentUser
        {
            AccessibleTenants = new List<string> { "acme" },
            Roles = new List<string> { "user" }
        };

        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureOptions: opts =>
            {
                opts.UseMultiTenancy()
                    .ResolveTenantFromHeader("X-Tenant-Id");
                // No CrossTenantPolicy configured
            },
            configureEndpoints: web =>
            {
                web.MapGet("/test", () => Results.Ok());
            });

        // Act: tenant in list, but no policy → middleware skips cross-tenant check
        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Add("X-Tenant-Id", "acme");
        var response = await app.Client.SendAsync(request);

        // Assert: passes through since no policy is configured
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
