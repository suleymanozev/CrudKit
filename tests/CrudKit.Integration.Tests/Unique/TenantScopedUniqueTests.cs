using CrudKit.Integration.Tests.Fixtures;

using Xunit;

namespace CrudKit.Integration.Tests.Unique;

public class TenantScopedUniqueTests
{
    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task SameTenant_DuplicateCode_Throws(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<TenantUniqueItem>(db);

        await repo.Create(new TenantUniqueItem { Code = "PRD-001", Name = "Widget" });

        // Same tenant, same code — should throw (unique constraint)
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await repo.Create(new TenantUniqueItem { Code = "PRD-001", Name = "Duplicate" }));
    }

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task DifferentTenants_SameCode_Allowed(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenant1 = Guid.NewGuid().ToString();
        var tenant2 = Guid.NewGuid().ToString();

        using var db1 = fixture.CreateContext(tenantId: tenant1);
        var repo1 = fixture.CreateRepo<TenantUniqueItem>(db1);
        await repo1.Create(new TenantUniqueItem { Code = "PRD-001", Name = "Tenant1 Widget" });

        using var db2 = fixture.CreateContext(tenantId: tenant2);
        var repo2 = fixture.CreateRepo<TenantUniqueItem>(db2);

        // Different tenant, same code — should succeed
        var item = await repo2.Create(new TenantUniqueItem { Code = "PRD-001", Name = "Tenant2 Widget" });
        Assert.Equal("PRD-001", item.Code);
    }

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task SoftDeleted_SameCode_AllowsNewRecord(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<TenantUniqueItem>(db);

        var item = await repo.Create(new TenantUniqueItem { Code = "PRD-001", Name = "Original" });
        await repo.Delete(item.Id);

        // Soft-deleted record does not occupy the unique slot — new record with same code must succeed
        var newItem = await repo.Create(new TenantUniqueItem { Code = "PRD-001", Name = "Replacement" });
        Assert.Equal("PRD-001", newItem.Code);
    }
}
