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
/// Creates test database instances based on DB_PROVIDER environment variable.
/// Default: sqlite. Options: postgresql, sqlserver.
/// </summary>
public class DatabaseFixture : IDisposable
{
    private static readonly string Provider =
        Environment.GetEnvironmentVariable("DB_PROVIDER")?.ToLowerInvariant() ?? "sqlite";

    private SqliteConnection? _sqliteConnection;
    private string? _dbName;

    // Shared filter instance so EfRepo.Restore can toggle the same filter the DbContext uses
    private DataFilter<ISoftDeletable>? _softDeleteFilter;

    public IntegrationDbContext CreateContext(
        string? tenantId = null,
        ICurrentUser? user = null)
    {
        var efOptions = new CrudKitEfOptions
        {
            AuditTrailEnabled = true,
            EnumAsStringEnabled = true,
        };

        _softDeleteFilter = new DataFilter<ISoftDeletable>();
        var tenantFilter = new DataFilter<IMultiTenant>();
        var tenantContext = new TenantContext();
        if (tenantId is not null)
            tenantContext.TenantId = tenantId;

        var deps = new CrudKitDbContextDependencies
        {
            CurrentUser = user ?? new FakeCurrentUser(),
            EfOptions = efOptions,
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
        var sp = BuildServiceProvider(db);
        return new EfRepo<T>(sp, queryBuilder, filterApplier);
    }

    private DbContextOptions<IntegrationDbContext> CreateDbOptions()
    {
        var builder = new DbContextOptionsBuilder<IntegrationDbContext>();

        switch (Provider)
        {
            case "postgresql":
                _dbName = $"crudkit_test_{Guid.NewGuid():N}";
                builder.UseNpgsql($"Host=localhost;Port=5432;Database={_dbName};Username=postgres;Password=postgres");
                break;
            case "sqlserver":
                _dbName = $"crudkit_test_{Guid.NewGuid():N}";
                builder.UseSqlServer($"Server=localhost;Database={_dbName};User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True");
                break;
            default: // sqlite
                _sqliteConnection = new SqliteConnection("Data Source=:memory:");
                _sqliteConnection.Open();
                builder.UseSqlite(_sqliteConnection);
                break;
        }

        return builder.Options;
    }

    private IServiceProvider BuildServiceProvider(IntegrationDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<CrudKitDbContext>(db);
        services.AddSingleton<ICrudKitDbContext>(db);
        services.AddSingleton<IDataFilter<ISoftDeletable>>(_softDeleteFilter!);
        services.AddSingleton(typeof(IDataFilter<>), typeof(DataFilter<>));
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _sqliteConnection?.Dispose();

        // Drop test database for PostgreSQL
        if (_dbName is not null && Provider is "postgresql")
        {
            using var conn = new Npgsql.NpgsqlConnection("Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)";
            cmd.ExecuteNonQuery();
        }
    }
}
