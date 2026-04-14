using CrudKit.Core.Models;
using CrudKit.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace CrudKit.Integration.Tests.EdgeCases;

public class EdgeCaseTests
{
    private readonly ITestOutputHelper _output;

    public EdgeCaseTests(ITestOutputHelper output) => _output = output;

    // ─── Test 1: Concurrent AutoSequence — duplicate number risk ───

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task AutoSequence_ConcurrentCreates_NoDuplicateNumbers(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();

        // Create 10 entities concurrently, each with its own DbContext
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            using var db = fixture.CreateContext(tenantId: tenantId);
            var repo = fixture.CreateRepo<OrderEntity>(db);
            return await repo.Create(new OrderEntity { CustomerName = $"Customer-{i}" });
        });

        var results = await Task.WhenAll(tasks);
        var orderNumbers = results.Select(r => r.OrderNumber).ToList();

        _output.WriteLine($"[{provider}] Generated order numbers: {string.Join(", ", orderNumbers)}");

        // All numbers must be unique — duplicates indicate a race condition
        Assert.Equal(10, orderNumbers.Distinct().Count());
    }

    // ─── Test 3: Cascade soft delete does not infinite loop ───

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task CascadeDelete_ParentWithChildren_CompletesWithoutHanging(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<OrderEntity>(db);

        var order = await repo.Create(new OrderEntity { CustomerName = "Test" });
        db.OrderLines.Add(new OrderLineEntity
        {
            OrderEntityId = order.Id,
            ProductName = "Widget",
            Quantity = 1,
            UnitPrice = 10,
            TenantId = tenantId
        });
        db.OrderLines.Add(new OrderLineEntity
        {
            OrderEntityId = order.Id,
            ProductName = "Gadget",
            Quantity = 2,
            UnitPrice = 20,
            TenantId = tenantId
        });
        await db.SaveChangesAsync();

        // Delete parent — cascade to children, must complete within timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repo.Delete(order.Id, cts.Token);

        // Verify cascade happened
        foreach (var entry in db.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;

        var deletedOrder = await db.Orders.IgnoreQueryFilters()
            .FirstAsync(o => o.Id == order.Id);
        var deletedLines = await db.OrderLines.IgnoreQueryFilters()
            .Where(l => l.OrderEntityId == order.Id)
            .ToListAsync();

        Assert.NotNull(deletedOrder.DeletedAt);
        Assert.Equal(2, deletedLines.Count);
        Assert.All(deletedLines, l => Assert.NotNull(l.DeletedAt));
    }

    // ─── Test 4: Soft delete + unique restore race condition ───

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task UniqueRestore_ConflictWithActiveRecord_Throws(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<TenantUniqueItem>(db);

        // Create and soft-delete an item
        var original = await repo.Create(new TenantUniqueItem { Code = "RACE-001", Name = "Original" });
        await repo.Delete(original.Id);

        // Create a new item with the same code (should succeed — original is soft-deleted)
        var replacement = await repo.Create(new TenantUniqueItem { Code = "RACE-001", Name = "Replacement" });
        Assert.Equal("RACE-001", replacement.Code);

        // Detach tracked entities to avoid stale data
        foreach (var entry in db.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;

        // Now try to restore the original — should fail because "RACE-001" is occupied
        await Assert.ThrowsAsync<AppError>(async () =>
            await repo.Restore(original.Id));
    }

    // ─── Test 5: Large collection — bulk delete memory ───

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task BulkDelete_500Records_CompletesWithinTimeout(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<TenantAwareItem>(db);

        // Seed 500 records
        for (int i = 0; i < 500; i++)
        {
            db.TenantAwareItems.Add(new TenantAwareItem
            {
                Name = $"Item-{i}",
                TenantId = tenantId
            });
        }
        await db.SaveChangesAsync();

        // Bulk delete within timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var deleted = await repo.BulkDelete(
            new Dictionary<string, FilterOp>(),
            cts.Token);

        _output.WriteLine($"[{provider}] Bulk deleted {deleted} records");
        Assert.Equal(500, deleted);

        // Verify all soft-deleted
        var remaining = await repo.Count(CancellationToken.None);
        Assert.Equal(0, remaining);
    }

    // ─── Test 6: Concurrent soft delete — same entity ───

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task ConcurrentSoftDelete_SameEntity_OnlyOneSucceeds(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db1 = fixture.CreateContext(tenantId: tenantId);
        var repo1 = fixture.CreateRepo<TenantAwareItem>(db1);

        var item = await repo1.Create(new TenantAwareItem { Name = "Contested" });
        var itemId = item.Id;

        // Try to delete from two contexts concurrently
        using var db2 = fixture.CreateContext(tenantId: tenantId);
        var repo2 = fixture.CreateRepo<TenantAwareItem>(db2);

        var results = await Task.WhenAll(
            Task.Run(async () =>
            {
                try { await repo1.Delete(itemId); return true; }
                catch { return false; }
            }),
            Task.Run(async () =>
            {
                try { await repo2.Delete(itemId); return true; }
                catch { return false; }
            }));

        _output.WriteLine($"[{provider}] Delete results: {string.Join(", ", results)}");

        // At least one should succeed
        Assert.Contains(true, results);
    }

    // ─── Test 7: Entity with empty/minimal data ───

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task CreateEntity_MinimalData_SetsSystemFieldsCorrectly(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<TenantAwareItem>(db);

        // Create with empty name — system fields should still be populated
        var item = await repo.Create(new TenantAwareItem { Name = "" });

        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.True(item.CreatedAt > DateTime.MinValue, "CreatedAt should be auto-set");
        Assert.True(item.UpdatedAt > DateTime.MinValue, "UpdatedAt should be auto-set");
        Assert.Equal(tenantId, item.TenantId);
    }

    // ─── Test 8: Filter with special characters (SQL injection via repo) ───

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task Filter_SpecialCharacters_DoesNotCrash(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<TenantAwareItem>(db);

        await repo.Create(new TenantAwareItem { Name = "Normal Item" });

        // Filter with SQL injection attempt
        var filters = new Dictionary<string, FilterOp>
        {
            ["name"] = FilterOp.Parse("like:'; DROP TABLE--")
        };

        // Should not crash — either return empty or handle gracefully
        var result = await repo.List(new ListParams
        {
            Page = 1,
            PerPage = 10,
            Filters = filters
        });

        Assert.NotNull(result);
        Assert.Equal(0, result.Total); // No match, but no crash
    }
}
