using System.Diagnostics;
using CrudKit.Core.Models;
using CrudKit.Integration.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace CrudKit.Integration.Tests.Performance;

public class BulkOperationPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public BulkOperationPerformanceTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task BulkDelete_1000Records_CompletesAndSoftDeletes(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<TenantAwareItem>(db);

        // Seed 1000 records
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            db.TenantAwareItems.Add(new TenantAwareItem
            {
                Name = $"Item-{i}",
                TenantId = tenantId
            });
        }
        await db.SaveChangesAsync();
        var createTime = sw.ElapsedMilliseconds;
        _output.WriteLine($"[{provider}] Create 1000 records: {createTime}ms");

        // Bulk delete all — should soft-delete since TenantAwareItem is FullAuditableAggregateRoot
        sw.Restart();
        var deleted = await repo.BulkDelete(
            new Dictionary<string, FilterOp>(),
            CancellationToken.None);
        var deleteTime = sw.ElapsedMilliseconds;

        _output.WriteLine($"[{provider}] Bulk delete {deleted} records: {deleteTime}ms");
        _output.WriteLine($"[{provider}] Per record: {(double)deleteTime / deleted:F2}ms");

        Assert.Equal(1000, deleted);

        // Verify all soft-deleted (Count uses global query filter, so 0 visible)
        var remaining = await repo.Count(CancellationToken.None);
        Assert.Equal(0, remaining);
    }

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task BulkUpdate_1000Records_CompletesAndUpdatesTimestamps(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<TenantAwareItem>(db);

        // Seed 1000 records
        for (int i = 0; i < 1000; i++)
        {
            db.TenantAwareItems.Add(new TenantAwareItem
            {
                Name = $"Item-{i}",
                TenantId = tenantId
            });
        }
        await db.SaveChangesAsync();

        // Capture original UpdatedAt
        var beforeUpdate = DateTime.UtcNow;

        // Bulk update all names
        var sw = Stopwatch.StartNew();
        var updated = await repo.BulkUpdate(
            new Dictionary<string, FilterOp>(),
            new Dictionary<string, object?> { ["Name"] = "Updated" },
            CancellationToken.None);
        var updateTime = sw.ElapsedMilliseconds;

        _output.WriteLine($"[{provider}] Bulk update {updated} records: {updateTime}ms");
        _output.WriteLine($"[{provider}] Per record: {(double)updateTime / updated:F2}ms");

        Assert.Equal(1000, updated);

        // Verify values were applied — read fresh from DB
        using var verifyDb = fixture.CreateContext(tenantId: tenantId);
        var items = verifyDb.TenantAwareItems.Where(x => x.TenantId == tenantId).ToList();
        Assert.All(items, item =>
        {
            Assert.Equal("Updated", item.Name);
            // UpdatedAt should be set by ProcessBeforeSave (hook-aware)
            Assert.True(item.UpdatedAt >= beforeUpdate, "UpdatedAt should be refreshed by SaveChanges");
        });
    }
}
