using CrudKit.Core.Auth;
using Xunit;

namespace CrudKit.Core.Tests.Auth;

public class AnonymousCurrentUserTests
{
    [Fact]
    public void AnonymousCurrentUser_HasCorrectDefaults()
    {
        var anon = new AnonymousCurrentUser();

        Assert.Null(anon.Id);
        Assert.Null(anon.Username);
        Assert.False(anon.IsAuthenticated);
        Assert.Empty(anon.Roles);
        Assert.False(anon.HasRole("admin"));
        Assert.False(anon.HasPermission("entity", "action"));
        Assert.NotNull(anon.AccessibleTenants);
        Assert.Empty(anon.AccessibleTenants);
    }

    [Fact]
    public void HasRole_AlwaysReturnsFalse()
    {
        var anon = new AnonymousCurrentUser();

        Assert.False(anon.HasRole("admin"));
        Assert.False(anon.HasRole("user"));
        Assert.False(anon.HasRole(""));
    }

    [Fact]
    public void HasPermission_AlwaysReturnsFalse()
    {
        var anon = new AnonymousCurrentUser();

        Assert.False(anon.HasPermission("Order", "Read"));
        Assert.False(anon.HasPermission("Order", "Delete"));
        Assert.False(anon.HasPermission("", ""));
    }

    [Fact]
    public void AccessibleTenants_IsEmptyList_NotNull()
    {
        // Empty (not null) means no cross-tenant access, as opposed to null (all tenants)
        var anon = new AnonymousCurrentUser();

        Assert.NotNull(anon.AccessibleTenants);
        Assert.Empty(anon.AccessibleTenants);
    }
}
