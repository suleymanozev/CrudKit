using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Extensions;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Repository;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser());
        services.AddDbContext<TestDbContext>((sp, opts) =>
            opts.UseSqlite($"Data Source={Guid.NewGuid()};Mode=Memory;Cache=Shared"));
        services.AddCrudKitEf<TestDbContext>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddCrudKitEf_RegistersIDbDialect()
    {
        using var sp = BuildProvider() as ServiceProvider;
        using var scope = sp!.CreateScope();
        var dialect = scope.ServiceProvider.GetRequiredService<IDbDialect>();
        Assert.NotNull(dialect);
    }

    [Fact]
    public void AddCrudKitEf_RegistersFilterApplier()
    {
        using var sp = BuildProvider() as ServiceProvider;
        using var scope = sp!.CreateScope();
        var applier = scope.ServiceProvider.GetRequiredService<FilterApplier>();
        Assert.NotNull(applier);
    }

    [Fact]
    public void AddCrudKitEf_RegistersOpenGenericQueryBuilder()
    {
        using var sp = BuildProvider() as ServiceProvider;
        using var scope = sp!.CreateScope();
        var qb = scope.ServiceProvider.GetRequiredService<QueryBuilder<PersonEntity>>();
        Assert.NotNull(qb);
    }

    [Fact]
    public void AddCrudKitEf_RegistersOpenGenericIRepo()
    {
        using var sp = BuildProvider() as ServiceProvider;
        using var scope = sp!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepo<PersonEntity>>();
        Assert.IsType<EfRepo<PersonEntity>>(repo);
    }

}
