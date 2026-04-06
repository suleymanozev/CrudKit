using CrudKit.Api.Configuration;
using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Api.Tests.Options;

public class CrudKitApiOptionsTests
{
    [Fact]
    public void UseEnumAsString_SetsFlag()
    {
        var opts = new CrudKitApiOptions();
        Assert.False(opts.EnumAsStringEnabled);

        var result = opts.UseEnumAsString();

        Assert.True(opts.EnumAsStringEnabled);
        Assert.Same(opts, result); // fluent return
    }

    [Fact]
    public void UseExport_SetsFlag()
    {
        var opts = new CrudKitApiOptions();
        Assert.False(opts.ExportEnabled);

        var result = opts.UseExport();

        Assert.True(opts.ExportEnabled);
        Assert.Same(opts, result);
    }

    [Fact]
    public void UseImport_SetsFlag()
    {
        var opts = new CrudKitApiOptions();
        Assert.False(opts.ImportEnabled);

        var result = opts.UseImport();

        Assert.True(opts.ImportEnabled);
        Assert.Same(opts, result);
    }

    // Test hook type that implements IGlobalCrudHook
    private class TestGlobalHook : IGlobalCrudHook { }
    private class AnotherGlobalHook : IGlobalCrudHook { }

    [Fact]
    public void UseGlobalHook_AddsTypeToList()
    {
        var opts = new CrudKitApiOptions();
        Assert.Empty(opts.GlobalHookTypes);

        var result = opts.UseGlobalHook<TestGlobalHook>();

        Assert.Single(opts.GlobalHookTypes);
        Assert.Equal(typeof(TestGlobalHook), opts.GlobalHookTypes[0]);
        Assert.Same(opts, result);
    }

    [Fact]
    public void UseGlobalHook_MultipleHooks_PreservesOrder()
    {
        var opts = new CrudKitApiOptions();

        opts.UseGlobalHook<TestGlobalHook>()
            .UseGlobalHook<AnotherGlobalHook>();

        Assert.Equal(2, opts.GlobalHookTypes.Count);
        Assert.Equal(typeof(TestGlobalHook), opts.GlobalHookTypes[0]);
        Assert.Equal(typeof(AnotherGlobalHook), opts.GlobalHookTypes[1]);
    }

    [Fact]
    public void UseAuditTrail_EnablesAuditAndReturnsOptions()
    {
        var opts = new CrudKitApiOptions();
        Assert.False(opts.AuditTrailEnabled);

        var auditOpts = opts.UseAuditTrail();

        Assert.True(opts.AuditTrailEnabled);
        Assert.NotNull(auditOpts);
    }

    [Fact]
    public void UseAuditTrail_EnableAuditFailedOperations_SetsFlag()
    {
        var opts = new CrudKitApiOptions();

        opts.UseAuditTrail().EnableAuditFailedOperations();

        Assert.True(opts.AuditTrailEnabled);
        Assert.True(opts.AuditFailedOperations);
    }

    [Fact]
    public void UseMultiTenancy_ReturnsMultiTenancyOptions()
    {
        var opts = new CrudKitApiOptions();
        var mtOpts = opts.UseMultiTenancy();
        Assert.NotNull(mtOpts);
    }

    [Fact]
    public void FluentChaining_AllMethodsReturnSameInstance()
    {
        var opts = new CrudKitApiOptions();

        var result = opts
            .UseExport()
            .UseImport()
            .UseEnumAsString()
            .UseGlobalHook<TestGlobalHook>();

        Assert.Same(opts, result);
        Assert.True(opts.ExportEnabled);
        Assert.True(opts.ImportEnabled);
        Assert.True(opts.EnumAsStringEnabled);
        Assert.Single(opts.GlobalHookTypes);
    }
}
