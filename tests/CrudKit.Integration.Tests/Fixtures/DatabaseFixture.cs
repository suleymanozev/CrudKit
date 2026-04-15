using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Tenancy;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Integration.Tests.Fixtures;

/// <summary>
/// Base fixture for creating test databases. Subclassed per provider.
/// </summary>
public abstract class DatabaseFixture : IAsyncDisposable
{
    public abstract string ProviderName { get; }

    public abstract Task InitializeAsync();
    public abstract ValueTask DisposeAsync();

    protected abstract DbContextOptions<IntegrationDbContext> CreateDbOptions();

    // Shared filter instance so EfRepo.Restore can toggle the same filter the DbContext uses
    private DataFilter<ISoftDeletable>? _softDeleteFilter;

    public IntegrationDbContext CreateContext(string? tenantId = null, ICurrentUser? user = null)
    {
        _softDeleteFilter = new DataFilter<ISoftDeletable>();
        var tenantFilter = new DataFilter<IMultiTenant>();
        var tenantContext = new TenantContext();
        if (tenantId is not null)
            tenantContext.TenantId = tenantId;

        var deps = new CrudKitDbContextDependencies
        {
            CurrentUser = user ?? new FakeCurrentUser(),
            EfOptions = new CrudKitEfOptions { AuditTrailEnabled = true, EnumAsStringEnabled = true },
            TenantContext = tenantContext,
            SoftDeleteFilter = _softDeleteFilter,
            TenantFilter = tenantFilter,
        };

        var options = CreateDbOptions();
        var ctx = new IntegrationDbContext(options, deps);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    public EfRepo<T> CreateRepo<T>(IntegrationDbContext db) where T : class, IEntity
    {
        var dialect = DialectDetector.Detect(db);
        var filterApplier = new FilterApplier(dialect);
        var queryBuilder = new QueryBuilder<T>(filterApplier);

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<CrudKitDbContext>(db);
        services.AddSingleton<ICrudKitDbContext>(db);
        services.AddSingleton<IDataFilter<ISoftDeletable>>(_softDeleteFilter!);
        services.AddSingleton(typeof(IDataFilter<>), typeof(DataFilter<>));
        var sp = services.BuildServiceProvider();

        return new EfRepo<T>(sp, queryBuilder, filterApplier);
    }
}

/// <summary>SQLite in-memory — always available, no Docker needed.</summary>
public class SqliteFixture : DatabaseFixture
{
    private SqliteConnection? _connection;

    public override string ProviderName => "SQLite";

    public override Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _connection?.Dispose();
        return ValueTask.CompletedTask;
    }

    protected override DbContextOptions<IntegrationDbContext> CreateDbOptions()
    {
        return new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseSqlite(_connection!)
            .Options;
    }
}

/// <summary>PostgreSQL using shared container — each fixture gets a unique database.</summary>
public class PostgreSqlFixture : DatabaseFixture
{
    private readonly string _baseConnectionString;
    private readonly string _dbName;

    public PostgreSqlFixture(string baseConnectionString)
    {
        _baseConnectionString = baseConnectionString;
        _dbName = $"crudkit_test_{Guid.NewGuid():N}";
    }

    public override string ProviderName => "PostgreSQL";

    public override async Task InitializeAsync()
    {
        // Create a unique database for this test
        await using var conn = new Npgsql.NpgsqlConnection(_baseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{_dbName}\"";
        await cmd.ExecuteNonQueryAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        // Drop the test database
        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(_baseConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* best effort cleanup */ }
    }

    protected override DbContextOptions<IntegrationDbContext> CreateDbOptions()
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(_baseConnectionString)
        {
            Database = _dbName
        };
        return new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseNpgsql(builder.ConnectionString)
            .Options;
    }
}
