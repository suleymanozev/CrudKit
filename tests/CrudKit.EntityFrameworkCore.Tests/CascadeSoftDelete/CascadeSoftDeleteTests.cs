using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Repository;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.CascadeSoftDelete;

public class CascadeSoftDeleteTests
{
    private static (TestDbContext db, EfRepo<ParentItemEntity> repo) CreateParentRepo(
        TimeProvider? timeProvider = null)
    {
        var softDeleteFilter = new DataFilter<ISoftDeletable>();
        var db = DbHelper.CreateDb(timeProvider: timeProvider, softDeleteFilter: softDeleteFilter);
        var dialect = DialectDetector.Detect(db);
        var filterApplier = new FilterApplier(dialect);
        var queryBuilder = new QueryBuilder<ParentItemEntity>(filterApplier);
        var repo = new EfRepo<ParentItemEntity>(DbHelper.WrapAsServiceProvider(db, softDeleteFilter: softDeleteFilter), queryBuilder, filterApplier);
        return (db, repo);
    }

    /// <summary>
    /// Detaches all tracked entities so subsequent queries hit the database
    /// instead of returning cached change-tracker data.
    /// </summary>
    private static void DetachAll(TestDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }

    [Fact]
    public async Task SoftDelete_Parent_CascadesToChildren()
    {
        var (db, repo) = CreateParentRepo();

        // Create parent with two children
        var parent = new ParentItemEntity { Name = "Parent1" };
        db.ParentItems.Add(parent);
        await db.SaveChangesAsync();

        var child1 = new ChildItemEntity { Name = "Child1", ParentItemId = parent.Id };
        var child2 = new ChildItemEntity { Name = "Child2", ParentItemId = parent.Id };
        db.ChildItems.AddRange(child1, child2);
        await db.SaveChangesAsync();

        var parentId = parent.Id;

        // Soft-delete the parent
        db.ParentItems.Remove(parent);
        await db.SaveChangesAsync();

        // Detach all tracked entities to force fresh reads from the database
        DetachAll(db);

        // Both children should be soft-deleted
        var children = await db.ChildItems.IgnoreQueryFilters()
            .Where(c => c.ParentItemId == parentId)
            .ToListAsync();

        Assert.Equal(2, children.Count);
        Assert.All(children, c => Assert.NotNull(c.DeletedAt));
    }

    [Fact]
    public async Task SoftDelete_Parent_DoesNotAffectOtherParentsChildren()
    {
        var (db, _) = CreateParentRepo();

        // Create two parents with children each
        var parent1 = new ParentItemEntity { Name = "Parent1" };
        var parent2 = new ParentItemEntity { Name = "Parent2" };
        db.ParentItems.AddRange(parent1, parent2);
        await db.SaveChangesAsync();

        var child1 = new ChildItemEntity { Name = "Child1", ParentItemId = parent1.Id };
        var child2 = new ChildItemEntity { Name = "Child2", ParentItemId = parent2.Id };
        db.ChildItems.AddRange(child1, child2);
        await db.SaveChangesAsync();

        var parent2Id = parent2.Id;

        // Soft-delete only parent1
        db.ParentItems.Remove(parent1);
        await db.SaveChangesAsync();

        DetachAll(db);

        // parent2's child should still be alive
        var parent2Children = await db.ChildItems
            .Where(c => c.ParentItemId == parent2Id)
            .ToListAsync();

        Assert.Single(parent2Children);
        Assert.Null(parent2Children[0].DeletedAt);
    }

    [Fact]
    public async Task Restore_DoesNotCascadeToChildren()
    {
        var (db, repo) = CreateParentRepo();

        // Create parent with children
        var parent = new ParentItemEntity { Name = "Parent1" };
        db.ParentItems.Add(parent);
        await db.SaveChangesAsync();

        var child1 = new ChildItemEntity { Name = "Child1", ParentItemId = parent.Id };
        var child2 = new ChildItemEntity { Name = "Child2", ParentItemId = parent.Id };
        db.ChildItems.AddRange(child1, child2);
        await db.SaveChangesAsync();

        var parentId = parent.Id;

        // Soft-delete the parent (cascades to children)
        db.ParentItems.Remove(parent);
        await db.SaveChangesAsync();

        DetachAll(db);

        // Restore the parent only — children stay deleted
        await repo.Restore(parentId);

        DetachAll(db);

        // Parent is restored
        var restoredParent = await db.ParentItems.FindAsync(parentId);
        Assert.NotNull(restoredParent);
        Assert.Null(restoredParent!.DeletedAt);

        // Children remain soft-deleted — no cascade restore
        // (some children may have been deleted intentionally before parent deletion)
        var (db2, _) = CreateParentRepo();
        // Use a fresh context sharing the same DB but with soft-delete filter disabled
        // Actually, reuse same db — just need to see deleted children
        var softDeleteFilter = new DataFilter<ISoftDeletable>();
        using (softDeleteFilter.Disable())
        {
            // We can't disable filter on existing db, so query raw
        }
        // Query via raw SQL to check children are still deleted
        var deletedCount = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"ChildItems\" WHERE \"ParentItemId\" = {0} AND \"DeletedAt\" IS NOT NULL", parentId)
            .SingleAsync();
        Assert.Equal(2, deletedCount);
    }

    [Fact]
    public async Task Restore_CascadeDoesNotRestoreOtherParentsChildren()
    {
        var (db, repo) = CreateParentRepo();

        // Create two parents with children
        var parent1 = new ParentItemEntity { Name = "Parent1" };
        var parent2 = new ParentItemEntity { Name = "Parent2" };
        db.ParentItems.AddRange(parent1, parent2);
        await db.SaveChangesAsync();

        var child1 = new ChildItemEntity { Name = "Child1", ParentItemId = parent1.Id };
        var child2 = new ChildItemEntity { Name = "Child2", ParentItemId = parent2.Id };
        db.ChildItems.AddRange(child1, child2);
        await db.SaveChangesAsync();

        var parent1Id = parent1.Id;
        var parent2Id = parent2.Id;

        // Soft-delete both parents
        db.ParentItems.Remove(parent1);
        db.ParentItems.Remove(parent2);
        await db.SaveChangesAsync();

        DetachAll(db);

        // Restore only parent1
        await repo.Restore(parent1Id);

        DetachAll(db);

        // parent2's child should remain soft-deleted
        var parent2Children = await db.ChildItems.IgnoreQueryFilters()
            .Where(c => c.ParentItemId == parent2Id)
            .ToListAsync();

        Assert.Single(parent2Children);
        Assert.NotNull(parent2Children[0].DeletedAt);
    }
}
