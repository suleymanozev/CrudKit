using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Factory for creating isolated SQLite in-memory test database instances.
/// Each call produces a fresh database. The TestDbContext takes ownership
/// of the SqliteConnection and disposes it when the context is disposed.
/// </summary>
public static class DbHelper
{
    public static TestDbContext CreateDb(ICurrentUser? user = null, TimeProvider? timeProvider = null,
        bool auditTrailEnabled = false)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .Options;

        var efOptions = auditTrailEnabled ? new CrudKitEfOptions { AuditTrailEnabled = true } : null;
        var db = new TestDbContext(options, user ?? new FakeCurrentUser(), connection, timeProvider, efOptions);
        db.Database.EnsureCreated();
        return db;
    }
}
