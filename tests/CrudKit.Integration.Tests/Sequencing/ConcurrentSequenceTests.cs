using CrudKit.Integration.Tests.Fixtures;
using Xunit;

namespace CrudKit.Integration.Tests.Sequencing;

public class ConcurrentSequenceTests
{
    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task AutoSequence_GeneratesUniqueNumbers(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenantId = Guid.NewGuid().ToString();
        using var db = fixture.CreateContext(tenantId: tenantId);
        var repo = fixture.CreateRepo<OrderEntity>(db);

        var order1 = await repo.Create(new OrderEntity { CustomerName = "A" });
        var order2 = await repo.Create(new OrderEntity { CustomerName = "B" });
        var order3 = await repo.Create(new OrderEntity { CustomerName = "C" });

        Assert.NotEmpty(order1.OrderNumber);
        Assert.NotEmpty(order2.OrderNumber);
        Assert.NotEmpty(order3.OrderNumber);

        // All numbers must be unique
        var numbers = new[] { order1.OrderNumber, order2.OrderNumber, order3.OrderNumber };
        Assert.Equal(3, numbers.Distinct().Count());

        // Should be sequential
        Assert.EndsWith("00001", order1.OrderNumber);
        Assert.EndsWith("00002", order2.OrderNumber);
        Assert.EndsWith("00003", order3.OrderNumber);
    }

    [Theory]
    [ClassData(typeof(AllProviders))]
    public async Task AutoSequence_DifferentTenants_IndependentSequences(string provider)
    {
        await using var fixture = await FixtureFactory.CreateAsync(provider);
        var tenant1 = Guid.NewGuid().ToString();
        var tenant2 = Guid.NewGuid().ToString();

        using var db1 = fixture.CreateContext(tenantId: tenant1);
        var repo1 = fixture.CreateRepo<OrderEntity>(db1);
        var order1 = await repo1.Create(new OrderEntity { CustomerName = "T1" });

        using var db2 = fixture.CreateContext(tenantId: tenant2);
        var repo2 = fixture.CreateRepo<OrderEntity>(db2);
        var order2 = await repo2.Create(new OrderEntity { CustomerName = "T2" });

        // Both tenants start at 00001
        Assert.EndsWith("00001", order1.OrderNumber);
        Assert.EndsWith("00001", order2.OrderNumber);
    }
}
