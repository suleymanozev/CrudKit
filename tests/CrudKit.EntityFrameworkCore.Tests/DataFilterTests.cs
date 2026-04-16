using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Tenancy;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests;

/// <summary>
/// Tests for the IDataFilter runtime toggle of soft-delete and tenant query filters.
/// </summary>
public class DataFilterTests
{
    // -----------------------------------------------------------------------
    // Soft-delete filter tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SoftDeleteFilter_DefaultEnabled_HidesDeletedRecords()
    {
        // No filter injected — default behaviour must still hide deleted records.
        using var db = DbHelper.CreateDb();

        var entity = new SoftPersonEntity { Name = "Alice" };
        db.SoftPersons.Add(entity);
        await db.SaveChangesAsync();

        db.SoftPersons.Remove(entity);
        await db.SaveChangesAsync();

        Assert.Empty(await db.SoftPersons.ToListAsync());
    }

    [Fact]
    public async Task SoftDeleteFilter_Disable_ShowsDeletedRecords()
    {
        var softDeleteFilter = new DataFilter<ISoftDeletable>();
        using var db = DbHelper.CreateDb(softDeleteFilter: softDeleteFilter);

        var entity = new SoftPersonEntity { Name = "Bob" };
        db.SoftPersons.Add(entity);
        await db.SaveChangesAsync();

        db.SoftPersons.Remove(entity);
        await db.SaveChangesAsync();

        // Filter active → not visible.
        Assert.Empty(await db.SoftPersons.ToListAsync());

        // Disable filter → deleted record becomes visible.
        using (softDeleteFilter.Disable())
        {
            var all = await db.SoftPersons.ToListAsync();
            Assert.Single(all);
            Assert.NotNull(all[0].DeletedAt);
        }
    }

    [Fact]
    public async Task SoftDeleteFilter_ScopeRestores_AfterDispose()
    {
        var softDeleteFilter = new DataFilter<ISoftDeletable>();
        using var db = DbHelper.CreateDb(softDeleteFilter: softDeleteFilter);

        var entity = new SoftPersonEntity { Name = "Carol" };
        db.SoftPersons.Add(entity);
        await db.SaveChangesAsync();

        db.SoftPersons.Remove(entity);
        await db.SaveChangesAsync();

        // Disable inside a scope.
        using (softDeleteFilter.Disable())
        {
            Assert.Single(await db.SoftPersons.ToListAsync());
        }

        // After scope is disposed the filter must be active again.
        Assert.Empty(await db.SoftPersons.ToListAsync());
    }

    [Fact]
    public async Task SoftDeleteFilter_Enable_RestoresAfterManualDisable()
    {
        var softDeleteFilter = new DataFilter<ISoftDeletable>();
        using var db = DbHelper.CreateDb(softDeleteFilter: softDeleteFilter);

        var entity = new SoftPersonEntity { Name = "Dave" };
        db.SoftPersons.Add(entity);
        await db.SaveChangesAsync();

        db.SoftPersons.Remove(entity);
        await db.SaveChangesAsync();

        // Manually disable (no using-statement).
        var outerScope = softDeleteFilter.Disable();

        // Deleted record visible.
        Assert.Single(await db.SoftPersons.ToListAsync());

        // Re-enable in a nested scope while already disabled.
        using (softDeleteFilter.Enable())
        {
            // Filter is active inside this inner scope.
            Assert.Empty(await db.SoftPersons.ToListAsync());
        }

        // Inner scope disposed → back to disabled (outer scope still open).
        Assert.Single(await db.SoftPersons.ToListAsync());

        // Dispose outer scope.
        outerScope.Dispose();

        // Back to enabled.
        Assert.Empty(await db.SoftPersons.ToListAsync());
    }

    // -----------------------------------------------------------------------
    // Tenant filter tests
    // Both dbA and dbB must share the same SQLite connection so they see the same data.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates two TestDbContext instances that share the same SQLite in-memory connection,
    /// so data written by one is visible to the other (modulo query filters).
    /// </summary>
    private static (TestDbContext dbA, TestDbContext dbB, SqliteConnection conn) CreateSharedTenantDbs(
        string tenantA, string tenantB,
        IDataFilter<IMultiTenant>? tenantFilter = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var optA = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options;
        var optB = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options;

        var dbA = new TestDbContext(optA, new FakeCurrentUser(),
            tenantContext: new TenantContext { TenantId = tenantA },
            tenantFilter: tenantFilter);
        dbA.Database.EnsureCreated();

        var dbB = new TestDbContext(optB, new FakeCurrentUser(),
            tenantContext: new TenantContext { TenantId = tenantB },
            tenantFilter: tenantFilter);

        return (dbA, dbB, connection);
    }

