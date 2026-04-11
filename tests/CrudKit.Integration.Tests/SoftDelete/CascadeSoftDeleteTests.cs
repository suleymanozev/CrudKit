using CrudKit.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.Integration.Tests.SoftDelete;

public class CascadeSoftDeleteTests
{
    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task CascadeDelete_SetsDeleteBatchIdOnChildren(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var orderRepo = fixture.CreateRepo<OrderEntity>(db);

        // Create order
        var order = await orderRepo.Create(new OrderEntity { CustomerName = "Test" });

        // Create lines
        db.OrderLines.Add(new OrderLineEntity { OrderEntityId = order.Id, ProductName = "Widget", Quantity = 5, UnitPrice = 10, TenantId = tenantId });
        db.OrderLines.Add(new OrderLineEntity { OrderEntityId = order.Id, ProductName = "Gadget", Quantity = 3, UnitPrice = 20, TenantId = tenantId });
        await db.SaveChangesAsync();

        // Delete order (cascade to lines)
        await orderRepo.Delete(order.Id);

        // Detach all tracked entities so we get fresh data from DB (cascade runs via raw SQL)
        foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;

        // Verify lines have same DeleteBatchId as order
        var deletedOrder = await db.Orders.IgnoreQueryFilters()
            .FirstAsync(o => o.Id == order.Id);
        var deletedLines = await db.OrderLines.IgnoreQueryFilters()
            .Where(l => l.OrderEntityId == order.Id)
            .ToListAsync();

        Assert.NotNull(deletedOrder.DeleteBatchId);
        Assert.Equal(2, deletedLines.Count);
        Assert.All(deletedLines, l => Assert.Equal(deletedOrder.DeleteBatchId, l.DeleteBatchId));
    }

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task CascadeRestore_OnlyRestoresBatchDeletedChildren(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var orderRepo = fixture.CreateRepo<OrderEntity>(db);
        var lineRepo = fixture.CreateRepo<OrderLineEntity>(db);

        var order = await orderRepo.Create(new OrderEntity { CustomerName = "Test" });

        var line1 = new OrderLineEntity { OrderEntityId = order.Id, ProductName = "Keep", Quantity = 1, UnitPrice = 10, TenantId = tenantId };
        var line2 = new OrderLineEntity { OrderEntityId = order.Id, ProductName = "Remove", Quantity = 1, UnitPrice = 20, TenantId = tenantId };
        db.OrderLines.AddRange(line1, line2);
        await db.SaveChangesAsync();

        // Delete line2 individually first
        await lineRepo.Delete(line2.Id);

        // Now delete order (cascade only affects line1)
        foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
        await orderRepo.Delete(order.Id);

        // Restore order
        foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
        await orderRepo.Restore(order.Id);

        // Detach again to read fresh data after cascade restore (raw SQL)
        foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;

        // line1 should be restored (same batch), line2 should stay deleted (different batch)
        var restoredLine = await db.OrderLines.IgnoreQueryFilters()
            .FirstAsync(l => l.Id == line1.Id);
        var stillDeletedLine = await db.OrderLines.IgnoreQueryFilters()
            .FirstAsync(l => l.Id == line2.Id);

        Assert.Null(restoredLine.DeletedAt);
        Assert.NotNull(stillDeletedLine.DeletedAt);
    }
}
