using CrudKit.Integration.Tests.Fixtures;
using Xunit;

namespace CrudKit.Integration.Tests.Sequencing;

public class ConcurrentSequenceTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();

    [Fact]
    public async Task AutoSequence_GeneratesUniqueNumbers()
    {
        var tenantId = Guid.NewGuid().ToString();
        using var db = _fixture.CreateContext(tenantId: tenantId);
        var repo = _fixture.CreateRepo<OrderEntity>(db);

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

    [Fact]
    public async Task AutoSequence_DifferentTenants_IndependentSequences()
    {
        var tenant1 = Guid.NewGuid().ToString();
        var tenant2 = Guid.NewGuid().ToString();

        using var db1 = _fixture.CreateContext(tenantId: tenant1);
        var repo1 = _fixture.CreateRepo<OrderEntity>(db1);
        var order1 = await repo1.Create(new OrderEntity { CustomerName = "T1" });

        using var db2 = _fixture.CreateContext(tenantId: tenant2);
        var repo2 = _fixture.CreateRepo<OrderEntity>(db2);
        var order2 = await repo2.Create(new OrderEntity { CustomerName = "T2" });

        // Both tenants start at 00001
        Assert.EndsWith("00001", order1.OrderNumber);
        Assert.EndsWith("00001", order2.OrderNumber);
    }

    public void Dispose() => _fixture.Dispose();
}
