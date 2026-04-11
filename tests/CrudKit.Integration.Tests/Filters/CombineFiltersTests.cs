using CrudKit.Core.Models;
using CrudKit.Integration.Tests.Fixtures;
using Xunit;

namespace CrudKit.Integration.Tests.Filters;

public class CombineFiltersTests
{
    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task TenantAndSoftDeleteFilters_CombinedQuery_TranslatesCorrectly(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<TenantAwareItem>(db);

        // Create items
        await repo.Create(new TenantAwareItem { Name = "Item1" });
        await repo.Create(new TenantAwareItem { Name = "Item2" });

        // List — should return 2 (tenant filtered, soft-delete filtered)
        var result = await repo.List(new ListParams { Page = 1, PerPage = 10 });
        Assert.Equal(2, result.Total);
    }

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task DifferentTenants_IsolatedData(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenant1 = Guid.NewGuid().ToString();
        var tenant2 = Guid.NewGuid().ToString();

        using var db1 = fixture.CreateContext(tenantId: tenant1);
        var repo1 = fixture.CreateRepo<TenantAwareItem>(db1);
        await repo1.Create(new TenantAwareItem { Name = "Tenant1Item" });

        using var db2 = fixture.CreateContext(tenantId: tenant2);
        var repo2 = fixture.CreateRepo<TenantAwareItem>(db2);
        await repo2.Create(new TenantAwareItem { Name = "Tenant2Item" });

        var result1 = await repo1.List(new ListParams { Page = 1, PerPage = 10 });
        var result2 = await repo2.List(new ListParams { Page = 1, PerPage = 10 });

        Assert.Equal(1, result1.Total);
        Assert.Equal(1, result2.Total);
    }
}
