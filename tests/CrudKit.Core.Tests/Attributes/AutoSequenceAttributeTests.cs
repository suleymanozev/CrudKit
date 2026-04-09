using System.Reflection;
using CrudKit.Core.Attributes;
using Xunit;

namespace CrudKit.Core.Tests.Attributes;

public class AutoSequenceAttributeTests
{
    private class TestInvoice
    {
        [AutoSequence("INV-{year}-{seq:5}")]
        public string InvoiceNumber { get; set; } = "";

        public string Name { get; set; } = "";
    }

    [Fact]
    public void AutoSequence_StoresTemplate()
    {
        var prop = typeof(TestInvoice).GetProperty("InvoiceNumber");
        var attr = prop!.GetCustomAttribute<AutoSequenceAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("INV-{year}-{seq:5}", attr!.Template);
    }

    [Fact]
    public void AutoSequence_NotPresentOnOtherProperties()
    {
        var prop = typeof(TestInvoice).GetProperty("Name");
        var attr = prop!.GetCustomAttribute<AutoSequenceAttribute>();
        Assert.Null(attr);
    }

    [Fact]
    public void AutoSequence_TargetsProperties()
    {
        var attrUsage = typeof(AutoSequenceAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(attrUsage);
        Assert.True(attrUsage!.ValidOn.HasFlag(AttributeTargets.Property));
    }
}
