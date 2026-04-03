using CrudKit.Core.Models;
using Xunit;

namespace CrudKit.Core.Tests.Models;

public class PaginatedTests
{
    [Fact]
    public void Constructor_ShouldHaveEmptyDataByDefault()
    {
        var paged = new Paginated<string>();
        Assert.Empty(paged.Data);
        Assert.Equal(0, paged.Total);
    }

    [Fact]
    public void TotalPages_ShouldCalculateCorrectly()
    {
        var paged = new Paginated<int>
        {
            Total = 25,
            PerPage = 10,
            TotalPages = (int)Math.Ceiling(25.0 / 10)
        };
        Assert.Equal(3, paged.TotalPages);
    }

    [Fact]
    public void TotalPages_WhenExactDivision_ShouldNotAddExtra()
    {
        var paged = new Paginated<int>
        {
            Total = 20,
            PerPage = 10,
            TotalPages = (int)Math.Ceiling(20.0 / 10)
        };
        Assert.Equal(2, paged.TotalPages);
    }

    [Fact]
    public void TotalPages_WhenZeroTotal_ShouldBeZero()
    {
        var paged = new Paginated<int>
        {
            Total = 0,
            PerPage = 10,
            TotalPages = (int)Math.Ceiling(0.0 / 10)
        };
        Assert.Equal(0, paged.TotalPages);
    }
}
