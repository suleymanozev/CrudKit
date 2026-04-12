using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Attributes;
using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.Api.Tests.Security;

// Test entity — multi-tenant + soft-deletable
[CrudEntity]
public class SecureItem : IAuditableEntity, ISoftDeletable, IMultiTenant
{
    public Guid Id { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    public string Secret { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeleteBatchId { get; set; }
    public string TenantId { get; set; } = "";
}

// Current user scoped to a specific tenant
public class TenantUser : ICurrentUser
{
    private readonly string _tenantId;
    public TenantUser(string tenantId) => _tenantId = tenantId;
    public string? Id => "user-1";
    public string? Username => "testuser";
    public IReadOnlyList<string> Roles => [];
    public bool IsAuthenticated => true;
    public bool HasRole(string role) => false;
    public bool HasPermission(string entity, string action) => true;
    public IReadOnlyList<string>? AccessibleTenants => [_tenantId];
}

public class SecurityAuditTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Helper: create app with multi-tenancy enabled for a specific tenant
    private static Task<TestWebApp> CreateTenantApp(string tenantId)
    {
        return TestWebApp.CreateAsync(
            currentUser: new TenantUser(tenantId),
            configureEndpoints: web =>
                web.MapCrudEndpoints<SecureItem>("secure-items"),
            configureOptions: opts =>
            {
                opts.UseMultiTenancy()
                    .ResolveTenantFromHeader("X-Tenant-Id");
            });
    }

    // ─── 1. TENANT ISOLATION ───

    [Fact]
    public async Task TenantIsolation_CannotReadOtherTenantData()
    {
        // Create with tenant-A
        await using var app = await CreateTenantApp("tenant-a");

        var createResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
        {
            Content = JsonContent.Create(new { Name = "Secret Doc" }),
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        // Try to read with tenant-B header — EF query filter should exclude it
        var getResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/secure-items/{id}")
        {
            Headers = { { "X-Tenant-Id", "tenant-b" } }
        });

        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task TenantIsolation_ListOnlyReturnsTenantData()
    {
        await using var app = await CreateTenantApp("tenant-a");

        // Create item for tenant-A
        var createResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
        {
            Content = JsonContent.Create(new { Name = "Tenant A Item" }),
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        // List with tenant-B — should be empty
        var listResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/secure-items")
        {
            Headers = { { "X-Tenant-Id", "tenant-b" } }
        });
        var listJson = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
        Assert.Equal(0, listJson.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task TenantIsolation_CannotUpdateOtherTenantData()
    {
        await using var app = await CreateTenantApp("tenant-a");

        var createResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
        {
            Content = JsonContent.Create(new { Name = "Original" }),
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var id = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        // Try to update with tenant-B
        var updateResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Put, $"/api/secure-items/{id}")
        {
            Content = JsonContent.Create(new { Name = "Hacked" }),
            Headers = { { "X-Tenant-Id", "tenant-b" } }
        });

        Assert.Equal(HttpStatusCode.NotFound, updateResp.StatusCode);
    }

    [Fact]
    public async Task TenantIsolation_CannotDeleteOtherTenantData()
    {
        await using var app = await CreateTenantApp("tenant-a");

        var createResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
        {
            Content = JsonContent.Create(new { Name = "Protected" }),
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var id = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        var deleteResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/secure-items/{id}")
        {
            Headers = { { "X-Tenant-Id", "tenant-b" } }
        });

        Assert.Equal(HttpStatusCode.NotFound, deleteResp.StatusCode);
    }

    // ─── 2. MASS ASSIGNMENT ───

    [Fact]
    public async Task MassAssignment_CannotSetTenantId()
    {
        await using var app = await CreateTenantApp("tenant-a");

        // Try to POST with a different TenantId in body
        var resp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
        {
            Content = JsonContent.Create(new { Name = "Test", TenantId = "tenant-hacker" }),
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        // Verify TenantId was set from header, not body — read back with tenant-a should succeed
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetString()!;

        var getResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/secure-items/{id}")
        {
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        // If TenantId was set to "tenant-hacker", this GET with tenant-a would return 404
    }

    [Fact]
    public async Task MassAssignment_CannotSetDeletedAt()
    {
        await using var app = await CreateTenantApp("tenant-a");

        var resp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
        {
            Content = JsonContent.Create(new { Name = "Test", DeletedAt = "2026-01-01T00:00:00Z" }),
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        // Item should NOT be soft-deleted — retrievable via GET
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetString()!;

        var getResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/secure-items/{id}")
        {
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
    }

    [Fact]
    public async Task MassAssignment_CannotSetId()
    {
        await using var app = await CreateTenantApp("tenant-a");
        var fakeId = Guid.NewGuid();

        var resp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
        {
            Content = JsonContent.Create(new { Name = "Test", Id = fakeId }),
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var actualId = json.RootElement.GetProperty("id").GetString()!;

        // Id should be auto-generated, not the fake one
        Assert.NotEqual(fakeId.ToString(), actualId);
    }

    [Fact]
    public async Task MassAssignment_CannotSetCreatedAt()
    {
        await using var app = await CreateTenantApp("tenant-a");

        var resp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
        {
            Content = JsonContent.Create(new { Name = "Test", CreatedAt = "2020-01-01T00:00:00Z" }),
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var createdAt = json.RootElement.GetProperty("createdAt").GetDateTime();

        // CreatedAt should be now, not 2020
        Assert.True(createdAt > DateTime.UtcNow.AddMinutes(-1));
    }

    // ─── 3. SOFT DELETE BYPASS ───

    [Fact]
    public async Task SoftDeleteBypass_DeletedItemNotAccessible()
    {
        await using var app = await CreateTenantApp("tenant-a");

        var createResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
        {
            Content = JsonContent.Create(new { Name = "To Delete" }),
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var id = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        // Delete
        var deleteResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/secure-items/{id}")
        {
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });
        Assert.True(deleteResp.IsSuccessStatusCode);

        // GET should return 404
        var getResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/secure-items/{id}")
        {
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);

        // List should not include deleted item
        var listResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/secure-items")
        {
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });
        var listJson = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
        Assert.Equal(0, listJson.RootElement.GetProperty("total").GetInt32());
    }

    // ─── 4. QUERY INJECTION ───

    [Fact]
    public async Task QueryInjection_FilterDoesNotCorruptData()
    {
        await using var app = await CreateTenantApp("tenant-a");

        // Create an item first
        var createResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
        {
            Content = JsonContent.Create(new { Name = "Safe" }),
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        // Try SQL injection via filter — EF parameterized queries prevent execution
        await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            "/api/secure-items?name=like:'; DROP TABLE secure_items;--")
        {
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        // Verify data is still intact after the injection attempt
        var listResp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/secure-items")
        {
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listJson = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
        Assert.Equal(1, listJson.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task QueryInjection_SortDoesNotCrash()
    {
        await using var app = await CreateTenantApp("tenant-a");

        var resp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            "/api/secure-items?sort=; DROP TABLE--")
        {
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        // Should not crash with 500 — invalid sort field is handled gracefully
        Assert.NotEqual(HttpStatusCode.InternalServerError, resp.StatusCode);
    }

    // ─── 5. HIDDEN SYSTEM FIELDS ───

    [Fact]
    public async Task HiddenFields_TenantIdNotInResponse()
    {
        await using var app = await CreateTenantApp("tenant-a");

        var resp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
        {
            Content = JsonContent.Create(new { Name = "Test" }),
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("tenantId", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HiddenFields_DeleteBatchIdNotInResponse()
    {
        await using var app = await CreateTenantApp("tenant-a");

        var resp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
        {
            Content = JsonContent.Create(new { Name = "Test" }),
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("deleteBatchId", body, StringComparison.OrdinalIgnoreCase);
    }

    // ─── 6. PAGINATION ABUSE ───

    [Fact]
    public async Task PaginationAbuse_PerPageCappedAtMaxPageSize()
    {
        await using var app = await CreateTenantApp("tenant-a");

        // Create a few items
        for (int i = 0; i < 5; i++)
        {
            await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
            {
                Content = JsonContent.Create(new { Name = $"Item {i}" }),
                Headers = { { "X-Tenant-Id", "tenant-a" } }
            });
        }

        // Request absurdly large page
        var resp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            "/api/secure-items?per_page=999999")
        {
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var perPage = json.RootElement.GetProperty("perPage").GetInt32();

        // perPage should be capped at MaxPageSize (default 100), not 999999
        Assert.True(perPage <= 100, $"perPage was {perPage} — should be capped at MaxPageSize");
    }

    [Fact]
    public async Task PaginationAbuse_NegativePageNumberHandled()
    {
        await using var app = await CreateTenantApp("tenant-a");

        var resp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            "/api/secure-items?page=-1&per_page=-5")
        {
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        // Should not crash — return 200 with sane defaults
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task PaginationAbuse_ZeroPerPageHandled()
    {
        await using var app = await CreateTenantApp("tenant-a");

        var resp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            "/api/secure-items?per_page=0")
        {
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var perPage = json.RootElement.GetProperty("perPage").GetInt32();
        Assert.True(perPage > 0, "perPage should default to a positive number");
    }

    // ─── 7. AUTH BYPASS ───

    // Unauthenticated user
    private sealed class AnonymousUser : ICurrentUser
    {
        public string? Id => null;
        public string? Username => null;
        public IReadOnlyList<string> Roles => [];
        public bool IsAuthenticated => false;
        public bool HasRole(string role) => false;
        public bool HasPermission(string entity, string action) => false;
        public IReadOnlyList<string>? AccessibleTenants => null;
    }

    // User without admin role
    private sealed class RegularUser : ICurrentUser
    {
        public string? Id => "user-regular";
        public string? Username => "regular";
        public IReadOnlyList<string> Roles => ["user"];
        public bool IsAuthenticated => true;
        public bool HasRole(string role) => role == "user";
        public bool HasPermission(string entity, string action) => false;
        public IReadOnlyList<string>? AccessibleTenants => ["tenant-a"];
    }

    [Fact]
    public async Task AuthBypass_UnauthenticatedCannotAccessProtectedEntity()
    {
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new AnonymousUser(),
            configureEndpoints: web =>
                web.MapCrudEndpoints<AdminEntity>("admin-items"));

        var resp = await app.Client.GetAsync("/api/admin-items");
        // Should be 401 or 403
        Assert.True(resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 401/403 but got {resp.StatusCode}");
    }

    [Fact]
    public async Task AuthBypass_WrongRoleCannotAccessRoleProtectedEntity()
    {
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new RegularUser(),
            configureEndpoints: web =>
                web.MapCrudEndpoints<AdminEntity>("admin-items"));

        var resp = await app.Client.GetAsync("/api/admin-items");
        Assert.True(resp.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 403 but got {resp.StatusCode}");
    }

    [Fact]
    public async Task AuthBypass_WrongRoleCannotCreateOnProtectedEntity()
    {
        await using var app = await TestWebApp.CreateAsync(
            currentUser: new RegularUser(),
            configureEndpoints: web =>
                web.MapCrudEndpoints<AdminEntity>("admin-items"));

        var resp = await app.Client.PostAsJsonAsync("/api/admin-items", new { Name = "Hacked" });
        Assert.True(resp.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 403 but got {resp.StatusCode}");
    }

    // ─── 8. ERROR INFORMATION LEAKAGE ───

    [Fact]
    public async Task ErrorLeakage_InternalErrorDoesNotExposeStackTrace()
    {
        await using var app = await CreateTenantApp("tenant-a");

        // Request a non-existent entity — triggers controlled 404
        var resp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            $"/api/secure-items/{Guid.NewGuid()}")
        {
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("StackTrace", body);
        Assert.DoesNotContain("at CrudKit", body);
        Assert.DoesNotContain("NpgsqlConnection", body);
        Assert.DoesNotContain("ConnectionString", body);
    }

    [Fact]
    public async Task ErrorLeakage_ValidationErrorDoesNotExposeInternals()
    {
        await using var app = await CreateTenantApp("tenant-a");

        // Send invalid data — empty required field
        var resp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/secure-items")
        {
            Content = JsonContent.Create(new { Name = "" }), // Required field empty
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        var body = await resp.Content.ReadAsStringAsync();

        // Should contain structured error, not internals
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("StackTrace", body);
    }

    [Fact]
    public async Task ErrorLeakage_InvalidGuidDoesNotExposeInternals()
    {
        await using var app = await CreateTenantApp("tenant-a");

        var resp = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            "/api/secure-items/not-a-guid")
        {
            Headers = { { "X-Tenant-Id", "tenant-a" } }
        });

        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("FormatException", body);
        Assert.DoesNotContain("StackTrace", body);
    }
}
