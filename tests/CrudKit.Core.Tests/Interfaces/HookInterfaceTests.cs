using System.Reflection;
using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Core.Tests.Interfaces;

public class HookInterfaceTests
{
    [Fact]
    public void IGlobalCrudHook_HasBeforeUpdateWithExistingEntity()
    {
        var method = typeof(IGlobalCrudHook).GetMethod(
            "BeforeUpdate",
            [typeof(object), typeof(object), typeof(CrudKit.Core.Context.AppContext)]);
        Assert.NotNull(method);
    }

    [Fact]
    public void IGlobalCrudHook_HasAfterUpdateWithExistingEntity()
    {
        var method = typeof(IGlobalCrudHook).GetMethod(
            "AfterUpdate",
            [typeof(object), typeof(object), typeof(CrudKit.Core.Context.AppContext)]);
        Assert.NotNull(method);
    }

    [Fact]
    public void ICrudHooks_HasBeforeUpdateWithExistingEntity()
    {
        var methods = typeof(ICrudHooks<>).GetMethods()
            .Where(m => m.Name == "BeforeUpdate" && m.GetParameters().Length == 3);
        Assert.Single(methods);
    }

    [Fact]
    public void ICrudHooks_HasAfterUpdateWithExistingEntity()
    {
        var methods = typeof(ICrudHooks<>).GetMethods()
            .Where(m => m.Name == "AfterUpdate" && m.GetParameters().Length == 3);
        Assert.Single(methods);
    }

    [Fact]
    public void IGlobalCrudHook_DefaultImplementation_DoesNotThrow()
    {
        IGlobalCrudHook hook = new TestGlobalHook();
        var task = hook.BeforeUpdate(new object(), new CrudKit.Core.Context.AppContext
        {
            Services = null!,
            CurrentUser = null!
        });
        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void IGlobalCrudHook_3ParamOverload_DefaultCallsTwoParam()
    {
        IGlobalCrudHook hook = new TestGlobalHook();
        var task = hook.BeforeUpdate(new object(), new object(),
            new CrudKit.Core.Context.AppContext { Services = null!, CurrentUser = null! });
        Assert.True(task.IsCompleted);
    }

    private class TestGlobalHook : IGlobalCrudHook { }
}
