using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Repository;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using CrudKit.Core.Tenancy;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Repository;

public class EfRepoTenantGuardTests
{
    // ---- Helpers ----

    /// <summary>
    /// Creates an EfRepo for TenantPersonEntity with no tenant set (simulates missing tenant context).
    /// </summary>
    private static (TestDbContext db, EfRepo<TenantPersonEntity> repo) CreateTenantRepoNoTenant()
    {
        var db = DbHelper.CreateDb(); // tenantContext = null → CurrentTenantId will be null
        var dialect = DialectDetector.Detect(db);
        var filterApplier = new FilterApplier(dialect);
        var queryBuilder = new QueryBuilder<TenantPersonEntity>(filterApplier);
        var repo = new EfRepo<TenantPersonEntity>(DbHelper.WrapAsServiceProvider(db), queryBuilder, filterApplier);
        return (db, repo);
    }

    /// <summary>
    /// Creates an EfRepo for TenantPersonEntity with a tenant context set to "tenant-1".
    /// </summary>
    private static (TestDbContext db, EfRepo<TenantPersonEntity> repo) CreateTenantRepoWithTenant(string tenantId = "tenant-1")
    {
        var tenantContext = new TenantContext { TenantId = tenantId };
        var db = DbHelper.CreateDb(tenantContext: tenantContext);
        var dialect = DialectDetector.Detect(db);
        var filterApplier = new FilterApplier(dialect);
        var queryBuilder = new QueryBuilder<TenantPersonEntity>(filterApplier);
        var repo = new EfRepo<TenantPersonEntity>(DbHelper.WrapAsServiceProvider(db), queryBuilder, filterApplier);
        return (db, repo);
    }

    /// <summary>
    /// Creates an EfRepo for PersonEntity (non-multi-tenant) with no tenant context.
    /// </summary>
    private static (TestDbContext db, EfRepo<PersonEntity> repo) CreateNonTenantRepo()
    {
        var db = DbHelper.CreateDb(); // no tenant context
        var dialect = DialectDetector.Detect(db);
        var filterApplier = new FilterApplier(dialect);
        var queryBuilder = new QueryBuilder<PersonEntity>(filterApplier);
        var repo = new EfRepo<PersonEntity>(DbHelper.WrapAsServiceProvider(db), queryBuilder, filterApplier);
        return (db, repo);
    }

    // ---- Tests: IMultiTenant entity with null tenant → must throw 400 ----

    [Fact]
    public async Task List_ThrowsBadRequest_WhenTenantNullForMultiTenantEntity()
    {
        var (_, repo) = CreateTenantRepoNoTenant();

        var ex = await Assert.ThrowsAsync<AppError>(() =>
            repo.List(new ListParams { Page = 1, PerPage = 10 }));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Tenant context is required", ex.Message);
    }

    [Fact]
    public async Task Create_ThrowsBadRequest_WhenTenantNullForMultiTenantEntity()
    {
        var (_, repo) = CreateTenantRepoNoTenant();

        var ex = await Assert.ThrowsAsync<AppError>(() =>
            repo.Create(new { Name = "Alice" }));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Tenant context is required", ex.Message);
    }

    [Fact]
    public async Task FindById_ThrowsBadRequest_WhenTenantNullForMultiTenantEntity()
    {
        var (_, repo) = CreateTenantRepoNoTenant();

        var ex = await Assert.ThrowsAsync<AppError>(() =>
            repo.FindById(Guid.NewGuid()));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Tenant context is required", ex.Message);
    }

    [Fact]
    public async Task FindByIdOrDefault_ThrowsBadRequest_WhenTenantNullForMultiTenantEntity()
    {
        var (_, repo) = CreateTenantRepoNoTenant();

        var ex = await Assert.ThrowsAsync<AppError>(() =>
            repo.FindByIdOrDefault(Guid.NewGuid()));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Tenant context is required", ex.Message);
    }

    // ---- Tests: non-IMultiTenant entity with null tenant → works fine ----

    [Fact]
    public async Task List_WorksNormally_WhenTenantNullForNonMultiTenantEntity()
    {
        var (_, repo) = CreateNonTenantRepo();

        // Should not throw
        var result = await repo.List(new ListParams { Page = 1, PerPage = 10 });
        Assert.NotNull(result);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task Create_WorksNormally_WhenTenantNullForNonMultiTenantEntity()
    {
        var (_, repo) = CreateNonTenantRepo();

        // Should not throw
        var result = await repo.Create(new { Name = "Alice", Age = 30 });
        Assert.NotNull(result);
        Assert.Equal("Alice", result.Name);
    }

    // ---- Tests: IMultiTenant entity WITH a valid tenant → works fine ----

    [Fact]
    public async Task List_WorksNormally_WhenTenantSetForMultiTenantEntity()
    {
        var (_, repo) = CreateTenantRepoWithTenant("tenant-1");

        // Should not throw
        var result = await repo.List(new ListParams { Page = 1, PerPage = 10 });
        Assert.NotNull(result);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task Create_WorksNormally_WhenTenantSetForMultiTenantEntity()
    {
        var (_, repo) = CreateTenantRepoWithTenant("tenant-1");

        var result = await repo.Create(new { Name = "Bob" });
        Assert.NotNull(result);
        Assert.Equal("Bob", result.Name);
        Assert.Equal("tenant-1", result.TenantId);
    }
}
