using CrudKit.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace CrudKit.Core.Tests.Models;

public class ListParamsTests
{
    [Fact]
    public void FromQuery_ShouldParsePageAndPerPage()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "page", "3" },
            { "per_page", "50" }
        });

        var result = ListParams.FromQuery(query);
        Assert.Equal(3, result.Page);
        Assert.Equal(50, result.PerPage);
    }

    [Fact]
    public void FromQuery_ShouldClampPerPageToMax100()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "per_page", "500" }
        });

        var result = ListParams.FromQuery(query);
        Assert.Equal(100, result.PerPage);
    }

    [Fact]
    public void FromQuery_ShouldDefaultToPage1PerPage20()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>());
        var result = ListParams.FromQuery(query);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PerPage);
    }

    [Fact]
    public void FromQuery_ShouldParseSort()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "sort", "-created_at,username" }
        });

        var result = ListParams.FromQuery(query);
        Assert.Equal("-created_at,username", result.Sort);
    }

    [Fact]
    public void FromQuery_ShouldExtractFilters()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "page", "1" },
            { "sort", "-id" },
            { "username", "eq:ali" },
            { "age", "gte:18" }
        });

        var result = ListParams.FromQuery(query);
        Assert.Equal(2, result.Filters.Count);
        Assert.True(result.Filters.ContainsKey("username"));
        Assert.True(result.Filters.ContainsKey("age"));
        Assert.Equal("eq", result.Filters["username"].Operator);
        Assert.Equal("gte", result.Filters["age"].Operator);
    }
}
