using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Tenancy;
using CrudKit.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Factory for creating isolated SQLite in-memory test database instances.
/// Each call produces a fresh database. The TestDbContext takes ownership
/// of the SqliteConnection and disposes it when the context is disposed.
/// </summary>
public static class DbHelper
{
    public static TestDbContext CreateDb(ICurrentUser? user = null, TimeProvider? timeProvider = null,
        bool auditTrailEnabled = false, bool enumAsStringEnabled = false,
        ITenantContext? tenantContext = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .Options;

        CrudKitEfOptions? efOptions = null;
        if (auditTrailEnabled || enumAsStringEnabled)
        {
            efOptions = new CrudKitEfOptions
            {
                AuditTrailEnabled = auditTrailEnabled,
                EnumAsStringEnabled = enumAsStringEnabled,
            };
        }

        var db = new TestDbContext(options, user ?? new FakeCurrentUser(), connection, timeProvider, efOptions, tenantContext);
        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>
    /// Build a minimal IServiceProvider that resolves the given CrudKitDbContext.
    /// Used by tests that construct EfRepo manually.
    /// </summary>
    public static IServiceProvider WrapAsServiceProvider(CrudKitDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<CrudKitDbContext>(db);
        return services.BuildServiceProvider();
    }
}
