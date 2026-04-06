using CrudKit.Api.Filters;
using Xunit;

namespace CrudKit.Api.Tests.Filters;

/// <summary>
/// Tests for CrudAuthorizationFilter.DetermineOperation — covers path-based
/// detection branches for restore, transition, bulk-*, export, import.
/// </summary>
public class CrudAuthorizationFilterTests
{
    [Theory]
    [InlineData("POST", "/api/orders/abc/restore", "Restore")]
    [InlineData("POST", "/api/orders/abc/Restore", "Restore")]
    public void DetermineOperation_Restore(string method, string path, string expected)
    {
        var result = CrudAuthorizationFilter.DetermineOperation(method, path);
        Assert.Equal(Enum.Parse<CrudOperation>(expected), result);
    }

    [Theory]
    [InlineData("POST", "/api/orders/abc/transition/process", "Transition")]
    [InlineData("POST", "/api/orders/abc/transition/complete", "Transition")]
    public void DetermineOperation_Transition(string method, string path, string expected)
    {
        var result = CrudAuthorizationFilter.DetermineOperation(method, path);
        Assert.Equal(Enum.Parse<CrudOperation>(expected), result);
    }

    [Fact]
    public void DetermineOperation_BulkCount_IsRead()
    {
        var result = CrudAuthorizationFilter.DetermineOperation("POST", "/api/products/bulk-count");
        Assert.Equal(CrudOperation.Read, result);
    }

    [Fact]
    public void DetermineOperation_BulkDelete_IsDelete()
    {
        var result = CrudAuthorizationFilter.DetermineOperation("POST", "/api/products/bulk-delete");
        Assert.Equal(CrudOperation.Delete, result);
    }

    [Fact]
    public void DetermineOperation_BulkUpdate_IsUpdate()
    {
        var result = CrudAuthorizationFilter.DetermineOperation("POST", "/api/products/bulk-update");
        Assert.Equal(CrudOperation.Update, result);
    }

    [Fact]
    public void DetermineOperation_Export_IsExport()
    {
        var result = CrudAuthorizationFilter.DetermineOperation("GET", "/api/products/export");
        Assert.Equal(CrudOperation.Export, result);
    }

    [Fact]
    public void DetermineOperation_Import_IsImport()
    {
        var result = CrudAuthorizationFilter.DetermineOperation("POST", "/api/products/import");
        Assert.Equal(CrudOperation.Import, result);
    }

    [Theory]
    [InlineData("GET", "/api/products", "Read")]
    [InlineData("POST", "/api/products", "Create")]
    [InlineData("PUT", "/api/products/abc", "Update")]
    [InlineData("DELETE", "/api/products/abc", "Delete")]
    [InlineData("PATCH", "/api/products/abc", "Read")]  // Unknown method falls back to Read
    public void DetermineOperation_HttpMethodFallback(string method, string path, string expected)
    {
        var result = CrudAuthorizationFilter.DetermineOperation(method, path);
        Assert.Equal(Enum.Parse<CrudOperation>(expected), result);
    }
}
