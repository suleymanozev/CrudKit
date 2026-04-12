using CrudKit.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.Integration.Tests.Fixtures;

public class IntegrationDbContext : CrudKitDbContext
{
    public IntegrationDbContext(
        DbContextOptions<IntegrationDbContext> options,
        CrudKitDbContextDependencies deps)
        : base(options, deps) { }

    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderLineEntity> OrderLines => Set<OrderLineEntity>();
    public DbSet<TenantAwareItem> TenantAwareItems => Set<TenantAwareItem>();
    public DbSet<TenantUniqueItem> TenantUniqueItems => Set<TenantUniqueItem>();
    public DbSet<IndexedItem> IndexedItems => Set<IndexedItem>();

    protected override void OnModelCreatingCustom(ModelBuilder modelBuilder)
    {
        // Only apply schema for providers that support it
        if (Database.ProviderName?.Contains("Sqlite") != true)
            modelBuilder.HasDefaultSchema("integration_test");
    }
}
