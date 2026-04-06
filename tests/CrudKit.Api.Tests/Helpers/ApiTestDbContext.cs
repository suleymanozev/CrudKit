using CrudKit.Api.Models;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.Api.Tests.Helpers;

public class ApiTestDbContext : CrudKitDbContext
{
    public ApiTestDbContext(DbContextOptions<ApiTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<ProductEntity> Products => Set<ProductEntity>();
    public DbSet<SoftProductEntity> SoftProducts => Set<SoftProductEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<ConcurrentEntity> Concurrents => Set<ConcurrentEntity>();
    public DbSet<InvoiceEntity> Invoices => Set<InvoiceEntity>();
    public DbSet<InvoiceLineEntity> InvoiceLines => Set<InvoiceLineEntity>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<NoFlagEntity> NoFlagEntities => Set<NoFlagEntity>();
    public DbSet<OptOutEntity> OptOutEntities => Set<OptOutEntity>();

    protected override void OnModelCreatingCustom(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("__crud_idempotency");
            b.HasKey(e => e.Id);
            b.Property(e => e.Key).HasMaxLength(500).IsRequired();
            b.Property(e => e.Path).HasMaxLength(500).IsRequired();
            b.Property(e => e.Method).HasMaxLength(10).IsRequired();
            b.HasIndex(e => new { e.Key, e.TenantId }).IsUnique();
            b.HasIndex(e => e.ExpiresAt);
        });
    }
}
