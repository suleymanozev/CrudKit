using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Factory for creating isolated SQLite in-memory test database instances.
/// Each call produces a fresh database. The SqliteConnection must be kept open
/// for the in-memory database to persist.
/// </summary>
public static class DbHelper
{
    public static TestDbContext CreateDb(ICurrentUser? user = null)
    {
        // Use a new named connection for each test to ensure isolation.
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new TestDbContext(options, user ?? new FakeCurrentUser());
        db.Database.EnsureCreated();
        return db;
    }
}
