using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests;

public class ConcurrencyTests
{
    [Fact]
    public async Task RowVersion_DoesNotIncrementOnCreate()
    {
        using var db = DbHelper.CreateDb();

        var entity = new ConcurrentEntity { Name = "Alice" };
        db.ConcurrentEntities.Add(entity);
        await db.SaveChangesAsync();

        // RowVersion starts at 0 — CrudKitDbContext only increments on Modified
        Assert.Equal(0u, entity.RowVersion);
    }

    [Fact]
    public async Task RowVersion_IncrementsOnUpdate()
    {
        using var db = DbHelper.CreateDb();

        var entity = new ConcurrentEntity { Name = "Alice" };
        db.ConcurrentEntities.Add(entity);
        await db.SaveChangesAsync();

        Assert.Equal(0u, entity.RowVersion);

        entity.Name = "Bob";
        await db.SaveChangesAsync();

        Assert.Equal(1u, entity.RowVersion);
    }

    [Fact]
    public async Task RowVersion_IncrementsOnEachSuccessiveUpdate()
    {
        using var db = DbHelper.CreateDb();

        var entity = new ConcurrentEntity { Name = "v0" };
        db.ConcurrentEntities.Add(entity);
        await db.SaveChangesAsync();

        entity.Name = "v1";
        await db.SaveChangesAsync();
        Assert.Equal(1u, entity.RowVersion);

        entity.Name = "v2";
        await db.SaveChangesAsync();
        Assert.Equal(2u, entity.RowVersion);

        entity.Name = "v3";
        await db.SaveChangesAsync();
        Assert.Equal(3u, entity.RowVersion);
    }

    [Fact]
    public async Task RowVersion_ConcurrentUpdate_ThrowsDbUpdateConcurrencyException()
    {
        // Use a shared SQLite connection so both contexts see the same data
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .Options;

        // Seed data with db1
        using var db1 = new TestDbContext(options, new CrudKit.Core.Auth.FakeCurrentUser());
        db1.Database.EnsureCreated();
        var entity = new ConcurrentEntity { Name = "Original" };
        db1.ConcurrentEntities.Add(entity);
        await db1.SaveChangesAsync();

        var entityId = entity.Id;

        // db2 loads the same row — same RowVersion = 0
        using var db2 = new TestDbContext(options, new CrudKit.Core.Auth.FakeCurrentUser());
        var entityInDb2 = await db2.ConcurrentEntities.FindAsync(entityId);
        Assert.NotNull(entityInDb2);
        Assert.Equal(0u, entityInDb2!.RowVersion);

        // db1 updates first — RowVersion becomes 1 in the DB
        entity.Name = "UpdatedByDb1";
        await db1.SaveChangesAsync();
        Assert.Equal(1u, entity.RowVersion);

        // db2 tries to update with stale RowVersion=0 — must throw
        entityInDb2.Name = "UpdatedByDb2";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => db2.SaveChangesAsync());

        connection.Dispose();
    }
}
