using CrudKit.Core.Models;
using Xunit;

namespace CrudKit.Core.Tests.Models;

public class FilterOpTests
{
    [Theory]
    [InlineData("ali", "eq", "ali")]
    [InlineData("eq:ali", "eq", "ali")]
    [InlineData("neq:cancelled", "neq", "cancelled")]
    [InlineData("gt:10", "gt", "10")]
    [InlineData("gte:18", "gte", "18")]
    [InlineData("lt:100", "lt", "100")]
    [InlineData("lte:99.9", "lte", "99.9")]
    [InlineData("like:gmail", "like", "gmail")]
    [InlineData("starts:admin", "starts", "admin")]
    [InlineData("null", "null", "")]
    [InlineData("notnull", "notnull", "")]
    public void Parse_ShouldExtractOperatorAndValue(string raw, string expectedOp, string expectedVal)
    {
        var result = FilterOp.Parse(raw);
        Assert.Equal(expectedOp, result.Operator);
        Assert.Equal(expectedVal, result.Value);
    }

    [Fact]
    public void Parse_InOperator_ShouldSplitValues()
    {
        var result = FilterOp.Parse("in:a,b,c");
        Assert.Equal("in", result.Operator);
        Assert.NotNull(result.Values);
        Assert.Equal(3, result.Values.Count);
        Assert.Equal(new[] { "a", "b", "c" }, result.Values);
    }

    [Fact]
    public void Parse_EmptyString_ShouldReturnEqWithEmpty()
    {
        var result = FilterOp.Parse("");
        Assert.Equal("eq", result.Operator);
        Assert.Equal("", result.Value);
    }

    [Fact]
    public void Parse_TooLongValue_ShouldThrow()
    {
        var longValue = new string('x', 501);
        var ex = Assert.Throws<ArgumentException>(() => FilterOp.Parse(longValue));
        Assert.Contains("Filter value too long", ex.Message);
    }

    [Fact]
    public void Parse_MaxLengthValue_ShouldSucceed()
    {
        var maxValue = new string('x', 500);
        var result = FilterOp.Parse(maxValue);
        Assert.Equal("eq", result.Operator);
        Assert.Equal(maxValue, result.Value);
    }
}
