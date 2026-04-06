using CrudKit.Core.Auth;
using CrudKit.Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.Core.Tests.Context;

public class AppContextTests
{
    // Minimal IServiceProvider for constructing AppContext
    private static IServiceProvider BuildServices()
        => new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void AppContext_TenantId_ReadsFromTenantContext()
    {
        var ctx = new CrudKit.Core.Context.AppContext
        {
            Services = BuildServices(),
            CurrentUser = new FakeCurrentUser(),
            TenantContext = new TenantContext { TenantId = "acme" }
        };

        Assert.Equal("acme", ctx.TenantId);
    }

    [Fact]
    public void AppContext_TenantId_NullWhenNoTenantContext()
    {
        var ctx = new CrudKit.Core.Context.AppContext
        {
            Services = BuildServices(),
            CurrentUser = new FakeCurrentUser(),
            TenantContext = null
        };

        Assert.Null(ctx.TenantId);
    }

    [Fact]
    public void AppContext_UserId_ReadsFromCurrentUser()
    {
        var user = new FakeCurrentUser { Id = "user-42" };
        var ctx = new CrudKit.Core.Context.AppContext
        {
            Services = BuildServices(),
            CurrentUser = user
        };

        Assert.Equal("user-42", ctx.UserId);
    }

    [Fact]
    public void AppContext_IsAuthenticated_ReflectsCurrentUser()
    {
        var ctx = new CrudKit.Core.Context.AppContext
        {
            Services = BuildServices(),
            CurrentUser = new AnonymousCurrentUser()
        };

        Assert.False(ctx.IsAuthenticated);
    }

    [Fact]
    public void AppContext_RequestId_IsUniquePerInstance()
    {
        var svc = BuildServices();
        var user = new FakeCurrentUser();

        var ctx1 = new CrudKit.Core.Context.AppContext { Services = svc, CurrentUser = user };
        var ctx2 = new CrudKit.Core.Context.AppContext { Services = svc, CurrentUser = user };

        Assert.NotEqual(ctx1.RequestId, ctx2.RequestId);
    }
}
