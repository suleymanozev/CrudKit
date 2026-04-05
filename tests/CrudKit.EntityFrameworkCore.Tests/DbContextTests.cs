using CrudKit.Core.Auth;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests;

public class DbContextTests
{
    // ---- Timestamps ----

    [Fact]
    public async Task SaveChanges_SetsCreatedAtAndUpdatedAt_OnAdd()
    {
        using var db = DbHelper.CreateDb();
        var person = new PersonEntity { Name = "Alice" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        Assert.NotEqual(default, person.CreatedAt);
        Assert.NotEqual(default, person.UpdatedAt);
        Assert.NotEqual(Guid.Empty, person.Id);
    }

    [Fact]
    public async Task SaveChanges_SetsUpdatedAt_OnModify_PreservesCreatedAt()
    {
        using var db = DbHelper.CreateDb();
        var person = new PersonEntity { Name = "Alice" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        var createdAt = person.CreatedAt;
        await Task.Delay(5);

        person.Name = "Bob";
        await db.SaveChangesAsync();

        Assert.Equal(createdAt, person.CreatedAt);
        Assert.True(person.UpdatedAt >= createdAt);
    }

    // ---- Soft delete ----

    [Fact]
    public async Task Delete_SoftDeletable_SetsDeletedAt_NotRemovesRow()
    {
        using var db = DbHelper.CreateDb();
        var entity = new SoftPersonEntity { Name = "Alice" };
        db.SoftPersons.Add(entity);
        await db.SaveChangesAsync();

        db.SoftPersons.Remove(entity);
        await db.SaveChangesAsync();

        // Should not appear in normal query (global filter active)
        var found = await db.SoftPersons.FirstOrDefaultAsync(e => e.Id == entity.Id);
        Assert.Null(found);

        // But the row still exists (check via IgnoreQueryFilters)
        var raw = await db.SoftPersons.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);
        Assert.NotNull(raw);
        Assert.NotNull(raw!.DeletedAt);
    }

    [Fact]
    public async Task List_SoftDeletable_ExcludesDeletedRows()
    {
        using var db = DbHelper.CreateDb();
        db.SoftPersons.AddRange(
            new SoftPersonEntity { Name = "Alice" },
            new SoftPersonEntity { Name = "Bob" });
        await db.SaveChangesAsync();

        db.SoftPersons.Remove(db.SoftPersons.First(e => e.Name == "Bob"));
        await db.SaveChangesAsync();

        var list = await db.SoftPersons.ToListAsync();
        Assert.Single(list);
        Assert.Equal("Alice", list[0].Name);
    }

    // ---- Multi-tenant ----

    [Fact]
    public async Task SaveChanges_SetsTenantId_FromCurrentUser()
    {
        var user = new FakeCurrentUser("my-tenant");
        using var db = DbHelper.CreateDb(user);

        var entity = new TenantPersonEntity { Name = "Alice" };
        db.TenantPersons.Add(entity);
        await db.SaveChangesAsync();

        Assert.Equal("my-tenant", entity.TenantId);
    }

    [Fact]
    public async Task List_MultiTenant_FiltersToCurrentTenant()
    {
        // Use shared connection so both DbContexts see same data
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options1 = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection).Options;
        var options2 = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection).Options;

        using var db1 = new TestDbContext(options1, new FakeCurrentUser("tenant-1"));
        db1.Database.EnsureCreated();
        db1.TenantPersons.Add(new TenantPersonEntity { Name = "Alice", TenantId = "tenant-1" });
        await db1.SaveChangesAsync();

        using var db2 = new TestDbContext(options2, new FakeCurrentUser("tenant-2"));
        db2.Database.EnsureCreated(); // tables already exist; makes intent explicit
        db2.TenantPersons.Add(new TenantPersonEntity { Name = "Bob", TenantId = "tenant-2" });
        await db2.SaveChangesAsync();

        // Each context should only see its own tenant's data
        var list1 = await db1.TenantPersons.ToListAsync();
        Assert.Single(list1);
        Assert.Equal("Alice", list1[0].Name);

        var list2 = await db2.TenantPersons.ToListAsync();
        Assert.Single(list2);
        Assert.Equal("Bob", list2[0].Name);
    }

    // ---- Audit log ----

    [Fact]
    public async Task SaveChanges_WritesAuditLog_OnCreate()
    {
        using var db = DbHelper.CreateDb();
        var entity = new AuditPersonEntity { Name = "Alice" };
        db.AuditPersons.Add(entity);
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal("AuditPersonEntity", log!.EntityType);
        Assert.Equal("Create", log.Action);
        Assert.NotNull(log.NewValues);
    }

    [Fact]
    public async Task SaveChanges_WritesAuditLog_OnUpdate_WithChangedFields()
    {
        using var db = DbHelper.CreateDb();
        var entity = new AuditPersonEntity { Name = "Alice" };
        db.AuditPersons.Add(entity);
        await db.SaveChangesAsync();

        entity.Name = "Bob";
        await db.SaveChangesAsync();

        var updateLog = await db.AuditLogs
            .Where(l => l.Action == "Update")
            .FirstOrDefaultAsync();
        Assert.NotNull(updateLog);
        Assert.Contains("Name", updateLog!.ChangedFields ?? "");
    }
}
