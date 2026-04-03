using CrudKit.EntityFrameworkCore.Numbering;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Numbering;

public class SequenceGeneratorTests
{
    private static (TestDbContext db, SequenceGenerator gen) Setup()
    {
        var db = DbHelper.CreateDb();
        var gen = new SequenceGenerator(db);
        return (db, gen);
    }

    [Fact]
    public async Task Next_ReturnsFormattedNumber()
    {
        var (_, gen) = Setup();
        var number = await gen.Next<InvoiceEntity>("tenant-1");
        Assert.Matches(@"^INV-\d{4}-\d{5}$", number);
    }

    [Fact]
    public async Task Next_IncrementsSequentially()
    {
        var (_, gen) = Setup();
        var first  = await gen.Next<InvoiceEntity>("tenant-1");
        var second = await gen.Next<InvoiceEntity>("tenant-1");
        var third  = await gen.Next<InvoiceEntity>("tenant-1");

        Assert.EndsWith("-00001", first);
        Assert.EndsWith("-00002", second);
        Assert.EndsWith("-00003", third);
    }

    [Fact]
    public async Task Next_DifferentTenants_HaveSeparateSequences()
    {
        var (_, gen) = Setup();
        var t1 = await gen.Next<InvoiceEntity>("tenant-1");
        var t2 = await gen.Next<InvoiceEntity>("tenant-2");

        Assert.EndsWith("-00001", t1);
        Assert.EndsWith("-00001", t2); // separate counter per tenant
    }

    [Fact]
    public async Task Next_YearlyReset_UsesCurrentYear()
    {
        var (_, gen) = Setup();
        var number = await gen.Next<InvoiceEntity>("tenant-1");
        var year = DateTime.UtcNow.Year.ToString();
        Assert.Contains(year, number);
    }
}
