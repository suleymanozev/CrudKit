using CrudKit.Integration.Tests.Fixtures;
using Xunit;

namespace CrudKit.Integration.Tests.Index;

public class CrudIndexTests
{
    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task CompositeUniqueIndex_SameTenant_DuplicateBlocked(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<IndexedItem>(db);

        await repo.Create(new IndexedItem { Code = "A1", Category = "Electronics", Priority = 1 });

        // Same tenant, same Code+Category — should fail (composite unique index includes TenantId)
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await repo.Create(new IndexedItem { Code = "A1", Category = "Electronics", Priority = 2 }));
    }

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task CompositeUniqueIndex_SameTenant_DifferentCode_Allowed(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<IndexedItem>(db);

        await repo.Create(new IndexedItem { Code = "A1", Category = "Electronics", Priority = 1 });

        // Different Code, same Category — allowed (composite unique is on Code+Category)
        var item = await repo.Create(new IndexedItem { Code = "A2", Category = "Electronics", Priority = 2 });
        Assert.Equal("A2", item.Code);
    }

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task CompositeUniqueIndex_DifferentTenants_SameCombo_Allowed(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);

        var tenant1 = Guid.NewGuid().ToString();
        using var db1 = fixture.CreateContext(tenantId: tenant1);
        var repo1 = fixture.CreateRepo<IndexedItem>(db1);
        await repo1.Create(new IndexedItem { Code = "A1", Category = "Electronics", Priority = 1 });

        var tenant2 = Guid.NewGuid().ToString();
        using var db2 = fixture.CreateContext(tenantId: tenant2);
        var repo2 = fixture.CreateRepo<IndexedItem>(db2);

        // Different tenant, same Code+Category — allowed because TenantId is part of the index
        var item = await repo2.Create(new IndexedItem { Code = "A1", Category = "Electronics", Priority = 1 });
        Assert.Equal("A1", item.Code);
    }

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task CustomNameIndex_SameTenant_DuplicateCodeBlocked(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<IndexedItem>(db);

        await repo.Create(new IndexedItem { Code = "B1", Category = "Tools", Priority = 5 });

        // IX_Custom_Code index: (TenantId, Code) unique — same tenant, same Code must be blocked
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await repo.Create(new IndexedItem { Code = "B1", Category = "Different", Priority = 99 }));
    }

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task TenantIndependentIndex_DifferentTenants_AllowsDuplicatePriority(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);

        var tenant1 = Guid.NewGuid().ToString();
        using var db1 = fixture.CreateContext(tenantId: tenant1);
        var repo1 = fixture.CreateRepo<IndexedItem>(db1);
        await repo1.Create(new IndexedItem { Code = "C1", Category = "X", Priority = 10 });

        var tenant2 = Guid.NewGuid().ToString();
        using var db2 = fixture.CreateContext(tenantId: tenant2);
        var repo2 = fixture.CreateRepo<IndexedItem>(db2);

        // Priority index has TenantAware=false (non-unique) — duplicate priority across tenants is fine
        var item = await repo2.Create(new IndexedItem { Code = "C2", Category = "Y", Priority = 10 });
        Assert.Equal(10, item.Priority);
    }
}
