using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.Identity.Tests.Helpers;

public static class IdentityDbHelper
{
    public static TestIdentityDbContext CreateDb(ICurrentUser? user = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TestIdentityDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new TestIdentityDbContext(options, user ?? new FakeCurrentUser());
        db.Database.EnsureCreated();
        return db;
    }
}
