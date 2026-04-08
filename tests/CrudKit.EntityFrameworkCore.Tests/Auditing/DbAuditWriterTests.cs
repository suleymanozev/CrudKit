using CrudKit.Core.Auth;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Auditing;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Auditing;

public class DbAuditWriterTests
{
    // Build a service provider that resolves TestDbContext as ICrudKitDbContext.
    private static (IServiceProvider Services, TestDbContext Db) BuildServiceProvider()
    {
        var db = DbHelper.CreateDb();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<CrudKitDbContext>(db);
        services.AddSingleton<ICrudKitDbContext>(db);

        return (services.BuildServiceProvider(), db);
    }

    private static DbAuditWriter CreateWriter(IServiceProvider services)
        => new DbAuditWriter(services, new AuditDbContextAccessor());

    [Fact]
    public async Task WriteAsync_EmptyList_DoesNotSaveAnything()
    {
        var (services, db) = BuildServiceProvider();
        var writer = CreateWriter(services);

        await writer.WriteAsync([], CancellationToken.None);

        Assert.Equal(0, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task WriteAsync_SingleEntry_AppearsInAuditLogs()
    {
        var (services, db) = BuildServiceProvider();
        var writer = CreateWriter(services);

        var timestamp = DateTime.UtcNow;
        var entry = new AuditEntry
        {
            EntityType = "TestEntity",
            EntityId = "123",
            Action = "Create",
            UserId = "user-1",
            Timestamp = timestamp,
            NewValues = """{"Name":"Alice"}""",
        };

        await writer.WriteAsync([entry], CancellationToken.None);

        var log = await db.AuditLogs.SingleAsync();
        Assert.Equal("TestEntity", log.EntityType);
        Assert.Equal("123", log.EntityId);
        Assert.Equal("Create", log.Action);
        Assert.Equal("user-1", log.UserId);
        Assert.Equal(timestamp, log.Timestamp);
        Assert.Equal("""{"Name":"Alice"}""", log.NewValues);
    }

    [Fact]
    public async Task WriteAsync_MultipleEntries_AllAppearInAuditLogs()
    {
        var (services, db) = BuildServiceProvider();
        var writer = new DbAuditWriter(services, new AuditDbContextAccessor());

        var entries = new List<AuditEntry>
        {
            new() { EntityType = "Foo", EntityId = "1", Action = "Create", Timestamp = DateTime.UtcNow },
            new() { EntityType = "Bar", EntityId = "2", Action = "Update", Timestamp = DateTime.UtcNow },
            new() { EntityType = "Baz", EntityId = "3", Action = "Delete", Timestamp = DateTime.UtcNow },
        };

        await writer.WriteAsync(entries, CancellationToken.None);

        var count = await db.AuditLogs.CountAsync();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task WriteAsync_ResetsIsAuditSaveFlag_AfterSave()
    {
        var (services, db) = BuildServiceProvider();
        var writer = new DbAuditWriter(services, new AuditDbContextAccessor());

        var entry = new AuditEntry
        {
            EntityType = "TestEntity",
            EntityId = "1",
            Action = "Create",
            Timestamp = DateTime.UtcNow,
        };

        await writer.WriteAsync([entry], CancellationToken.None);

        // IsAuditSave must be false after WriteAsync completes (reset in finally block).
        Assert.False(db.IsAuditSave);
    }

    [Fact]
    public void AuditDbContextAccessor_Default_ResolvesICrudKitDbContext()
    {
        var (services, db) = BuildServiceProvider();
        var accessor = new AuditDbContextAccessor();
        var resolved = accessor.Resolve(services);
        Assert.Same(db, resolved);
    }

    [Fact]
    public void AuditDbContextAccessor_WithType_ResolvesSpecificContext()
    {
        var (services, db) = BuildServiceProvider();
        var accessor = new AuditDbContextAccessor(typeof(TestDbContext));
        // TestDbContext is registered in BuildServiceProvider as itself.
        var resolved = accessor.Resolve(services);
        Assert.Same(db, resolved);
    }

    [Fact]
    public async Task AuditEntries_SameSaveChanges_ShareCorrelationId()
    {
        // Create a database with audit trail enabled.
        var db = DbHelper.CreateDb(auditTrailEnabled: true);
        var currentUser = new FakeCurrentUser { Id = "test-user" };

        // Add two [Audited] entities.
        var entity1 = new AuditPersonEntity { Id = Guid.NewGuid(), Name = "Alice" };
        var entity2 = new AuditPersonEntity { Id = Guid.NewGuid(), Name = "Bob" };

        db.AuditPersons.Add(entity1);
        db.AuditPersons.Add(entity2);

        // Save changes — both audit entries should have the same CorrelationId.
        await db.SaveChangesAsync();

        var auditLogs = db.AuditLogs.ToList();
        Assert.Equal(2, auditLogs.Count);
        Assert.NotNull(auditLogs[0].CorrelationId);
        Assert.Equal(auditLogs[0].CorrelationId, auditLogs[1].CorrelationId);

        // Save again — new CorrelationId should be generated.
        entity1.Name = "Alice Updated";
        await db.SaveChangesAsync();

        var updatedLogs = db.AuditLogs.Where(l => l.Action == "Update").ToList();
        Assert.Single(updatedLogs);
        Assert.NotEqual(auditLogs[0].CorrelationId, updatedLogs[0].CorrelationId);
    }
}
