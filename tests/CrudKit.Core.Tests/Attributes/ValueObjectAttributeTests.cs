using System.Reflection;
using CrudKit.Core.Attributes;
using Xunit;

namespace CrudKit.Core.Tests.Attributes;

public class ValueObjectAttributeTests
{
    [ValueObject]
    private class TestMoney
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "TRY";
    }

    private class TestProduct
    {
        [Flatten]
        public TestMoney PurchasePrice { get; set; } = new();

        public TestMoney SalesPrice { get; set; } = new();
    }

    [Fact]
    public void ValueObject_CanBeAppliedToClass()
    {
        var attr = typeof(TestMoney).GetCustomAttribute<ValueObjectAttribute>();
        Assert.NotNull(attr);
    }

    [Fact]
    public void Flatten_CanBeAppliedToProperty()
    {
        var prop = typeof(TestProduct).GetProperty("PurchasePrice");
        Assert.NotNull(prop!.GetCustomAttribute<FlattenAttribute>());
    }

    [Fact]
    public void Flatten_NotPresentOnUnmarkedProperty()
    {
        var prop = typeof(TestProduct).GetProperty("SalesPrice");
        Assert.Null(prop!.GetCustomAttribute<FlattenAttribute>());
    }
}
