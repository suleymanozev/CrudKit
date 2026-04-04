using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Tests.Helpers;

public sealed class TestWebApp : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly SqliteConnection _connection;

    public HttpClient Client { get; }

    private TestWebApp(WebApplication app, SqliteConnection connection)
    {
        _app = app;
        _connection = connection;
        Client = app.GetTestClient();
    }

    public static async Task<TestWebApp> CreateAsync(
        ICurrentUser? currentUser = null,
        Action<WebApplication>? configureEndpoints = null,
        Action<IServiceCollection>? configureServices = null,
        string environment = "Development")
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Environment.EnvironmentName = environment;

        builder.Services.AddDbContext<ApiTestDbContext>((_, opts) => opts.UseSqlite(connection));
        builder.Services.AddScoped<ICurrentUser>(_ => currentUser ?? new FakeCurrentUser());
        builder.Services.AddCrudKitEf<ApiTestDbContext>();

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        configureEndpoints?.Invoke(app);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiTestDbContext>();
            db.Database.EnsureCreated();
        }

        await app.StartAsync();
        return new TestWebApp(app, connection);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _connection.Dispose();
    }
}
