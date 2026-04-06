using CrudKit.Api.Configuration;
using CrudKit.Api.Extensions;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.Api.Tests.Extensions;

public class CrudKitAppExtensionsTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public CrudKitAppExtensionsTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    // Builds a minimal WebApplicationBuilder with ApiTestDbContext over the shared in-memory connection.
    private WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<ApiTestDbContext>((_, opts) => opts.UseSqlite(_connection));
        return builder;
    }

    [Fact]
    public void AddCrudKit_RegistersIRepo()
    {
        var builder = CreateBuilder();
        builder.Services.AddCrudKit<ApiTestDbContext>();

        using var app = builder.Build();
        using var scope = app.Services.CreateScope();

        var repo = scope.ServiceProvider.GetService(typeof(IRepo<ProductEntity>));
        Assert.NotNull(repo);
    }

    [Fact]
    public void AddCrudKit_RegistersCrudKitApiOptions_WithDefaults()
    {
        var builder = CreateBuilder();
        builder.Services.AddCrudKit<ApiTestDbContext>();

        using var app = builder.Build();
        var opts = app.Services.GetRequiredService<CrudKitApiOptions>();

        Assert.Equal(20, opts.DefaultPageSize);
        Assert.Equal(100, opts.MaxPageSize);
        Assert.Equal("/api", opts.ApiPrefix);
        Assert.Equal(10_000, opts.BulkLimit);
        Assert.False(opts.EnableIdempotency);
    }

    [Fact]
    public void AddCrudKit_CustomOptions_AreApplied()
    {
        var builder = CreateBuilder();
        builder.Services.AddCrudKit<ApiTestDbContext>(o =>
        {
            o.DefaultPageSize = 50;
            o.MaxPageSize = 200;
            o.ApiPrefix = "/v2";
            o.BulkLimit = 500;
            o.EnableIdempotency = true;
        });

        using var app = builder.Build();
        var opts = app.Services.GetRequiredService<CrudKitApiOptions>();

        Assert.Equal(50, opts.DefaultPageSize);
        Assert.Equal(200, opts.MaxPageSize);
        Assert.Equal("/v2", opts.ApiPrefix);
        Assert.Equal(500, opts.BulkLimit);
        Assert.True(opts.EnableIdempotency);
    }

    [Fact]
    public void AddCrudKit_FallsBackToAnonymousCurrentUser_WhenNotRegistered()
    {
        var builder = CreateBuilder();
        builder.Services.AddCrudKit<ApiTestDbContext>();

        using var app = builder.Build();
        using var scope = app.Services.CreateScope();

        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
        Assert.IsType<AnonymousCurrentUser>(currentUser);
    }

    [Fact]
    public void AddCrudKitModule_RegistersModule()
    {
        var builder = CreateBuilder();
        builder.Services.AddCrudKit<ApiTestDbContext>();
        builder.Services.AddCrudKitModule<TestModule>();

        using var app = builder.Build();
        var modules = app.Services.GetServices<IModule>().ToList();

        Assert.Contains(modules, m => m is TestModule);
    }

    [Fact]
    public void AddCrudKit_IsIdempotent()
    {
        var builder = CreateBuilder();

        // Calling AddCrudKit twice should not throw.
        builder.Services.AddCrudKit<ApiTestDbContext>();
        builder.Services.AddCrudKit<ApiTestDbContext>();

        // Build should succeed without exception.
        using var app = builder.Build();
        Assert.NotNull(app);
    }

    [Fact]
    public async Task UseCrudKit_CallsModuleMapEndpoints()
    {
        var builder = CreateBuilder();
        builder.Services.AddCrudKit<ApiTestDbContext>();

        var wasCalled = false;
        builder.Services.AddSingleton<IModule>(new CallbackModule(_ => wasCalled = true));

        var app = builder.Build();
        app.UseCrudKit();

        Assert.True(wasCalled);
        await app.DisposeAsync();
    }

    // ---- AuditTrailOptions ----

    [Fact]
    public void UseAuditTrail_SetsAuditTrailEnabled()
    {
        var opts = new CrudKitApiOptions();
        opts.UseAuditTrail();
        Assert.True(opts.AuditTrailEnabled);
    }

    [Fact]
    public void EnableAuditFailedOperations_ReturnsCrudKitApiOptions_AndAuditTrailEnabled()
    {
        var opts = new CrudKitApiOptions();
        // EnableAuditFailedOperations returns the parent CrudKitApiOptions (fluent chain)
        var result = opts.UseAuditTrail().EnableAuditFailedOperations();
        Assert.IsType<CrudKitApiOptions>(result);
        Assert.True(opts.AuditTrailEnabled);
    }

    [Fact]
    public void EnableAuditFailedOperations_PropagatesFlag_ToCrudKitEfOptions()
    {
        var builder = CreateBuilder();
        builder.Services.AddCrudKit<ApiTestDbContext>(o =>
            o.UseAuditTrail().EnableAuditFailedOperations());

        using var app = builder.Build();
        var efOptions = app.Services.GetRequiredService<CrudKit.EntityFrameworkCore.CrudKitEfOptions>();

        Assert.True(efOptions.AuditTrailEnabled);
        Assert.True(efOptions.AuditFailedOperations);
    }

    // --- Test helpers ---

    private class TestModule : IModule
    {
        public string Name => "Test";
        public void RegisterServices(IServiceCollection services, IConfiguration config) { }
        public void MapEndpoints(WebApplication app) { }
    }

    private class CallbackModule : IModule
    {
        private readonly Action<WebApplication> _onMapEndpoints;
        public CallbackModule(Action<WebApplication> onMapEndpoints) => _onMapEndpoints = onMapEndpoints;
        public string Name => "Callback";
        public void RegisterServices(IServiceCollection services, IConfiguration config) { }
        public void MapEndpoints(WebApplication app) => _onMapEndpoints(app);
    }
}