    [Fact]
    public async Task TenantFilter_DefaultEnabled_HidesOtherTenantRecords()
    {
        // Verify baseline: without filter injection the tenant filter still works.
        var (dbA, dbB, conn) = CreateSharedTenantDbs("tenant-a", "tenant-b");
        using var _ = conn;
        using var _a = dbA;
        using var _b = dbB;

        dbA.TenantPersons.Add(new TenantPersonEntity { Name = "TenantA-Person" });
        await dbA.SaveChangesAsync();

        // TenantB context must see zero records from TenantA.
        Assert.Empty(await dbB.TenantPersons.ToListAsync());
    }

    [Fact]
    public async Task TenantFilter_Disable_ShowsAllTenants()
    {
        var tenantFilter = new DataFilter<IMultiTenant>();
        var (dbA, dbB, conn) = CreateSharedTenantDbs("tenant-a", "tenant-b", tenantFilter);
        using var _ = conn;
        using var _a = dbA;
        using var _b = dbB;

        dbA.TenantPersons.Add(new TenantPersonEntity { Name = "PersonA" });
        await dbA.SaveChangesAsync();

        dbB.TenantPersons.Add(new TenantPersonEntity { Name = "PersonB" });
        await dbB.SaveChangesAsync();

        // Filter active on dbA → only tenant-a records visible.
        Assert.Single(await dbA.TenantPersons.ToListAsync());

        // Disable → both records visible from dbA.
        using (tenantFilter.Disable())
        {
            var all = await dbA.TenantPersons.ToListAsync();
            Assert.Equal(2, all.Count);
        }

        // Scope disposed → filter restored.
        Assert.Single(await dbA.TenantPersons.ToListAsync());
    }

    [Fact]
    public async Task TenantFilter_ScopeRestores_AfterDispose()
    {
        var tenantFilter = new DataFilter<IMultiTenant>();
        var (dbA, dbB, conn) = CreateSharedTenantDbs("a", "b", tenantFilter);
        using var _ = conn;
        using var _a = dbA;
        using var _b = dbB;

        dbA.TenantPersons.Add(new TenantPersonEntity { Name = "FromA" });
        await dbA.SaveChangesAsync();

        dbB.TenantPersons.Add(new TenantPersonEntity { Name = "FromB" });
        await dbB.SaveChangesAsync();

        using (tenantFilter.Disable())
        {
            Assert.Equal(2, (await dbA.TenantPersons.ToListAsync()).Count);
        }

        // After scope: only tenant-a record visible from dbA.
        Assert.Single(await dbA.TenantPersons.ToListAsync());
    }

    // -----------------------------------------------------------------------
    // DataFilter<T> unit tests (state machine, no DB)
    // -----------------------------------------------------------------------

    [Fact]
    public void DataFilter_IsEnabledByDefault()
    {
        var filter = new DataFilter<ISoftDeletable>();
        Assert.True(filter.IsEnabled);
    }

    [Fact]
    public void DataFilter_Disable_SetsIsEnabledFalse()
    {
        var filter = new DataFilter<ISoftDeletable>();
        using (filter.Disable())
        {
            Assert.False(filter.IsEnabled);
        }
        Assert.True(filter.IsEnabled);
    }

    [Fact]
    public void DataFilter_Enable_RestoresPreviousState()
    {
        var filter = new DataFilter<ISoftDeletable>();
        using (filter.Disable())
        {
            Assert.False(filter.IsEnabled);
            using (filter.Enable())
            {
                Assert.True(filter.IsEnabled);
            }
            Assert.False(filter.IsEnabled);
        }
        Assert.True(filter.IsEnabled);
    }

    [Fact]
    public void DataFilter_NestedDisable_RestoresCorrectly()
    {
        var filter = new DataFilter<ISoftDeletable>();

        var scope1 = filter.Disable();
        Assert.False(filter.IsEnabled);

        var scope2 = filter.Disable();
        Assert.False(filter.IsEnabled);

        scope2.Dispose();
        Assert.False(filter.IsEnabled); // still disabled — scope1 is open

        scope1.Dispose();
        Assert.True(filter.IsEnabled); // now fully restored
    }
}
