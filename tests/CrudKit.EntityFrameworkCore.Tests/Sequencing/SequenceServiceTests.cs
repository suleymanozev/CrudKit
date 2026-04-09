using CrudKit.EntityFrameworkCore.Sequencing;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Sequencing;

public class SequenceServiceTests
{
    [Fact]
    public async Task NextValueAsync_FirstCall_Returns1()
    {
        using var ctx = DbHelper.CreateDb();
        var service = new SequenceService(ctx);

        var value = await service.NextValueAsync("TestEntity", "", "TST-2026-");
        Assert.Equal(1, value);
    }

    [Fact]
    public async Task NextValueAsync_SecondCall_Returns2()
    {
        using var ctx = DbHelper.CreateDb();
        var service = new SequenceService(ctx);

        await service.NextValueAsync("TestEntity", "", "TST-2026-");
        var second = await service.NextValueAsync("TestEntity", "", "TST-2026-");
        Assert.Equal(2, second);
    }

    [Fact]
    public async Task NextValueAsync_DifferentTenants_IndependentSequences()
    {
        using var ctx = DbHelper.CreateDb();
        var service = new SequenceService(ctx);

        var t1v1 = await service.NextValueAsync("Invoice", "tenant-a", "INV-2026-");
        var t2v1 = await service.NextValueAsync("Invoice", "tenant-b", "INV-2026-");
        var t1v2 = await service.NextValueAsync("Invoice", "tenant-a", "INV-2026-");

        Assert.Equal(1, t1v1);
        Assert.Equal(1, t2v1);
        Assert.Equal(2, t1v2);
    }

    [Fact]
    public async Task NextValueAsync_DifferentPrefixes_IndependentSequences()
    {
        using var ctx = DbHelper.CreateDb();
        var service = new SequenceService(ctx);

        var v2026 = await service.NextValueAsync("Invoice", "", "INV-2026-");
        var v2027 = await service.NextValueAsync("Invoice", "", "INV-2027-");

        Assert.Equal(1, v2026);
        Assert.Equal(1, v2027);
    }
}
