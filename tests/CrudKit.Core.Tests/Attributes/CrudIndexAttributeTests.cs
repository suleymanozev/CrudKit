using System.Reflection;
using CrudKit.Core.Attributes;
using Xunit;

namespace CrudKit.Core.Tests.Attributes;

public class CrudIndexAttributeTests
{
    [CrudIndex("Code", IsUnique = true)]
    [CrudIndex("AssociateId", "InvoiceDate")]
    [CrudIndex("Status", TenantAware = false)]
    private class TestEntity { }

    [Fact]
    public void CrudIndex_StoresProperties()
    {
        var attrs = typeof(TestEntity).GetCustomAttributes<CrudIndexAttribute>().ToList();
        Assert.Equal(3, attrs.Count);
    }

    [Fact]
    public void CrudIndex_UniqueProperty()
    {
        var attr = typeof(TestEntity).GetCustomAttributes<CrudIndexAttribute>()
            .First(a => a.Properties.Contains("Code"));
        Assert.True(attr.IsUnique);
        Assert.True(attr.TenantAware); // default
    }

    [Fact]
    public void CrudIndex_CompositeProperties()
    {
        var attr = typeof(TestEntity).GetCustomAttributes<CrudIndexAttribute>()
            .First(a => a.Properties.Length == 2);
        Assert.Equal("AssociateId", attr.Properties[0]);
        Assert.Equal("InvoiceDate", attr.Properties[1]);
    }

    [Fact]
    public void CrudIndex_TenantAwareFalse()
    {
        var attr = typeof(TestEntity).GetCustomAttributes<CrudIndexAttribute>()
            .First(a => a.Properties.Contains("Status"));
        Assert.False(attr.TenantAware);
        Assert.False(attr.IsUnique);
    }

    [Fact]
    public void CrudIndex_DefaultTenantAwareTrue()
    {
        var attr = new CrudIndexAttribute("Name");
        Assert.True(attr.TenantAware);
        Assert.False(attr.IsUnique);
    }
}
