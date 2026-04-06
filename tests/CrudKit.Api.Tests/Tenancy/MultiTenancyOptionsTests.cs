using CrudKit.Api.Configuration;
using Xunit;

namespace CrudKit.Api.Tests.Tenancy;

/// <summary>
/// Unit tests for MultiTenancyOptions resolver methods.
/// Each test verifies that the corresponding Resolve* call sets TenantResolver on CrudKitApiOptions.
/// </summary>
public class MultiTenancyOptionsTests
{
    private static CrudKitApiOptions CreateOptions() => new CrudKitApiOptions();

    [Fact]
    public void ResolveTenantFromClaim_SetsResolver()
    {
        var opts = CreateOptions();
        opts.UseMultiTenancy().ResolveTenantFromClaim("tenant_id");

        // TenantResolver is internal — accessible because tests reference the same assembly.
        Assert.NotNull(opts.TenantResolver);
    }

    [Fact]
    public void ResolveTenantFromSubdomain_SetsResolver()
    {
        var opts = CreateOptions();
        opts.UseMultiTenancy().ResolveTenantFromSubdomain();

        Assert.NotNull(opts.TenantResolver);
    }

    [Fact]
    public void ResolveTenantFromRoute_SetsResolver()
    {
        var opts = CreateOptions();
        opts.UseMultiTenancy().ResolveTenantFromRoute("tenantId");

        Assert.NotNull(opts.TenantResolver);
    }

    [Fact]
    public void ResolveTenantFromQuery_SetsResolver()
    {
        var opts = CreateOptions();
        opts.UseMultiTenancy().ResolveTenantFromQuery("tenant");

        Assert.NotNull(opts.TenantResolver);
    }

    [Fact]
    public void ResolveTenantFromHeader_SetsResolver()
    {
        var opts = CreateOptions();
        opts.UseMultiTenancy().ResolveTenantFromHeader("X-Tenant-Id");

        Assert.NotNull(opts.TenantResolver);
    }

    [Fact]
    public void RejectUnresolvedTenant_SetsFlag()
    {
        var opts = CreateOptions();
        opts.UseMultiTenancy().RejectUnresolvedTenant();

        Assert.True(opts.TenantRejectUnresolved);
    }

    [Fact]
    public void RejectUnresolvedTenant_DefaultIsFalse()
    {
        var opts = CreateOptions();
        // Without calling RejectUnresolvedTenant the flag must default to false
        Assert.False(opts.TenantRejectUnresolved);
    }

    [Fact]
    public void UseMultiTenancy_ReturnsChainingInstance()
    {
        // Chaining multiple resolver calls: last one wins (overwrites TenantResolver)
        var opts = CreateOptions();
        opts.UseMultiTenancy()
            .ResolveTenantFromHeader("X-Tenant-Id")
            .ResolveTenantFromQuery("tenant")
            .RejectUnresolvedTenant();

        Assert.NotNull(opts.TenantResolver);
        Assert.True(opts.TenantRejectUnresolved);
    }
}
